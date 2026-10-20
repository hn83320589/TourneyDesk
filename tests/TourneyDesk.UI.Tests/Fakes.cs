using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Session;
using TourneyDesk.UI.Services;

namespace TourneyDesk.UI.Tests;

internal sealed class MemoryStore : ITournamentStore
{
    public List<AuditEntry> Audit { get; } = [];

    public int Commits { get; private set; }

    public void Commit(TournamentState before, TournamentState after, IReadOnlyList<AuditEntry> audit)
    {
        Commits++;
        Audit.AddRange(audit);
    }
}

/// <summary>記憶體版的開檔閘道：可預先放入賽事，並指定鎖定判定結果。</summary>
internal sealed class FakeGateway : ITournamentGateway
{
    public Dictionary<string, TournamentState> Files { get; } = [];

    public LockDecision Lock { get; set; } = new(LockDecisionKind.Acquire, null, "");

    public List<(string Path, bool Stale, string? Force)> Opens { get; } = [];

    public MemoryStore Store { get; } = new();

    public LockDecision CheckLock(string path) => Lock;

    public OpenedTournament Open(string path, bool confirmedStaleTakeover, string? forceConfirmation)
    {
        Opens.Add((path, confirmedStaleTakeover, forceConfirmation));
        if (!Files.TryGetValue(path, out var state))
            throw new StorageException($"找不到檔案：{path}");
        return new OpenedTournament(path, state, Store, () => Store.Audit, new Noop());
    }

    public OpenedTournament Create(string path, TournamentState initial)
    {
        Files[path] = initial;
        return new OpenedTournament(path, initial, Store, () => Store.Audit, new Noop());
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class FakeDialogs : IFileDialogService
{
    public string? OpenPath { get; set; }

    public string? CreatePath { get; set; } = @"C:\賽事\測試.tourney";

    public string? CsvPath { get; set; }

    public string? SavePath { get; set; }

    public string? PickTournamentToOpen() => OpenPath;

    public string? PickTournamentToCreate(string suggestedName) => CreatePath;

    public string? PickCsvToImport() => CsvPath;

    public string? PickSaveLocation(string suggestedName, string filter) => SavePath;
}

internal sealed class FakePrinter : IPrintService
{
    public int Calls { get; private set; }

    public Task PrintAsync()
    {
        Calls++;
        return Task.CompletedTask;
    }
}
