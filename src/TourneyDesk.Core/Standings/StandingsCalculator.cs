using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Standings;

/// <summary>積分榜的一列。所有數值皆為整數：分數類為半分，SB 為「半分 × 半分」。</summary>
/// <param name="DirectEncounter">直接對戰得分（半分）；僅在最終所屬的並列群組適用直接對戰時有值。</param>
public sealed record StandingRow(
    int Rank,
    int PlayerId,
    int RegNo,
    string Name,
    int Score,
    int? DirectEncounter,
    int BuchholzCut1,
    int Buchholz,
    int SonnebornBerger,
    int Wins,
    bool Withdrawn);

/// <summary>
/// 依「假設事項」計算名次：
/// ① 總分 → ② 直接對戰（群組內兩兩皆有實際對局時才適用）→ ③ Buchholz Cut-1 → ④ Buchholz → ⑤ SB → ⑥ 實際勝場。
/// 某一項把並列群組分成數個較小群組時，每個仍並列的小群組從 ② 重新開始。
/// 名次採競賽排名（1、2、2、4）；並列者依註冊編號由小到大顯示。
/// </summary>
public static class StandingsCalculator
{
    private const int FirstTieBreak = 2;
    private const int LastTieBreak = 6;

    public static ImmutableArray<StandingRow> Compute(TournamentState state)
    {
        var sheet = ScoreSheet.Build(state);
        var metrics = state.Players.Values.ToDictionary(p => p.Id, p => PlayerMetrics.Compute(p.Id, sheet));
        var playedPairs = PlayedPairs(state);
        var directEncounter = new Dictionary<int, int>();

        var blocks = new List<List<int>>();
        foreach (var scoreGroup in state.Players.Keys
                     .GroupBy(id => metrics[id].Score)
                     .OrderByDescending(g => g.Key))
        {
            Resolve([.. scoreGroup], FirstTieBreak, blocks, metrics, playedPairs, sheet, directEncounter);
        }

        var rows = ImmutableArray.CreateBuilder<StandingRow>(state.Players.Count);
        var placed = 0;
        foreach (var block in blocks)
        {
            var rank = placed + 1;
            foreach (var id in block.OrderBy(id => state.Players[id].RegNo))
            {
                var p = state.Players[id];
                var m = metrics[id];
                rows.Add(new StandingRow(rank, id, p.RegNo, p.Name, m.Score,
                    directEncounter.TryGetValue(id, out var de) ? de : null,
                    m.BuchholzCut1, m.Buchholz, m.SonnebornBerger, m.Wins, p.IsWithdrawn));
            }

            placed += block.Count;
        }

        return rows.MoveToImmutable();
    }

    private static void Resolve(
        List<int> group,
        int fromCriterion,
        List<List<int>> blocks,
        Dictionary<int, PlayerMetrics> metrics,
        HashSet<(int, int)> playedPairs,
        ScoreSheet sheet,
        Dictionary<int, int> directEncounter)
    {
        if (group.Count == 1)
        {
            blocks.Add(group);
            return;
        }

        for (var criterion = fromCriterion; criterion <= LastTieBreak; criterion++)
        {
            var values = CriterionValues(criterion, group, metrics, playedPairs, sheet);
            if (values is null)
                continue; // 直接對戰不適用

            if (criterion == 2)
                foreach (var id in group)
                    directEncounter[id] = (int)values[id];

            var subgroups = group.GroupBy(id => values[id]).OrderByDescending(g => g.Key).ToList();
            if (subgroups.Count == 1)
                continue;

            foreach (var sub in subgroups)
                Resolve([.. sub], FirstTieBreak, blocks, metrics, playedPairs, sheet, directEncounter);
            return;
        }

        blocks.Add(group); // 全部項目相同 → 並列
    }

    private static Dictionary<int, long>? CriterionValues(
        int criterion,
        List<int> group,
        Dictionary<int, PlayerMetrics> metrics,
        HashSet<(int, int)> playedPairs,
        ScoreSheet sheet) => criterion switch
        {
            2 => DirectEncounter(group, playedPairs, sheet),
            3 => group.ToDictionary(id => id, id => (long)metrics[id].BuchholzCut1),
            4 => group.ToDictionary(id => id, id => (long)metrics[id].Buchholz),
            5 => group.ToDictionary(id => id, id => (long)metrics[id].SonnebornBerger),
            6 => group.ToDictionary(id => id, id => (long)metrics[id].Wins),
            _ => throw new ArgumentOutOfRangeException(nameof(criterion)),
        };

    /// <summary>僅在群組內每兩人之間都至少有一場實際對局時適用；否則回傳 null。</summary>
    private static Dictionary<int, long>? DirectEncounter(List<int> group, HashSet<(int, int)> playedPairs, ScoreSheet sheet)
    {
        for (var i = 0; i < group.Count; i++)
            for (var j = i + 1; j < group.Count; j++)
                if (!playedPairs.Contains(Key(group[i], group[j])))
                    return null;

        var members = group.ToHashSet();
        return group.ToDictionary(id => id, id => (long)sheet.EntriesOf(id)
            .Where(e => e.Kind == EntryKind.Played && members.Contains(e.Opponent!.Value))
            .Sum(e => e.Points));
    }

    private static HashSet<(int, int)> PlayedPairs(TournamentState state) =>
        state.Rounds
            .Where(r => r.IsPublished)
            .SelectMany(r => r.Games)
            .Where(g => g.Result.IsPlayed())
            .Select(g => Key(g.White, g.Black))
            .ToHashSet();

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}

/// <summary>單一選手的同分判定數值（皆為整數）。</summary>
public sealed record PlayerMetrics(int Score, int BuchholzCut1, int Buchholz, int SonnebornBerger, int Wins)
{
    public static PlayerMetrics Compute(int playerId, ScoreSheet sheet)
    {
        var opponentScores = new List<int>();
        var sb = 0;
        var wins = 0;

        foreach (var e in sheet.EntriesOf(playerId))
        {
            if (e.Kind == EntryKind.Played)
            {
                var opponentTotal = sheet.Total(e.Opponent!.Value);
                opponentScores.Add(opponentTotal);
                sb += opponentTotal * e.Points;
                if (e.Points == HalfPoints.Win)
                    wins++;
            }
            else
            {
                opponentScores.Add(VirtualOpponentScore(playerId, e.Round, sheet));
            }
        }

        var buchholz = opponentScores.Sum();
        var cut1 = opponentScores.Count == 0 ? 0 : buchholz - opponentScores.Min();
        return new PlayerMetrics(sheet.Total(playerId), cut1, buchholz, sb, wins);
    }

    /// <summary>
    /// 虛擬對手分數（半分）＝ 該選手在該輪之前的累計分數 + 1 ×（目前已完成輪數 − 該輪輪次 + 1）。
    /// 括號內若為負（該輪之後的輪次尚未完成）以 0 計。
    /// </summary>
    public static int VirtualOpponentScore(int playerId, int round, ScoreSheet sheet) =>
        sheet.ScoreBefore(playerId, round) + Math.Max(0, sheet.CompletedRounds - round + 1);
}
