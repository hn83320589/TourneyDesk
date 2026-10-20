namespace TourneyDesk.Core.Session;

/// <summary>只能新增的異動紀錄。</summary>
/// <param name="Kind">異動種類，例如「成績更正」「手動交換」「復原」。</param>
/// <param name="Target">異動對象，例如「第 3 輪 第 5 桌」。</param>
public sealed record AuditEntry(DateTimeOffset Timestamp, string Kind, string Target, string? Before, string? After);
