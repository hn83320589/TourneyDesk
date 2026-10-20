using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Locking;
using TourneyDesk.Data;
using TourneyDesk.Data.Locking;
using TourneyDesk.UI.Services;

namespace TourneyDesk.App;

/// <summary>以 Data 層實作開檔：先取得鎖定，再開啟 SQLite 賽事檔；任何失敗都會釋放已取得的鎖定。</summary>
internal sealed class FileGateway(TimeProvider time) : ITournamentGateway
{
    public LockDecision CheckLock(string path) => Manager(path).Check();

    public OpenedTournament Open(string path, bool confirmedStaleTakeover, string? forceConfirmation)
    {
        var handle = Manager(path).Acquire(confirmedStaleTakeover, forceConfirmation);
        try
        {
            var (file, state) = TournamentFile.Open(path);
            return Wrap(path, file, state, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public OpenedTournament Create(string path, TournamentState initial)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        var handle = Manager(path).Acquire();
        try
        {
            return Wrap(path, TournamentFile.Create(path, initial), initial, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static OpenedTournament Wrap(string path, TournamentFile file, TournamentState state, ILockHandle handle)
    {
        var opened = new OpenedTournament(path, state, file, file.ReadAudit, new Resources(file, handle));
        file.Warning += (_, message) => opened.RaiseWarning(message);
        return opened;
    }

    private LockManager Manager(string path) =>
        new(new FileSystemLockFile(path), new SystemProcessInspector(), time, SystemProcessInspector.Current());

    /// <summary>先關檔再釋放鎖定。</summary>
    private sealed class Resources(TournamentFile file, ILockHandle handle) : IDisposable
    {
        public void Dispose()
        {
            file.Dispose();
            handle.Dispose();
        }
    }
}
