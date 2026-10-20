using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>記錄每次 Commit 的假檔案層；可設定讓下一次寫入失敗。</summary>
internal sealed class InMemoryStore : ITournamentStore
{
    public List<(TournamentState Before, TournamentState After, IReadOnlyList<AuditEntry> Audit)> Commits { get; } = [];

    public List<AuditEntry> AuditLog { get; } = [];

    public bool FailNextCommit { get; set; }

    public void Commit(TournamentState before, TournamentState after, IReadOnlyList<AuditEntry> audit)
    {
        if (FailNextCommit)
        {
            FailNextCommit = false;
            throw new StorageException("模擬的磁碟錯誤");
        }

        Commits.Add((before, after, audit));
        AuditLog.AddRange(audit);
    }
}
