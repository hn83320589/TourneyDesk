using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Locking;

/// <summary>驗收 19：以注入的時鐘、程序檢查與假鎖定檔驗證鎖定規則。</summary>
public class LockManagerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeLockFile _file = new();
    private readonly FakeProcesses _processes = new();
    private readonly ManualTimeProvider _time = new();

    private LockManager Manager(string machine = "PC-A", int pid = 100) =>
        new(_file, _processes, _time, new ProcessIdentity(machine, pid, Start));

    [Fact]
    public void First_open_acquires_and_records_holder_with_injected_time()
    {
        using var handle = Manager().Acquire();

        Assert.Equal(("PC-A", 100, _time.GetUtcNow()), (handle.Info.MachineName, handle.Info.ProcessId, handle.Info.OpenedAt));
        Assert.Equal(LockProbeKind.Held, _file.Probe().Kind);
    }

    [Fact]
    public void Second_program_is_denied_and_sees_holder()
    {
        _processes.Alive.Add((100, Start));
        using var first = Manager(pid: 100).Acquire();

        var decision = Manager(pid: 200).Check();

        Assert.Equal(LockDecisionKind.Denied, decision.Kind);
        Assert.Equal(100, decision.Holder!.ProcessId);
        Assert.Contains("PC-A", decision.Message);
        Assert.Contains("程序 100", decision.Message);
        Assert.Throws<LockUnavailableException>(() => Manager(pid: 200).Acquire(confirmedStaleTakeover: true));
    }

    [Fact]
    public void Held_lock_from_other_machine_cannot_be_forced()
    {
        using var first = Manager("PC-B").Acquire();

        var second = Manager("PC-A");

        Assert.Equal(LockDecisionKind.Denied, second.Check().Kind);
        Assert.Throws<LockUnavailableException>(() => second.Acquire(forceConfirmation: LockArbiter.ForceConfirmationText));
    }

    [Fact]
    public void Normal_close_releases_lock()
    {
        Manager().Acquire().Dispose();

        Assert.Equal(LockProbeKind.None, _file.Probe().Kind);
        Assert.Equal(LockDecisionKind.Acquire, Manager(pid: 300).Check().Kind);
    }

    [Fact]
    public void Crash_on_same_machine_leaves_stale_lock_that_needs_confirmation()
    {
        Manager(pid: 100).Acquire();
        _file.SimulateCrash(); // handle 被 OS 釋放，但鎖定檔留下；PID 100 已不存在

        var next = Manager(pid: 200);
        var decision = next.Check();

        Assert.Equal(LockDecisionKind.ConfirmStaleTakeover, decision.Kind);
        Assert.Contains("已不存在", decision.Message);
        Assert.Throws<LockUnavailableException>(() => next.Acquire());
        using var handle = next.Acquire(confirmedStaleTakeover: true);
        Assert.Equal(200, handle.Info.ProcessId);
    }

    [Fact]
    public void Unheld_lock_whose_process_is_still_alive_is_denied()
    {
        _processes.Alive.Add((100, Start));
        Manager(pid: 100).Acquire();
        _file.SimulateCrash();

        Assert.Equal(LockDecisionKind.Denied, Manager(pid: 200).Check().Kind);
    }

    [Fact]
    public void Reused_pid_with_different_start_time_is_treated_as_dead()
    {
        _processes.Alive.Add((100, Start.AddHours(5))); // 同 PID，但是另一個程序
        Manager(pid: 100).Acquire();
        _file.SimulateCrash();

        Assert.Equal(LockDecisionKind.ConfirmStaleTakeover, Manager(pid: 200).Check().Kind);
    }

    [Fact]
    public void Other_machine_lock_is_never_taken_over_automatically()
    {
        Manager("PC-B").Acquire();
        _file.SimulateCrash();

        var mine = Manager("PC-A");
        var decision = mine.Check();

        Assert.Equal(LockDecisionKind.RequireForceConfirmation, decision.Kind);
        Assert.Contains(LockArbiter.ForceConfirmationText, decision.Message);
        Assert.Throws<LockUnavailableException>(() => mine.Acquire(confirmedStaleTakeover: true));
        Assert.Throws<LockUnavailableException>(() => mine.Acquire(forceConfirmation: "確定"));
        using var handle = mine.Acquire(forceConfirmation: LockArbiter.ForceConfirmationText);
        Assert.Equal("PC-A", handle.Info.MachineName);
    }

    [Fact]
    public void Unreadable_lock_content_requires_force_confirmation()
    {
        _file.Corrupt();

        Assert.Equal(LockDecisionKind.RequireForceConfirmation, Manager().Check().Kind);
    }

    [Fact]
    public void Machine_names_are_compared_case_insensitively()
    {
        Manager("pc-a", 100).Acquire();
        _file.SimulateCrash();

        Assert.Equal(LockDecisionKind.ConfirmStaleTakeover, Manager("PC-A", 200).Check().Kind);
    }

    private sealed class FakeProcesses : IProcessInspector
    {
        public HashSet<(int, DateTimeOffset)> Alive { get; } = [];

        public bool IsAlive(int processId, DateTimeOffset processStartTime) => Alive.Contains((processId, processStartTime));
    }

    private sealed class FakeLockFile : ILockFile
    {
        private LockInfo? _content;
        private bool _exists;
        private bool _held;

        public LockProbe Probe() =>
            !_exists ? LockProbe.None : new LockProbe(_held ? LockProbeKind.Held : LockProbeKind.Unheld, _content);

        public ILockHandle Acquire(LockInfo info)
        {
            if (_held)
                throw new LockUnavailableException("held");
            (_exists, _held, _content) = (true, true, info);
            return new Handle(this, info);
        }

        public void SimulateCrash() => _held = false;

        public void Corrupt() => (_exists, _held, _content) = (true, false, null);

        private sealed class Handle(FakeLockFile owner, LockInfo info) : ILockHandle
        {
            public LockInfo Info => info;

            public void Dispose()
            {
                if (owner._held && owner._content == info)
                    (owner._exists, owner._held, owner._content) = (false, false, null);
            }
        }
    }
}
