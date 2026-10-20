using System.Collections.Immutable;
using System.Diagnostics;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.TestSupport;
using Xunit.Abstractions;

namespace TourneyDesk.Core.Tests.Performance;

[Trait("Category", "Performance")]
public class SwissPerformanceTests(ITestOutputHelper output)
{
    /// <summary>驗收 8：400 人 9 輪完整賽事，每輪合法且耗時符合上限。</summary>
    [ReleaseOnlyFact]
    public void Full_400_player_9_round_tournament_meets_rules_and_time_limit()
    {
        var rng = new Random(400);
        var players = Enumerable.Range(1, 400)
            .Select(i => new Player(i, i, $"P{i}", rng.Next(800, 2400)))
            .ToImmutableSortedDictionary(p => p.Id, p => p);
        var state = new TournamentState(TournamentSettings.Create("400 人", TournamentFormat.Swiss, 9), players, [], 401, 401);

        // 暖機：JIT 編譯不計入第一輪
        SwissPairingEngine.Pair(SwissRoundInput.FromState(state, 1));

        var met = new HashSet<(int, int)>();
        var byes = new HashSet<int>();
        for (var round = 1; round <= 9; round++)
        {
            var input = SwissRoundInput.FromState(state, round);
            var sw = Stopwatch.StartNew();
            var result = SwissPairingEngine.Pair(input);
            sw.Stop();
            output.WriteLine($"第 {round} 輪：{sw.ElapsedMilliseconds} ms，目標值 ①={result.Evaluation.Objective.Repeats} ④={result.Evaluation.Objective.ScoreDiffSquares}");
            Assert.True(sw.Elapsed <= PerformanceLimits.SwissRound400, $"第 {round} 輪耗時 {sw.ElapsedMilliseconds} ms 超過上限");

            var seen = result.Pairing.Games.SelectMany(g => new[] { g.White, g.Black }).ToList();
            if (result.Pairing.Bye is { } bye)
            {
                seen.Add(bye);
                Assert.True(byes.Add(bye), "重複輪空");
            }

            Assert.Equal(400, seen.Count);
            Assert.Equal(400, seen.Distinct().Count());
            foreach (var g in result.Pairing.Games)
                Assert.True(met.Add(g.White < g.Black ? (g.White, g.Black) : (g.Black, g.White)), "重複對戰");

            var games = result.Pairing.Games.Select(g => new Game(g.White, g.Black, SwissHistoryGenerator.RandomResult(rng)));
            state = state.WithRound(new Round(round, RoundStatus.Published, [.. games], result.Pairing.Bye));
        }
    }
}
