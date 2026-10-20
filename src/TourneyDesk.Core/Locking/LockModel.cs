namespace TourneyDesk.Core.Locking;

/// <summary>鎖定檔中記錄的持有者。</summary>
public sealed record LockInfo(string MachineName, int ProcessId, DateTimeOffset ProcessStartTime, DateTimeOffset OpenedAt, Guid SessionId);

public enum LockProbeKind
{
    /// <summary>沒有鎖定檔。</summary>
    None,

    /// <summary>鎖定檔的 handle 正被持有（另一個程式確實開著）。</summary>
    Held,

    /// <summary>鎖定檔存在但沒有人持有 handle（持有者已關閉異常、當機或斷線）。</summary>
    Unheld,
}

/// <param name="Holder">鎖定檔內容；無法讀取（損毀、權限）時為 null。</param>
public sealed record LockProbe(LockProbeKind Kind, LockInfo? Holder)
{
    public static LockProbe None { get; } = new(LockProbeKind.None, null);
}

/// <summary>鎖定檔的 I/O（Data 層實作；測試用假實作）。</summary>
public interface ILockFile
{
    LockProbe Probe();

    /// <summary>寫入 <paramref name="info"/> 並持有鎖定檔直到 Dispose。若 handle 已被他人持有則丟出 <see cref="LockUnavailableException"/>。</summary>
    ILockHandle Acquire(LockInfo info);
}

public interface ILockHandle : IDisposable
{
    LockInfo Info { get; }
}

/// <summary>「程序是否仍存在」（同一台電腦）。以 PID + 程序啟動時間比對，避免 PID 被重用時誤判。</summary>
public interface IProcessInspector
{
    bool IsAlive(int processId, DateTimeOffset processStartTime);
}

/// <summary>目前程式自身的身分。</summary>
public sealed record ProcessIdentity(string MachineName, int ProcessId, DateTimeOffset ProcessStartTime);

public sealed class LockUnavailableException(string message) : Exception(message);
