using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Session;

/// <summary>執行命令時的環境資訊（由 Session 提供，命令本身不得自行取時間）。</summary>
public sealed record CommandContext(DateTimeOffset Now)
{
    public AuditEntry Audit(string kind, string target, string? before, string? after) =>
        new(Now, kind, target, before, after);
}

/// <summary>一次操作的結果：新狀態、描述（顯示於復原清單）與異動紀錄。</summary>
public sealed record Transition(TournamentState NewState, string Description, IReadOnlyList<AuditEntry> Audit);

/// <summary>
/// 所有改變賽事狀態的操作。實作必須是純函式：只依 <paramref name="state"/> 與命令內容決定結果；
/// 違反規則時丟出 <see cref="DomainException"/>。
/// </summary>
public interface ITournamentCommand
{
    Transition Apply(TournamentState state, CommandContext context);
}
