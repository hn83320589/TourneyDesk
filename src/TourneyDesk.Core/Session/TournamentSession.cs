using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Session;

public sealed record CommandResult(bool Succeeded, string? Error)
{
    public static CommandResult Ok { get; } = new(true, null);

    public static CommandResult Fail(string error) => new(false, error);
}

/// <summary>
/// 應用服務：所有狀態變更（含復原與重做）都走同一條管線——
/// 驗證 → 計算新狀態 → 單一交易寫入 → 更新記憶體狀態 → 記錄歷史。
/// 寫入失敗時記憶體狀態與歷史都維持原樣。
/// </summary>
public sealed class TournamentSession(
    TournamentState initial,
    ITournamentStore store,
    TimeProvider time,
    int undoCapacity = TournamentSession.DefaultUndoCapacity)
{
    public const int DefaultUndoCapacity = 100;

    private readonly UndoHistory _history = new(undoCapacity);
    private string? _backgroundWork;

    public TournamentState State { get; private set; } = initial;

    public event EventHandler? StateChanged;

    public bool IsBusy => _backgroundWork is not null;

    public bool CanUndo => !IsBusy && _history.CanUndo;

    public bool CanRedo => !IsBusy && _history.CanRedo;

    public string? UndoDescription => _history.PeekUndo?.Description;

    public string? RedoDescription => _history.PeekRedo?.Description;

    public CommandResult Execute(ITournamentCommand command)
    {
        if (BusyError() is { } busy)
            return busy;

        Transition transition;
        try
        {
            transition = command.Apply(State, new CommandContext(time.GetUtcNow()));
        }
        catch (DomainException ex)
        {
            return CommandResult.Fail(ex.Message);
        }

        var before = State;
        var result = CommitTo(transition.NewState, transition.Audit);
        if (result.Succeeded)
            _history.Record(new HistoryEntry(before, transition.NewState, transition.Description));
        return Publish(result);
    }

    public CommandResult Undo()
    {
        if (BusyError() is { } busy)
            return busy;
        if (_history.PeekUndo is not { } entry)
            return CommandResult.Fail("沒有可復原的操作。");

        var result = CommitTo(entry.Before, [Audit("復原", entry.Description)]);
        if (result.Succeeded)
            _history.CommitUndo();
        return Publish(result);
    }

    public CommandResult Redo()
    {
        if (BusyError() is { } busy)
            return busy;
        if (_history.PeekRedo is not { } entry)
            return CommandResult.Fail("沒有可重做的操作。");

        var result = CommitTo(entry.After, [Audit("重做", entry.Description)]);
        if (result.Succeeded)
            _history.CommitRedo();
        return Publish(result);
    }

    /// <summary>
    /// 標記背景工作（例如配對計算）進行中；期間拒絕任何狀態變更，
    /// 確保背景計算所依據的狀態不會被改動。Dispose 後解除。
    /// </summary>
    public IDisposable BeginBackgroundWork(string description)
    {
        if (_backgroundWork is not null)
            throw new InvalidOperationException($"已有背景工作進行中：{_backgroundWork}");
        _backgroundWork = description;
        return new BackgroundWorkScope(this);
    }

    private CommandResult CommitTo(TournamentState target, IReadOnlyList<AuditEntry> audit)
    {
        try
        {
            store.Commit(State, target, audit);
        }
        catch (StorageException ex)
        {
            return CommandResult.Fail($"無法寫入賽事檔：{ex.Message}");
        }

        State = target;
        return CommandResult.Ok;
    }

    private CommandResult Publish(CommandResult result)
    {
        if (result.Succeeded)
            StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private CommandResult? BusyError() =>
        _backgroundWork is null ? null : CommandResult.Fail($"「{_backgroundWork}」進行中，請等待完成或取消後再操作。");

    private AuditEntry Audit(string kind, string description) =>
        new(time.GetUtcNow(), kind, description, null, null);

    private sealed class BackgroundWorkScope(TournamentSession session) : IDisposable
    {
        public void Dispose() => session._backgroundWork = null;
    }
}
