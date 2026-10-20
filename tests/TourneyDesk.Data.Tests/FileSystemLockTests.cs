using System.Text.Json;
using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Tests.TestSupport;
using TourneyDesk.Data.Locking;

namespace TourneyDesk.Data.Tests;

/// <summary>實際檔案系統上的鎖定行為（同一程序內以兩個 handle 模擬兩個程式）。</summary>
public class FileSystemLockTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ManualTimeProvider _time = new();

    public void Dispose() => _dir.Dispose();

    private LockManager Manager(string path, ProcessIdentity identity) =>
        new(new FileSystemLockFile(path), new SystemProcessInspector(), _time, identity);

    [Fact]
    public void Second_opener_sees_lock_held_with_holder_details()
    {
        var path = _dir.File("a.tourney");
        var self = SystemProcessInspector.Current();
        using var first = Manager(path, self).Acquire();

        var probe = new FileSystemLockFile(path).Probe();
        var decision = Manager(path, self).Check();

        Assert.Equal(LockProbeKind.Held, probe.Kind);
        Assert.Equal(self.ProcessId, probe.Holder!.ProcessId);
        Assert.Equal(LockDecisionKind.Denied, decision.Kind);
        Assert.Contains(Environment.MachineName, decision.Message);
        Assert.Throws<LockUnavailableException>(() => new FileSystemLockFile(path).Acquire(first.Info));
    }

    [Fact]
    public void Closing_releases_and_removes_lock_file()
    {
        var path = _dir.File("b.tourney");
        var lockFile = new FileSystemLockFile(path);

        Manager(path, SystemProcessInspector.Current()).Acquire().Dispose();

        Assert.False(File.Exists(lockFile.LockPath));
        Assert.Equal(LockProbeKind.None, lockFile.Probe().Kind);
    }

    [Fact]
    public void Leftover_lock_from_dead_local_process_is_stale()
    {
        var path = _dir.File("c.tourney");
        var lockFile = new FileSystemLockFile(path);
        // 模擬當掉：鎖定檔留下但沒有人持有 handle；PID 與啟動時間對應不到現存程序
        var dead = new LockInfo(Environment.MachineName, 999_999, DateTimeOffset.UnixEpoch, _time.GetUtcNow(), Guid.NewGuid());
        File.WriteAllText(lockFile.LockPath, JsonSerializer.Serialize(dead));

        var manager = Manager(path, SystemProcessInspector.Current());

        Assert.Equal(LockDecisionKind.ConfirmStaleTakeover, manager.Check().Kind);
        using var handle = manager.Acquire(confirmedStaleTakeover: true);
        Assert.Equal(Environment.ProcessId, handle.Info.ProcessId);
    }

    [Fact]
    public void Leftover_lock_from_other_machine_requires_force_text()
    {
        var path = _dir.File("d.tourney");
        var lockFile = new FileSystemLockFile(path);
        var foreign = new LockInfo("OTHER-PC", 1234, DateTimeOffset.UnixEpoch, _time.GetUtcNow(), Guid.NewGuid());
        File.WriteAllText(lockFile.LockPath, JsonSerializer.Serialize(foreign));

        var manager = Manager(path, SystemProcessInspector.Current());

        Assert.Equal(LockDecisionKind.RequireForceConfirmation, manager.Check().Kind);
        Assert.Throws<LockUnavailableException>(() => manager.Acquire(confirmedStaleTakeover: true));
        using var handle = manager.Acquire(forceConfirmation: LockArbiter.ForceConfirmationText);
        Assert.Equal(Environment.MachineName, handle.Info.MachineName);
    }

    [Fact]
    public void Corrupt_lock_content_is_reported_as_unknown_holder()
    {
        var path = _dir.File("e.tourney");
        var lockFile = new FileSystemLockFile(path);
        File.WriteAllText(lockFile.LockPath, "{not json");

        var probe = lockFile.Probe();

        Assert.Equal(LockProbeKind.Unheld, probe.Kind);
        Assert.Null(probe.Holder);
    }

    [Fact]
    public void Current_process_is_reported_alive_and_bogus_pid_dead()
    {
        var self = SystemProcessInspector.Current();
        var inspector = new SystemProcessInspector();

        Assert.True(inspector.IsAlive(self.ProcessId, self.ProcessStartTime));
        Assert.False(inspector.IsAlive(self.ProcessId, self.ProcessStartTime.AddHours(-3)));
        Assert.False(inspector.IsAlive(999_999, DateTimeOffset.UnixEpoch));
    }
}
