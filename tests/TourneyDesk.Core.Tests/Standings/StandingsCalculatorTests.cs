using System.Collections.Immutable;
using System.Globalization;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Standings;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Standings;

public class StandingsCalculatorTests
{
    /// <summary>以註冊編號 1..n 建立選手（內部 Id = 註冊編號），並依序加入輪次。</summary>
    private static TournamentState Build(int players, int byePoints, params Round[] rounds)
    {
        var settings = TournamentSettings.Create("T", TournamentFormat.Swiss, 5, byePoints);
        var dict = Enumerable.Range(1, players)
            .ToImmutableSortedDictionary(i => i, i => new Player(i, i, ((char)('A' + i - 1)).ToString(), 1000));
        return new TournamentState(settings, dict, [.. rounds], players + 1, players + 1);
    }

    private static Round R(int n, int? bye, params Game[] games) => new(n, RoundStatus.Published, [.. games], bye);

    private static StandingRow Row(ImmutableArray<StandingRow> rows, int regNo) => rows.Single(r => r.RegNo == regNo);

    [Fact]
    public void Tiebreak_sample_matches_expected_file_item_by_item()
    {
        var state = ScenarioFile.Load("tiebreak/scenario.json").ToState();
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "tiebreak", "expected-standings.csv"))
            .Skip(1).Where(l => l.Length > 0).ToList();

        var rows = StandingsCalculator.Compute(state);

        Assert.Equal(expected.Count, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var actual = string.Join(',',
                r.Rank, r.RegNo, r.Name, r.Score,
                r.DirectEncounter?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.BuchholzCut1, r.Buchholz, r.SonnebornBerger, r.Wins,
                r.Withdrawn ? "true" : "false");
            Assert.Equal(expected[i], actual);
        }
    }

    [Fact]
    public void Competition_ranking_skips_after_ties()
    {
        // A 勝 B；C 和 D（直接對戰適用但相等，其餘各項也相等）→ 1、2、2、4
        var s = Build(4, 2, R(1, null, new Game(1, 2, GameResult.WhiteWin), new Game(3, 4, GameResult.Draw)));

        var rows = StandingsCalculator.Compute(s);

        Assert.Equal([1, 2, 2, 4], rows.Select(r => r.Rank));
        Assert.Equal([1, 3, 4, 2], rows.Select(r => r.RegNo));
    }

    [Fact]
    public void Tied_players_are_listed_by_registration_number()
    {
        var settings = TournamentSettings.Create("T", TournamentFormat.Swiss);
        var players = new[] { new Player(10, 3, "C", 1), new Player(20, 1, "A", 1), new Player(30, 2, "B", 1) }
            .ToImmutableSortedDictionary(p => p.Id, p => p);
        var s = new TournamentState(settings, players, [], 31, 4);

        var rows = StandingsCalculator.Compute(s);

        Assert.All(rows, r => Assert.Equal(1, r.Rank));
        Assert.Equal([1, 2, 3], rows.Select(r => r.RegNo));
    }

    [Theory]
    [InlineData(GameResult.WhiteForfeit, 0, 2)]
    [InlineData(GameResult.BlackForfeit, 2, 0)]
    [InlineData(GameResult.DoubleForfeit, 0, 0)]
    public void Forfeit_scores_follow_assumptions(GameResult result, int white, int black)
    {
        var rows = StandingsCalculator.Compute(Build(2, 2, R(1, null, new Game(1, 2, result))));

        Assert.Equal((white, black), (Row(rows, 1).Score, Row(rows, 2).Score));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Bye_scores_follow_tournament_setting(int byePoints)
    {
        var rows = StandingsCalculator.Compute(Build(3, byePoints, R(1, 3, new Game(1, 2, GameResult.Draw))));

        Assert.Equal(byePoints, Row(rows, 3).Score);
    }

    [Fact]
    public void Unentered_results_and_draft_rounds_do_not_count()
    {
        var s = Build(4, 2,
            R(1, null, new Game(1, 2, GameResult.WhiteWin), new Game(3, 4)),
            new Round(2, RoundStatus.Draft, [new Game(1, 3, GameResult.WhiteWin)], null));

        var rows = StandingsCalculator.Compute(s);

        Assert.Equal(2, Row(rows, 1).Score);
        Assert.Equal(0, Row(rows, 3).Score);
        Assert.Equal(0, ScoreSheet.Build(s).CompletedRounds);
    }

    [Fact]
    public void Forfeit_wins_are_not_counted_as_actual_wins_or_in_sb()
    {
        var rows = StandingsCalculator.Compute(Build(2, 2, R(1, null, new Game(1, 2, GameResult.BlackForfeit))));

        Assert.Equal(0, Row(rows, 1).Wins);
        Assert.Equal(0, Row(rows, 1).SonnebornBerger);
    }

    [Fact]
    public void Virtual_opponent_uses_score_before_round_plus_completed_rounds_offset()
    {
        // 3 輪皆完成。選手 3 第 2 輪輪空：前累計 = 第 1 輪勝 2；偏移 = 3 − 2 + 1 = 2 → 虛擬 4
        var s = Build(4, 2,
            R(1, null, new Game(3, 4, GameResult.WhiteWin), new Game(1, 2, GameResult.Draw)),
            R(2, 3, new Game(1, 4, GameResult.Draw)),
            R(3, null, new Game(3, 1, GameResult.Draw), new Game(2, 4, GameResult.Draw)));

        var sheet = ScoreSheet.Build(s);

        Assert.Equal(3, sheet.CompletedRounds);
        Assert.Equal(4, PlayerMetrics.VirtualOpponentScore(3, 2, sheet));
    }

    [Fact]
    public void Virtual_opponent_offset_is_not_negative_for_unfinished_rounds()
    {
        // 第 1 輪未完成（3–4 未登錄），選手 5 輪空：前累計 0，偏移 max(0, 0 − 1 + 1) = 0
        var s = Build(5, 2, R(1, 5, new Game(1, 2, GameResult.Draw), new Game(3, 4)));

        var sheet = ScoreSheet.Build(s);

        Assert.Equal(0, PlayerMetrics.VirtualOpponentScore(5, 1, sheet));
        Assert.Equal(0, Row(StandingsCalculator.Compute(s), 5).Buchholz);
    }

    [Fact]
    public void Withdrawn_opponent_contributes_frozen_score()
    {
        // B：第 1 輪和 A、第 2 輪 D 棄權（B 得 2），第 3 輪起退賽 → B 的分數凍結在 3
        var s = Build(4, 2,
            R(1, null, new Game(1, 2, GameResult.Draw), new Game(3, 4, GameResult.WhiteWin)),
            R(2, null, new Game(1, 3, GameResult.WhiteWin), new Game(4, 2, GameResult.WhiteForfeit)));
        s = s.WithPlayer(s.Players[2] with { WithdrawRound = 3 });

        var rows = StandingsCalculator.Compute(s);

        // B 的分數：1（和 A）+ 2（D 棄權）= 3，凍結
        Assert.Equal(3, Row(rows, 2).Score);
        Assert.True(Row(rows, 2).Withdrawn);
        // A 的對手：B(3)、C(2) → Buchholz 5
        Assert.Equal(5, Row(rows, 1).Buchholz);
    }

    [Fact]
    public void Late_joiner_has_no_virtual_opponents_for_missed_rounds()
    {
        var s = Build(3, 2,
            R(1, null, new Game(1, 2, GameResult.WhiteWin)),
            R(2, null, new Game(3, 1, GameResult.WhiteWin)));
        s = s.WithPlayer(s.Players[3] with { JoinRound = 2 });

        var rows = StandingsCalculator.Compute(s);

        // C 只有一場實際對局（對手 A 總分 2）
        Assert.Equal(2, Row(rows, 3).Buchholz);
        Assert.Equal(0, Row(rows, 3).BuchholzCut1);
    }

    [Fact]
    public void Direct_encounter_decides_two_tied_players_who_met()
    {
        // B 勝 A；之後 A 勝 C、D，B 勝 E、負 C → A、B 同為 4。A 的 Buchholz 8 > B 的 6，但直接對戰 B 優先
        var s = Build(5, 2,
            R(1, null, new Game(2, 1, GameResult.WhiteWin), new Game(4, 3, GameResult.WhiteWin)),
            R(2, null, new Game(1, 3, GameResult.WhiteWin), new Game(2, 5, GameResult.WhiteWin)),
            R(3, null, new Game(1, 4, GameResult.WhiteWin), new Game(2, 3, GameResult.BlackWin)));

        var rows = StandingsCalculator.Compute(s);

        Assert.Equal(4, Row(rows, 1).Score);
        Assert.Equal(4, Row(rows, 2).Score);
        Assert.True(Row(rows, 1).Buchholz > Row(rows, 2).Buchholz);
        Assert.Equal(1, Row(rows, 2).Rank);
        Assert.Equal(2, Row(rows, 1).Rank);
        Assert.Equal((2, 0), (Row(rows, 2).DirectEncounter, Row(rows, 1).DirectEncounter));
    }

    [Fact]
    public void Direct_encounter_counts_all_games_between_group_members()
    {
        // 雙循環：A、B 交手兩次（A 勝、B 和）→ 直接對戰 A 3、B 1
        var s = Build(2, 0,
            R(1, null, new Game(1, 2, GameResult.WhiteWin)),
            R(2, null, new Game(2, 1, GameResult.Draw)));

        var rows = StandingsCalculator.Compute(s);

        Assert.Equal(3, Row(rows, 1).Score);
        Assert.Equal(1, Row(rows, 2).Score);
    }

    [Fact]
    public void Forfeit_between_tied_players_does_not_make_direct_encounter_applicable()
    {
        // A、B 第 1 輪是棄權（非實際對局）→ 直接對戰不適用，改比 Cut-1
        var s = Build(4, 2,
            R(1, null, new Game(1, 2, GameResult.DoubleForfeit), new Game(3, 4, GameResult.Draw)),
            R(2, null, new Game(1, 3, GameResult.WhiteWin), new Game(2, 4, GameResult.WhiteWin)));

        var rows = StandingsCalculator.Compute(s);

        Assert.Null(Row(rows, 1).DirectEncounter);
        Assert.Null(Row(rows, 2).DirectEncounter);
    }

    [Fact]
    public void Wins_break_tie_when_all_earlier_criteria_are_equal()
    {
        // A 勝 X、負 Y；B 和 X、和 Y；X、Y 最後同為 3 分 → Buchholz、Cut-1、SB 都相等，只差實際勝場
        const int x = 3, y = 4;
        var s = Build(6, 2,
            R(1, null, new Game(1, x, GameResult.WhiteWin), new Game(2, y, GameResult.Draw)),
            R(2, null, new Game(1, y, GameResult.BlackWin), new Game(2, x, GameResult.Draw)),
            R(3, null, new Game(x, 5, GameResult.WhiteWin), new Game(y, 6, GameResult.BlackWin)));

        var rows = StandingsCalculator.Compute(s);
        var a = Row(rows, 1);
        var b = Row(rows, 2);

        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Buchholz, b.Buchholz);
        Assert.Equal(a.BuchholzCut1, b.BuchholzCut1);
        Assert.Equal(a.SonnebornBerger, b.SonnebornBerger);
        Assert.True(a.Rank < b.Rank);
    }

    [Fact]
    public void Standings_are_computed_with_no_rounds()
    {
        var rows = StandingsCalculator.Compute(Build(3, 2));

        Assert.All(rows, r => Assert.Equal((1, 0, 0), (r.Rank, r.Score, r.Buchholz)));
    }
}
