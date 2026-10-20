using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.RoundRobin;
using TourneyDesk.Core.Standings;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.RoundRobin;

public class RoundRobinTests
{
    private static List<string> Lines(TournamentState s)
    {
        var lines = new List<string>();
        foreach (var r in s.Rounds)
        {
            for (var i = 0; i < r.Games.Length; i++)
                lines.Add($"{r.Number} {i + 1} {s.Players[r.Games[i].White].RegNo} {s.Players[r.Games[i].Black].RegNo}");
            if (r.Bye is { } b)
                lines.Add($"{r.Number} bye {s.Players[b].RegNo}");
        }

        return lines;
    }

    private static List<string> Expected(string file) =>
        [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "round-robin", file))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))];

    [Theory]
    [InlineData("single-7.json", "expected-single-7.txt")]
    [InlineData("double-4.json", "expected-double-4.txt")]
    public void Schedule_matches_hand_calculated_sample(string scenario, string expected)
    {
        var s = ScenarioFile.Load($"round-robin/{scenario}").ToState().Then(new StartRoundRobin());

        Assert.Equal(Expected(expected), Lines(s));
    }

    /// <summary>驗收 10：N = 2..64，單循環與雙循環的排程性質。</summary>
    [Fact]
    public void Schedule_properties_hold_for_2_to_64_players()
    {
        for (var n = 2; n <= 64; n++)
        {
            foreach (var isDouble in new[] { false, true })
            {
                var ids = Enumerable.Range(1, n).Select(i => i * 7).ToList(); // 任意非連續 Id
                var rounds = RoundRobinScheduler.Build(ids, isDouble);
                var cycles = isDouble ? 2 : 1;
                var context = $"n={n} double={isDouble}";

                var expectedRounds = (n % 2 == 0 ? n - 1 : n) * cycles;
                Assert.True(expectedRounds == rounds.Length, context);

                var meetings = new Dictionary<(int, int), int>();
                var whites = ids.ToDictionary(id => id, _ => 0);
                var blacks = ids.ToDictionary(id => id, _ => 0);
                var byes = ids.ToDictionary(id => id, _ => 0);
                foreach (var r in rounds)
                {
                    var seen = r.Games.SelectMany(g => new[] { g.White, g.Black }).Concat(r.Bye is { } b ? [b] : []).ToList();
                    Assert.True(seen.Count == seen.Distinct().Count(), $"{context} 第 {r.Number} 輪有人出賽兩次");
                    Assert.True((n % 2 == 1) == r.Bye.HasValue, $"{context} 輪空數錯誤");
                    foreach (var g in r.Games)
                    {
                        var key = g.White < g.Black ? (g.White, g.Black) : (g.Black, g.White);
                        meetings[key] = meetings.GetValueOrDefault(key) + 1;
                        whites[g.White]++;
                        blacks[g.Black]++;
                    }

                    if (r.Bye is { } bye)
                        byes[bye]++;
                }

                Assert.True(n * (n - 1) / 2 == meetings.Count, context);
                Assert.All(meetings.Values, c => Assert.True(c == cycles, context));
                Assert.All(ids, id => Assert.True(Math.Abs(whites[id] - blacks[id]) <= 1, $"{context} 選手 {id} 先後手差超過 1"));
                if (n % 2 == 1)
                    Assert.All(ids, id => Assert.True(byes[id] == cycles, $"{context} 輪空次數不相等"));

                if (isDouble)
                {
                    var half = rounds.Length / 2;
                    for (var k = 0; k < half; k++)
                    {
                        var first = rounds[k].Games.Select(g => (g.White, g.Black)).ToHashSet();
                        var second = rounds[k + half].Games.Select(g => (g.Black, g.White)).ToHashSet();
                        Assert.True(first.SetEquals(second), $"{context} 第二循環第 {k + 1} 輪未互換先後手");
                    }
                }
            }
        }
    }

    [Fact]
    public void Start_requires_round_robin_format_and_two_players()
    {
        var swiss = SwissFlow.NewSwiss(4);
        Assert.Equal("此操作只適用於循環賽。", swiss.RuleViolation(new StartRoundRobin()));

        var single = TournamentState.Create(TournamentSettings.Create("RR", TournamentFormat.RoundRobin)).Then(new AddPlayer("A", 1));
        Assert.Equal("循環賽至少需要 2 位選手。", single.RuleViolation(new StartRoundRobin()));
    }

    [Fact]
    public void Players_are_fixed_after_start()
    {
        var s = ScenarioFile.Load("round-robin/double-4.json").ToState().Then(new StartRoundRobin());

        Assert.Equal("循環賽已經開始。", s.RuleViolation(new StartRoundRobin()));
        Assert.Equal("雙循環開賽後不可新增選手。", s.RuleViolation(new AddPlayer("E", 1)));
        Assert.Contains("已排入賽程", s.RuleViolation(new DeletePlayer(1)));
    }

    /// <summary>驗收 11：循環賽進行中退賽，其後場次為棄權、對手得 2 半分；輪空依設定計分。</summary>
    [Fact]
    public void Withdrawal_during_round_robin_forfeits_later_games()
    {
        var s = ScenarioFile.Load("round-robin/single-7.json").ToState() with
        {
            Settings = TournamentSettings.Create("RR", TournamentFormat.RoundRobin, byePoints: 1),
        };
        s = s.Then(new StartRoundRobin());
        // 第 1、2 輪全部和局
        foreach (var r in new[] { 1, 2 })
            for (var b = 1; b <= 3; b++)
                s = s.Then(new SetResult(r, b, GameResult.Draw));

        var withdrawn = s.Then(new WithdrawPlayer(1, 3)); // #1 林一 自第 3 輪起退賽

        foreach (var r in withdrawn.Rounds.Where(r => r.Number >= 3))
        {
            if (r.GameIndexOf(1) is { } gi)
            {
                var g = r.Games[gi];
                Assert.Equal(g.White == 1 ? GameResult.WhiteForfeit : GameResult.BlackForfeit, g.Result);
                Assert.Equal(2, g.PointsFor(g.OpponentOf(1)));
            }
        }

        Assert.Null(withdrawn.GetRound(4).Bye); // 原本第 4 輪 #1 輪空 → 退賽後取消

        var rows = StandingsCalculator.Compute(withdrawn);
        Assert.Equal(2, rows.Single(r => r.RegNo == 1).Score); // 兩場和局，凍結
        // #5：第 1 輪和（1）、第 2 輪輪空（設定 1 半分）、第 3 輪對 #1 → #1 棄權（2）
        Assert.Equal(1 + 1 + 2, rows.Single(r => r.RegNo == 5).Score);
    }

    [Fact]
    public void Bye_points_follow_setting_in_round_robin()
    {
        foreach (var bye in new[] { 0, 1, 2 })
        {
            var s = ScenarioFile.Load("round-robin/single-7.json").ToState() with
            {
                Settings = TournamentSettings.Create("RR", TournamentFormat.RoundRobin, byePoints: bye),
            };
            s = s.Then(new StartRoundRobin());

            var rows = StandingsCalculator.Compute(s);

            Assert.Equal(bye, rows.Single(r => r.RegNo == 2).Score); // #2 第 1 輪輪空，其餘尚未登錄
        }
    }
}
