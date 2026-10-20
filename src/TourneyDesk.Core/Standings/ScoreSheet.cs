using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Standings;

public enum EntryKind
{
    /// <summary>雙方到場實際下完的對局。</summary>
    Played,

    /// <summary>單方或雙方棄權的對局（自己是棄權方或到場方都算）。</summary>
    Forfeit,

    /// <summary>輪空（系統自動登錄）。</summary>
    Bye,
}

/// <param name="Opponent">對手內部 Id；輪空為 null。</param>
/// <param name="Points">本場得分（半分）。</param>
public readonly record struct ScoreEntry(int Round, EntryKind Kind, int? Opponent, int Points);

/// <summary>
/// 依目前已登錄的成績整理出每位選手逐輪的得分紀錄。尚未登錄結果的對局與草稿輪次不計。
/// </summary>
public sealed class ScoreSheet
{
    private readonly Dictionary<int, List<ScoreEntry>> _entries;
    private readonly Dictionary<int, int> _totals;

    private ScoreSheet(Dictionary<int, List<ScoreEntry>> entries, int completedRounds)
    {
        _entries = entries;
        CompletedRounds = completedRounds;
        _totals = entries.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(e => e.Points));
    }

    /// <summary>「目前已完成輪數」：已發布且所有對局皆有結果的輪次數。</summary>
    public int CompletedRounds { get; }

    public static ScoreSheet Build(TournamentState state, int? beforeRound = null)
    {
        var limit = beforeRound ?? int.MaxValue;
        var entries = state.Players.Keys.ToDictionary(id => id, _ => new List<ScoreEntry>());
        var completed = 0;

        foreach (var round in state.Rounds)
        {
            if (round.Number >= limit || !round.IsPublished)
                continue;
            if (round.IsCompleted)
                completed++;

            if (round.Bye is { } bye)
                entries[bye].Add(new ScoreEntry(round.Number, EntryKind.Bye, null, state.Settings.ByePoints));

            foreach (var g in round.Games)
            {
                if (!g.Result.HasResult())
                    continue;
                var kind = g.Result.IsPlayed() ? EntryKind.Played : EntryKind.Forfeit;
                entries[g.White].Add(new ScoreEntry(round.Number, kind, g.Black, g.Result.WhitePoints()));
                entries[g.Black].Add(new ScoreEntry(round.Number, kind, g.White, g.Result.BlackPoints()));
            }
        }

        return new ScoreSheet(entries, completed);
    }

    public IReadOnlyList<ScoreEntry> EntriesOf(int playerId) => _entries[playerId];

    /// <summary>目前總分（半分）。已退賽者自然凍結在退賽當下的累計值。</summary>
    public int Total(int playerId) => _totals[playerId];

    public int ScoreBefore(int playerId, int round) =>
        _entries[playerId].Where(e => e.Round < round).Sum(e => e.Points);
}
