using System.Globalization;

namespace TourneyDesk.Core.Locking;

public enum LockDecisionKind
{
    /// <summary>可以直接開啟。</summary>
    Acquire,

    /// <summary>不可開啟（另一個程式確實正在使用）。</summary>
    Denied,

    /// <summary>殘留鎖（同一台電腦、持有者程序已不存在）：使用者確認後可接手。</summary>
    ConfirmStaleTakeover,

    /// <summary>持有者在其他電腦（或無法判讀）：不得自動接手，需輸入固定確認字串才能強制接手。</summary>
    RequireForceConfirmation,
}

public sealed record LockDecision(LockDecisionKind Kind, LockInfo? Holder, string Message);

/// <summary>
/// 開檔鎖定的判定規則（純邏輯）：
/// <list type="table">
/// <item>沒有鎖定檔 → 直接開啟。</item>
/// <item>鎖定檔 handle 被持有 → 拒絕，顯示持有者。</item>
/// <item>未被持有、記錄為本機且程序仍存在 → 拒絕。</item>
/// <item>未被持有、記錄為本機且程序已不存在 → 殘留鎖，確認後接手。</item>
/// <item>未被持有、記錄為其他電腦（或內容無法判讀）→ 輸入固定確認字串才能強制接手。</item>
/// </list>
/// </summary>
public static class LockArbiter
{
    public const string ForceConfirmationText = "我確定要強制接手";

    public static LockDecision Decide(LockProbe probe, ProcessIdentity self, IProcessInspector inspector)
    {
        switch (probe.Kind)
        {
            case LockProbeKind.None:
                return new LockDecision(LockDecisionKind.Acquire, null, "");
            case LockProbeKind.Held when IsSelf(probe.Holder, self):
                return new LockDecision(LockDecisionKind.Denied, probe.Holder, "此賽事檔已在本程式中開啟。");
            case LockProbeKind.Held:
                return new LockDecision(LockDecisionKind.Denied, probe.Holder,
                    $"此賽事檔正由{HolderText(probe.Holder)}使用中，無法開啟。");
        }

        if (probe.Holder is not { } holder)
        {
            return new LockDecision(LockDecisionKind.RequireForceConfirmation, null,
                $"此賽事檔留有無法判讀的鎖定紀錄。若確定沒有其他人正在使用，請輸入「{ForceConfirmationText}」強制接手。");
        }

        var sameMachine = string.Equals(holder.MachineName, self.MachineName, StringComparison.OrdinalIgnoreCase);
        if (!sameMachine)
        {
            return new LockDecision(LockDecisionKind.RequireForceConfirmation, holder,
                $"此賽事檔的鎖定紀錄屬於{HolderText(holder)}。若確定該電腦已不再使用此檔，請輸入「{ForceConfirmationText}」強制接手。");
        }

        if (inspector.IsAlive(holder.ProcessId, holder.ProcessStartTime))
        {
            return new LockDecision(LockDecisionKind.Denied, holder,
                $"此賽事檔正由{HolderText(holder)}使用中，無法開啟。");
        }

        return new LockDecision(LockDecisionKind.ConfirmStaleTakeover, holder,
            $"發現殘留的鎖定紀錄（{HolderText(holder)}，該程序已不存在）。是否接手？");
    }

    private static bool IsSelf(LockInfo? holder, ProcessIdentity self) =>
        holder is not null && holder.ProcessId == self.ProcessId
        && string.Equals(holder.MachineName, self.MachineName, StringComparison.OrdinalIgnoreCase);

    public static string HolderText(LockInfo? holder) =>
        holder is null
            ? "另一個程式"
            : string.Create(CultureInfo.InvariantCulture,
                $"電腦「{holder.MachineName}」的程序 {holder.ProcessId}（開啟時間 {holder.OpenedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}）");
}

/// <summary>開檔鎖定流程：先判定，再依使用者的回應取得鎖定。</summary>
public sealed class LockManager(ILockFile lockFile, IProcessInspector inspector, TimeProvider time, ProcessIdentity self)
{
    public LockDecision Check() => LockArbiter.Decide(lockFile.Probe(), self, inspector);

    /// <summary>
    /// 取得鎖定。會重新判定一次（狀態可能在詢問使用者期間改變）：
    /// 殘留鎖需 <paramref name="confirmedStaleTakeover"/>；他機鎖需 <paramref name="forceConfirmation"/> 等於固定字串。
    /// </summary>
    public ILockHandle Acquire(bool confirmedStaleTakeover = false, string? forceConfirmation = null)
    {
        var decision = Check();
        switch (decision.Kind)
        {
            case LockDecisionKind.Denied:
                throw new LockUnavailableException(decision.Message);
            case LockDecisionKind.ConfirmStaleTakeover when !confirmedStaleTakeover:
                throw new LockUnavailableException("需要確認後才能接手殘留的鎖定。");
            case LockDecisionKind.RequireForceConfirmation when forceConfirmation != LockArbiter.ForceConfirmationText:
                throw new LockUnavailableException($"確認字串不正確，必須輸入「{LockArbiter.ForceConfirmationText}」。");
        }

        var info = new LockInfo(self.MachineName, self.ProcessId, self.ProcessStartTime, time.GetUtcNow(), Guid.NewGuid());
        return lockFile.Acquire(info);
    }
}
