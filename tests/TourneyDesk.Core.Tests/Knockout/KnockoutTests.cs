using System.Collections.Immutable;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Knockout;

public class KnockoutTests
{
    private static TournamentState Sample11() =>
        ScenarioFile.Load("knockout/bracket-11.json").ToState().Then(new StartKnockout());

    private static string Source(Bracket b, KnockoutSource s) => s.Kind switch
    {
        KnockoutSourceKind.Seed => s.Value <= b.Seeds.Length ? $"S{s.Value}" : "-",
        KnockoutSourceKind.Winner => $"W{s.Value}",
        _ => $"L{s.Value}",
    };

    private static string Line(Bracket b, KnockoutMatch m)
    {
        var status = m.IsThirdPlace ? "季軍戰"
            : m.Outcome is { Kind: KnockoutOutcomeKind.Bye } o ? $"bye:S{b.SeedOf(o.Winner)}"
            : "待賽";
        return $"{m.Id} {m.Round} {m.Position} {Source(b, m.A)} {Source(b, m.B)} {status}";
    }

    /// <summary>依種子編號登錄勝者（範例中種子 = 註冊編號 = 內部 Id）。</summary>
    private static TournamentState Win(TournamentState s, int matchId, int seed) =>
        s.Then(new RecordKnockoutWinner(matchId, s.Bracket!.Seeds[seed - 1]));

    private static TournamentState PlayedSample()
    {
        var s = Sample11();
        foreach (var (match, seed) in new[] { (2, 8), (6, 7), (8, 6), (9, 1), (10, 4), (11, 2), (12, 3), (13, 1), (14, 3), (15, 1), (16, 2) })
            s = Win(s, match, seed);
        return s;
    }

    [Fact]
    public void Bracket_for_11_players_matches_expected_file_cell_by_cell()
    {
        var s = Sample11();
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "knockout", "expected-bracket-11.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

        Assert.Equal(expected, s.Bracket!.Matches.Select(m => Line(s.Bracket, m)).ToList());
    }

    [Theory]
    [InlineData(8, new[] { 1, 8, 4, 5, 2, 7, 3, 6 })]
    [InlineData(4, new[] { 1, 4, 2, 3 })]
    [InlineData(2, new[] { 1, 2 })]
    public void Standard_positions_for_small_sizes(int size, int[] expected)
    {
        Assert.Equal(expected, KnockoutBracketBuilder.StandardPositions(size).ToArray());
    }

    /// <summary>
    /// 驗收 12：N = 2..128。位置以獨立的性質刻畫：第 1 輪每組種子和為 S+1，且每組較小種子依序構成 S/2 的標準位置；
    /// 輪空只出現在第 1 輪且屬於最高的 S − N 位種子。
    /// </summary>
    [Fact]
    public void Seeding_and_byes_hold_for_2_to_128_players()
    {
        for (var n = 2; n <= 128; n++)
        {
            var ids = Enumerable.Range(1, n).Select(i => i * 3).ToImmutableArray();
            var players = ids.Select((id, i) => new Player(id, i + 1, $"P{i + 1}", 3000 - i)).ToDictionary(p => p.Id);
            var bracket = KnockoutRules.Normalize(KnockoutBracketBuilder.Build(ids, thirdPlaceMatch: n >= 4), players);
            var size = bracket.Size;
            var context = $"N={n}";

            Assert.True(size >= n && size / 2 < n, context);
            var firstRound = bracket.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
            Assert.Equal(size / 2, firstRound.Count);
            Assert.All(firstRound, m => Assert.True(m.A.Value + m.B.Value == size + 1, context));
            if (size > 2)
                Assert.Equal(KnockoutBracketBuilder.StandardPositions(size / 2).ToArray(),
                    firstRound.Select(m => Math.Min(m.A.Value, m.B.Value)).ToArray());

            var byeSeeds = bracket.Matches.Where(m => m.Outcome?.Kind == KnockoutOutcomeKind.Bye)
                .Select(m => { Assert.Equal(1, m.Round); return bracket.SeedOf(m.Outcome!.Winner); })
                .OrderBy(x => x).ToList();
            Assert.Equal(Enumerable.Range(1, size - n).ToList(), byeSeeds);

            var slotSeeds = firstRound.SelectMany(m => new[] { m.A.Value, m.B.Value }).Where(x => x <= n).OrderBy(x => x);
            Assert.Equal(Enumerable.Range(1, n), slotSeeds);
            Assert.Equal(size - 1 + (n >= 4 ? 1 : 0), bracket.Matches.Length);
        }
    }

    [Fact]
    public void Recording_requires_known_participants_and_valid_winner()
    {
        var s = Sample11();

        Assert.Contains("對戰雙方尚未確定", s.RuleViolation(new RecordKnockoutWinner(9, 1)));
        Assert.Equal("勝者必須是此場的參賽者之一。", s.RuleViolation(new RecordKnockoutWinner(2, 1)));
        Assert.Contains("已有勝者", s.RuleViolation(new RecordKnockoutWinner(1, 1))); // 輪空場
    }

    [Fact]
    public void Winners_advance_and_standings_follow_bracket_positions()
    {
        var s = PlayedSample();

        var places = KnockoutStandings.Compute(s);

        Assert.Equal(
            ["1:1", "2:3", "3:2", "4:4", "5:5", "5:6", "5:7", "5:8", "9:9", "9:10", "9:11"],
            places.Select(p => $"{p.Rank}:{p.RegNo}"));
        Assert.Equal("冠軍", places[0].Placement);
    }

    [Fact]
    public void Correcting_winner_lists_cascade_requires_confirmation_and_clears_dependents()
    {
        var s = PlayedSample();

        var impact = KnockoutRules.CascadeImpact(s, 10);
        Assert.Equal(
        [
            "準決賽第 1 場：勝者 #1 林一（比賽）將被清除",
            "決賽：勝者 #1 林一（比賽）將被清除",
            "季軍戰：勝者 #2 陳二（比賽）將被清除",
        ], impact.Items.ToArray());
        Assert.Contains("影響範圍已變更", s.RuleViolation(new CorrectKnockoutWinner(10, 5, "未確認")));

        var corrected = s.Then(new CorrectKnockoutWinner(10, 5, impact.Fingerprint));
        var b = corrected.Bracket!;
        Assert.Equal(5, b.GetMatch(10).Outcome!.Winner);
        Assert.All(new[] { 13, 15, 16 }, id => Assert.Null(b.GetMatch(id).Outcome));
        Assert.Equal(3, b.GetMatch(14).Outcome!.Winner);
        Assert.Equal((1, 5), (b.Participants(b.GetMatch(13)).A.PlayerId, b.Participants(b.GetMatch(13)).B.PlayerId));
    }

    [Fact]
    public void Correction_with_cascade_is_undone_in_one_step()
    {
        var session = new TournamentSession(PlayedSample(), new InMemoryStore(), new ManualTimeProvider());
        var before = StateHasher.Compute(session.State);
        var impact = KnockoutRules.CascadeImpact(session.State, 10);

        Assert.True(session.Execute(new CorrectKnockoutWinner(10, 5, impact.Fingerprint)).Succeeded);
        Assert.True(session.Undo().Succeeded);

        Assert.Equal(before, StateHasher.Compute(session.State));
    }

    [Fact]
    public void Correction_without_dependents_has_empty_impact()
    {
        var s = Win(Sample11(), 2, 8);

        var impact = KnockoutRules.CascadeImpact(s, 2);

        Assert.True(impact.IsEmpty);
        Assert.Equal(9, s.Then(new CorrectKnockoutWinner(2, 9, impact.Fingerprint)).Bracket!.GetMatch(2).Outcome!.Winner);
    }

    [Fact]
    public void Withdrawn_players_opponent_advances_immediately_when_known()
    {
        var s = Sample11().Then(new WithdrawFromKnockout(9)); // S9 退賽，對手 S8 已確定

        var m = s.Bracket!.GetMatch(2);
        Assert.Equal(new KnockoutOutcome(8, KnockoutOutcomeKind.Walkover), m.Outcome);
        Assert.Equal(1, s.Players[9].WithdrawRound);
    }

    [Fact]
    public void Withdrawn_players_later_match_is_walkover_once_opponent_is_known()
    {
        var s = Sample11().Then(new WithdrawFromKnockout(1)); // S1 輪空晉級後退賽；第 9 場對手待定

        Assert.Null(s.Bracket!.GetMatch(9).Outcome);
        var after = Win(s, 2, 9);
        Assert.Equal(new KnockoutOutcome(9, KnockoutOutcomeKind.Walkover), after.Bracket!.GetMatch(9).Outcome);
    }

    [Fact]
    public void Third_place_with_a_bye_semifinal_is_decided_automatically()
    {
        var s = TournamentState.Create(TournamentSettings.Create("KO", TournamentFormat.SingleElimination, thirdPlaceMatch: true))
            .Then(new AddPlayer("A", 3)).Then(new AddPlayer("B", 2)).Then(new AddPlayer("C", 1))
            .Then(new StartKnockout());

        var after = s.Then(new RecordKnockoutWinner(2, 2)); // S2 勝 S3

        var third = after.Bracket!.ThirdPlace!;
        Assert.Equal(new KnockoutOutcome(3, KnockoutOutcomeKind.Bye), third.Outcome);
    }

    [Fact]
    public void Two_player_bracket_has_only_a_final()
    {
        var s = TournamentState.Create(TournamentSettings.Create("KO", TournamentFormat.SingleElimination, thirdPlaceMatch: true))
            .Then(new AddPlayer("A", 3)).Then(new AddPlayer("B", 2)).Then(new StartKnockout());

        Assert.Single(s.Bracket!.Matches);
        Assert.Null(s.Bracket.ThirdPlace);
    }

    [Fact]
    public void Knockout_rejects_round_based_commands()
    {
        var s = Sample11();

        Assert.Contains("籤表", s.RuleViolation(new SetResult(1, 1, GameResult.WhiteWin)));
        Assert.Contains("籤表", s.RuleViolation(new WithdrawPlayer(1, 1)));
        Assert.Equal("籤表已經產生。", s.RuleViolation(new StartKnockout()));
    }
}
