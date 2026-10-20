using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Standings;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Standings;

public class StandingsPropertyTests
{
    private static string Snapshot(TournamentState s) =>
        string.Join("\n", StandingsCalculator.Compute(s).Select(r =>
            $"{r.Rank},{r.RegNo},{r.Score},{r.DirectEncounter},{r.BuchholzCut1},{r.Buchholz},{r.SonnebornBerger},{r.Wins}"));

    /// <summary>驗收 17：成績登錄順序不影響積分榜與各項同分判定值。</summary>
    [Fact]
    public void Result_entry_order_does_not_affect_standings()
    {
        Property.ForAll(seed: 17, cases: 200, (rng, _) =>
        {
            var (full, _) = SwissHistoryGenerator.Generate(rng, rng.Next(2, 30), rng.Next(1, 7));
            var blank = full with
            {
                Rounds = [.. full.Rounds.Select(r => r with { Games = [.. r.Games.Select(g => g with { Result = GameResult.None })] })],
            };

            var entries = full.Rounds
                .SelectMany(r => r.Games.Select((g, i) => new SetResult(r.Number, i + 1, g.Result)))
                .OrderBy(_ => rng.Next())
                .ToList();
            var replayed = entries.Aggregate(blank, (s, cmd) => s.Then(cmd));

            Assert.Equal(Snapshot(full), Snapshot(replayed));
        });
    }

    /// <summary>驗收 17：所有選手半分總和 = 有結果的實際對局數 × 2 + 輪空得分 + 棄權得分。</summary>
    [Fact]
    public void Total_half_points_are_conserved()
    {
        Property.ForAll(seed: 1717, cases: 300, (rng, _) =>
        {
            var (s, _) = SwissHistoryGenerator.Generate(rng, rng.Next(1, 40), rng.Next(0, 9));
            // 隨機清掉部分成績，涵蓋「進行中」的情況
            s = s with
            {
                Rounds = [.. s.Rounds.Select(r => r with
                {
                    Games = [.. r.Games.Select(g => rng.Next(10) == 0 ? g with { Result = GameResult.None } : g)],
                })],
            };

            var games = s.Rounds.SelectMany(r => r.Games).ToList();
            var played = games.Count(g => g.Result is GameResult.WhiteWin or GameResult.Draw or GameResult.BlackWin);
            var forfeitPoints = 2 * games.Count(g => g.Result is GameResult.WhiteForfeit or GameResult.BlackForfeit);
            var byePoints = s.Settings.ByePoints * s.Rounds.Count(r => r.Bye is not null);

            var total = StandingsCalculator.Compute(s).Sum(r => r.Score);

            Assert.Equal((played * 2) + byePoints + forfeitPoints, total);
        });
    }
}
