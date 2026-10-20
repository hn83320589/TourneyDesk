using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Session;

public class TournamentSessionTests
{
    private readonly InMemoryStore _store = new();
    private readonly ManualTimeProvider _time = new();

    private TournamentSession NewSession(int undoCapacity = TournamentSession.DefaultUndoCapacity) =>
        new(TournamentState.Create(TournamentSettings.Create("春季賽", TournamentFormat.Swiss)), _store, _time, undoCapacity);

    [Fact]
    public void Execute_applies_command_and_commits_with_audit()
    {
        var session = NewSession();

        var result = session.Execute(new AddPlayer("王小明", 1500));

        Assert.True(result.Succeeded);
        Assert.Single(session.State.Players);
        var commit = Assert.Single(_store.Commits);
        Assert.Same(session.State, commit.After);
        var audit = Assert.Single(commit.Audit);
        Assert.Equal(_time.GetUtcNow(), audit.Timestamp);
    }

    [Fact]
    public void Rule_violation_leaves_state_unchanged_and_writes_nothing()
    {
        var session = NewSession();
        var before = session.State;

        var result = session.Execute(new AddPlayer("", 1500));

        Assert.False(result.Succeeded);
        Assert.Equal("姓名不可空白。", result.Error);
        Assert.Same(before, session.State);
        Assert.Empty(_store.Commits);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Storage_failure_leaves_state_unchanged_and_reports_readable_message()
    {
        var session = NewSession();
        var before = session.State;
        _store.FailNextCommit = true;

        var result = session.Execute(new AddPlayer("王小明", 1500));

        Assert.False(result.Succeeded);
        Assert.Contains("模擬的磁碟錯誤", result.Error);
        Assert.Same(before, session.State);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_restores_previous_state_and_is_itself_audited()
    {
        var session = NewSession();
        var before = session.State;
        session.Execute(new AddPlayer("王小明", 1500));

        var result = session.Undo();

        Assert.True(result.Succeeded);
        Assert.Equal(StateHasher.Compute(before), StateHasher.Compute(session.State));
        Assert.Equal(2, _store.Commits.Count);
        Assert.Equal("復原", _store.Commits[1].Audit.Single().Kind);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Redo_reapplies_undone_state()
    {
        var session = NewSession();
        session.Execute(new AddPlayer("王小明", 1500));
        var after = StateHasher.Compute(session.State);
        session.Undo();

        var result = session.Redo();

        Assert.True(result.Succeeded);
        Assert.Equal(after, StateHasher.Compute(session.State));
        Assert.Equal("重做", _store.Commits[^1].Audit.Single().Kind);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void New_command_clears_redo_stack()
    {
        var session = NewSession();
        session.Execute(new AddPlayer("王小明", 1500));
        session.Undo();

        session.Execute(new AddPlayer("李大華", 1400));

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Undo_supports_at_least_fifty_levels()
    {
        var session = NewSession();
        var hashes = new List<string> { StateHasher.Compute(session.State) };
        for (var i = 0; i < 60; i++)
        {
            session.Execute(new AddPlayer($"選手{i}", 1000 + i));
            hashes.Add(StateHasher.Compute(session.State));
        }

        for (var i = 59; i >= 0; i--)
        {
            Assert.True(session.Undo().Succeeded);
            Assert.Equal(hashes[i], StateHasher.Compute(session.State));
        }

        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_history_is_bounded_by_capacity()
    {
        var session = NewSession(undoCapacity: 3);
        for (var i = 0; i < 5; i++)
            session.Execute(new AddPlayer($"選手{i}", 1000));

        var undone = 0;
        while (session.Undo().Succeeded)
            undone++;

        Assert.Equal(3, undone);
        Assert.Equal(2, session.State.Players.Count);
    }

    [Fact]
    public void Undo_failure_in_storage_keeps_history_intact()
    {
        var session = NewSession();
        session.Execute(new AddPlayer("王小明", 1500));
        var current = session.State;
        _store.FailNextCommit = true;

        var result = session.Undo();

        Assert.False(result.Succeeded);
        Assert.Same(current, session.State);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo().Succeeded);
    }

    [Fact]
    public void Background_work_blocks_commands_undo_and_redo()
    {
        var session = NewSession();
        session.Execute(new AddPlayer("王小明", 1500));

        using (session.BeginBackgroundWork("配對中"))
        {
            Assert.False(session.Execute(new AddPlayer("李大華", 1400)).Succeeded);
            var undo = session.Undo();
            Assert.False(undo.Succeeded);
            Assert.Contains("配對中", undo.Error);
        }

        Assert.True(session.Undo().Succeeded);
    }

    [Fact]
    public void Undo_and_redo_descriptions_follow_history()
    {
        var session = NewSession();
        session.Execute(new AddPlayer("王小明", 1500));

        Assert.Equal("新增選手 #1 王小明", session.UndoDescription);
        session.Undo();
        Assert.Null(session.UndoDescription);
        Assert.Equal("新增選手 #1 王小明", session.RedoDescription);
    }

    [Fact]
    public void State_changed_event_fires_on_every_successful_transition()
    {
        var session = NewSession();
        var count = 0;
        session.StateChanged += (_, _) => count++;

        session.Execute(new AddPlayer("王小明", 1500));
        session.Execute(new AddPlayer("", 1500));
        session.Undo();
        session.Redo();

        Assert.Equal(3, count);
    }
}
