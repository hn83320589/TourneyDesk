using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Session;

/// <summary>一步可復原的操作：前後兩個完整狀態（不可變，結構共享，成本很低）。</summary>
public sealed record HistoryEntry(TournamentState Before, TournamentState After, string Description);

/// <summary>有上限的復原／重做堆疊；超過上限時丟棄最舊的一步。</summary>
public sealed class UndoHistory(int capacity)
{
    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "復原層數必須大於 0。");

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public HistoryEntry? PeekUndo => _undo.Last?.Value;

    public HistoryEntry? PeekRedo => _redo.Count > 0 ? _redo.Peek() : null;

    public void Record(HistoryEntry entry)
    {
        _undo.AddLast(entry);
        if (_undo.Count > Capacity)
            _undo.RemoveFirst();
        _redo.Clear();
    }

    /// <summary>復原成功寫入後呼叫：把最上面一步移到重做堆疊。</summary>
    public void CommitUndo()
    {
        var entry = _undo.Last?.Value ?? throw new InvalidOperationException("沒有可復原的操作。");
        _undo.RemoveLast();
        _redo.Push(entry);
    }

    /// <summary>重做成功寫入後呼叫：把重做堆疊最上面一步移回復原堆疊。</summary>
    public void CommitRedo()
    {
        var entry = _redo.Pop();
        _undo.AddLast(entry);
        if (_undo.Count > Capacity)
            _undo.RemoveFirst();
    }
}
