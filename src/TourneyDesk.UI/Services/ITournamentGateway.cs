using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Session;

namespace TourneyDesk.UI.Services;

/// <summary>開檔、建檔與鎖定（App 以 Data 層實作；測試用假實作）。</summary>
public interface ITournamentGateway
{
    LockDecision CheckLock(string path);

    /// <summary>取得鎖定並開啟。失敗時丟出 <see cref="StorageException"/> 或 <see cref="LockUnavailableException"/>。</summary>
    OpenedTournament Open(string path, bool confirmedStaleTakeover, string? forceConfirmation);

    OpenedTournament Create(string path, TournamentState initial);
}

/// <summary>一個已開啟（並已鎖定）的賽事檔。Dispose 時關閉檔案並釋放鎖定。</summary>
public sealed class OpenedTournament(
    string path,
    TournamentState state,
    ITournamentStore store,
    Func<IReadOnlyList<AuditEntry>> readAudit,
    IDisposable resources) : IDisposable
{
    public string Path { get; } = path;

    public TournamentState State { get; } = state;

    public ITournamentStore Store { get; } = store;

    /// <summary>非致命警告（例如自動備份失敗）的接收者，由 <see cref="Workspace"/> 設定。</summary>
    public Action<string>? OnWarning { get; set; }

    public IReadOnlyList<AuditEntry> ReadAudit() => readAudit();

    public void RaiseWarning(string message) => OnWarning?.Invoke(message);

    public void Dispose() => resources.Dispose();
}
