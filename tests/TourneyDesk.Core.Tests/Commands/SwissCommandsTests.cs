using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Commands;

public class SwissCommandsTests
{
    private const GameResult W = GameResult.WhiteWin;
    private const GameResult B = GameResult.BlackWin;
    private const GameResult D = GameResult.Draw;

    [Fact]
    public void Generate_creates_draft_with_engine_proposal()
    {
        var s = SwissFlow.NewSwiss(4).Draft();

        var r = s.GetRound(1);
        Assert.Equal(RoundStatus.Draft, r.Status);
        Assert.Equal("A-D B-C", s.Show(1));
        Assert.True(r.EngineProposal!.SameAs(Pairing.Of(r)));
    }

    [Fact]
    public void Generate_requires_previous_round_completed()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W);

        Assert.Equal("第 1 輪尚有未登錄的成績。", s.RuleViolation(new GenerateSwissRound(2, SwissRoundPlanner.Propose(s, 2).Pairing)));
    }

    [Fact]
    public void Generate_rejects_when_draft_exists_or_rounds_exhausted()
    {
        var drafted = SwissFlow.NewSwiss(4).Draft();
        Assert.Contains("已有草稿", drafted.RuleViolation(new GenerateSwissRound(2, drafted.GetRound(1).EngineProposal!)));

        var done = SwissFlow.NewSwiss(2, rounds: 1).NextRound().Results(W);
        Assert.Equal("已達總輪數 1 輪。", done.RuleViolation(new GenerateSwissRound(2, Pairing.Of(done.GetRound(1)))));
    }

    [Fact]
    public void Generate_rejects_proposal_that_misses_players()
    {
        var s = SwissFlow.NewSwiss(4);
        var partial = new Pairing([new PairedGame(1, 2)], null);

        Assert.Equal("配對未涵蓋所有可配對選手。", s.RuleViolation(new GenerateSwissRound(1, partial)));
    }

    [Fact]
    public void Results_cannot_be_entered_on_draft()
    {
        var s = SwissFlow.NewSwiss(4).Draft();

        Assert.Contains("尚未發布", s.RuleViolation(new SetResult(1, 1, W)));
    }

    [Fact]
    public void Swap_between_boards_moves_players_and_recolors()
    {
        var s = SwissFlow.NewSwiss(4).Draft(); // A-D B-C

        var after = s.Then(new SwapInDraft(1, s.Id("D"), s.Id("C")));

        Assert.Equal("A-C B-D", after.Show(1));
    }

    [Fact]
    public void Swap_with_bye_player()
    {
        var s = SwissFlow.NewSwiss(5).Draft(); // A-D B-C bye:E

        var after = s.Then(new SwapInDraft(1, s.Id("A"), s.Id("E")));

        Assert.Equal("E-D B-C bye:A", after.Show(1).Replace("D-E", "E-D"));
        Assert.Equal(s.Id("A"), after.GetRound(1).Bye);
    }

    [Fact]
    public void Swap_within_same_board_swaps_colors()
    {
        var s = SwissFlow.NewSwiss(4).Draft();

        Assert.Equal("D-A B-C", s.Then(new SwapInDraft(1, s.Id("A"), s.Id("D"))).Show(1));
    }

    [Fact]
    public void Swap_is_only_allowed_on_drafts()
    {
        var s = SwissFlow.NewSwiss(4).NextRound();

        Assert.Equal("只有草稿可以手動交換。", s.RuleViolation(new SwapInDraft(1, 1, 2)));
    }

    [Fact]
    public void Evaluation_after_swap_shows_difference_and_violations()
    {
        // 第 1 輪 A-D B-C 後，第 2 輪引擎建議 A-B C-D；手動改成 A-D（重複）B-C（重複）
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).Draft();
        var swapped = s.Then(new SwapInDraft(2, s.Id("B"), s.Id("D")));

        var best = SwissRoundPlanner.Evaluate(s, s.GetRound(2));
        var manual = SwissRoundPlanner.Evaluate(swapped, swapped.GetRound(2));

        Assert.Equal(0, best.Objective.Repeats);
        Assert.Equal(2, manual.Objective.Repeats);
        Assert.Equal(2, manual.Violations.Length);
        Assert.True(best.Objective.CompareTo(manual.Objective) < 0);
    }

    [Fact]
    public void Publishing_violations_requires_force_and_is_audited()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).Draft();
        var swapped = s.Then(new SwapInDraft(2, s.Id("B"), s.Id("D")));

        var error = swapped.RuleViolation(new PublishRound(2));
        Assert.Contains("重複對戰", error);
        Assert.Contains("強制採用", error);

        var t = swapped.Run(new PublishRound(2, Force: true));
        Assert.True(t.NewState.GetRound(2).ForcedOverride);
        Assert.Equal(["強制採用", "發布配對"], t.Audit.Select(a => a.Kind));
    }

    [Fact]
    public void Publishing_engine_proposal_writes_single_audit()
    {
        var t = SwissFlow.NewSwiss(4).Draft().Run(new PublishRound(1));

        Assert.Equal("發布配對", Assert.Single(t.Audit).Kind);
        Assert.False(t.NewState.GetRound(1).ForcedOverride);
    }

    [Fact]
    public void Publishing_manual_but_legal_change_is_audited_as_adjustment()
    {
        var s = SwissFlow.NewSwiss(4).Draft();
        var swapped = s.Then(new SwapInDraft(1, s.Id("D"), s.Id("C")));

        var t = swapped.Run(new PublishRound(1));

        Assert.Equal(["採用手動調整", "發布配對"], t.Audit.Select(a => a.Kind));
        Assert.False(t.NewState.GetRound(1).ForcedOverride);
    }

    [Fact]
    public void Bye_is_recorded_automatically_on_publish()
    {
        var s = SwissFlow.NewSwiss(3).NextRound(); // A-B bye:C（最後一名輪空）

        var rows = TourneyDesk.Core.Standings.StandingsCalculator.Compute(s);

        Assert.Equal("A-B bye:C", s.Show(1));
        Assert.Equal(2, rows.Single(r => r.Name == "C").Score);
        Assert.False(s.GetRound(1).IsCompleted); // A-B 尚未登錄
    }

    [Fact]
    public void SetResult_records_then_corrects_with_audit()
    {
        var s = SwissFlow.NewSwiss(2).NextRound();

        var first = s.Run(new SetResult(1, 1, W));
        var second = first.NewState.Run(new SetResult(1, 1, D));

        Assert.Equal(("登錄成績", "未登錄", "先手勝"), (first.Audit[0].Kind, first.Audit[0].Before, first.Audit[0].After));
        Assert.Equal(("更正成績", "先手勝", "和"), (second.Audit[0].Kind, second.Audit[0].Before, second.Audit[0].After));
        Assert.Equal("第 1 輪 第 1 桌 #1 A vs #2 B", second.Audit[0].Target);
    }

    [Fact]
    public void SetResult_rejects_same_value_bad_board_and_clearing_with_later_rounds()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W);

        Assert.Equal("成績沒有變更。", s.RuleViolation(new SetResult(1, 1, W)));
        Assert.Equal("第 1 輪沒有第 3 桌。", s.RuleViolation(new SetResult(1, 3, W)));

        var later = s.NextRound();
        Assert.Contains("已有後續輪次", later.RuleViolation(new SetResult(1, 1, GameResult.None)));
    }

    [Fact]
    public void Correction_that_changes_next_proposal_marks_later_round_stale_without_touching_it()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).NextRound(); // R1 A-D B-C；R2 A-B C-D
        Assert.Equal("A-B C-D", s.Show(2));
        var publishedBefore = Pairing.Of(s.GetRound(2));

        var corrected = s.Then(new SetResult(1, 2, B)); // B-C 改為 C 勝

        Assert.Equal([2], SwissStaleness.FindStaleRounds(corrected).ToArray());
        Assert.True(Pairing.Of(corrected.GetRound(2)).SameAs(publishedBefore)); // 已發布配對未被改動
    }

    [Fact]
    public void Correction_that_does_not_change_next_proposal_keeps_round_valid()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).NextRound();

        var corrected = s.Then(new SetResult(1, 1, GameResult.BlackForfeit)); // A 仍得 2 分

        Assert.Empty(SwissStaleness.FindStaleRounds(corrected));
    }

    [Fact]
    public void Manually_adjusted_round_is_not_stale_by_itself()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).Draft();
        s = s.Then(new SwapInDraft(2, s.Id("B"), s.Id("D"))).Then(new PublishRound(2, Force: true));

        Assert.Empty(SwissStaleness.FindStaleRounds(s));
    }

    [Fact]
    public void Repair_from_round_requires_confirmed_impact_and_regenerates_draft()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).NextRound().Results(W, W);
        var corrected = s.Then(new SetResult(1, 2, B));
        var impact = RepairFromRound.Preview(corrected, 2);
        var proposal = SwissRoundPlanner.Propose(corrected, 2).Pairing;

        Assert.Contains("第 2 輪：已發布，2 場配對、2 筆成績將被清除", impact.Items);
        Assert.Contains("影響範圍已變更", corrected.RuleViolation(new RepairFromRound(2, proposal, "錯誤指紋")));

        var repaired = corrected.Then(new RepairFromRound(2, proposal, impact.Fingerprint));
        Assert.Equal(2, repaired.Rounds.Length);
        Assert.Equal(RoundStatus.Draft, repaired.GetRound(2).Status);
        Assert.Equal("C-A D-B", repaired.Show(2)); // C、D 先手次數較少 → 先手
    }

    [Fact]
    public void Repair_from_round_is_undoable_through_session()
    {
        var store = new InMemoryStore();
        var initial = SwissFlow.NewSwiss(4).NextRound().Results(W, W).NextRound().Results(W, W).Then(new SetResult(1, 2, B));
        var session = new TournamentSession(initial, store, new ManualTimeProvider());
        var before = StateHasher.Compute(session.State);
        var impact = RepairFromRound.Preview(session.State, 2);

        Assert.True(session.Execute(new RepairFromRound(2, SwissRoundPlanner.Propose(session.State, 2).Pairing, impact.Fingerprint)).Succeeded);
        Assert.True(session.Undo().Succeeded);

        Assert.Equal(before, StateHasher.Compute(session.State));
    }

    [Fact]
    public void Withdraw_forfeits_unentered_games_and_stops_future_pairing()
    {
        var s = SwissFlow.NewSwiss(4).NextRound(); // A-D B-C，尚未登錄
        var withdrawn = s.Then(new WithdrawPlayer(s.Id("D"), 1));

        Assert.Equal(GameResult.BlackForfeit, withdrawn.GetRound(1).Games[0].Result); // D 是後手 → 後手棄權
        Assert.Equal(GameResult.None, withdrawn.GetRound(1).Games[1].Result);
        var next = withdrawn.Then(new SetResult(1, 2, W)).Draft();
        Assert.DoesNotContain("D", next.Show(2).Split(' ', '-'));
        Assert.True(withdrawn.Players[s.Id("D")].IsWithdrawn);
    }

    [Fact]
    public void Withdraw_keeps_entered_results_and_discards_draft_containing_player()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).Draft();

        var t = s.Run(new WithdrawPlayer(s.Id("A"), 2));

        Assert.Single(t.NewState.Rounds);
        Assert.Equal(W, t.NewState.GetRound(1).Games[0].Result);
        Assert.Contains(t.Audit, a => a.Kind == "作廢草稿");
    }

    [Fact]
    public void Withdraw_rejects_twice()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Then(new WithdrawPlayer(1, 2));

        Assert.Contains("已經退賽", s.RuleViolation(new WithdrawPlayer(1, 2)));
    }

    [Fact]
    public void Withdraw_before_start_points_to_delete()
    {
        Assert.Equal("賽事尚未開始，請直接刪除選手。", SwissFlow.NewSwiss(4).RuleViolation(new WithdrawPlayer(1, 1)));
    }

    [Fact]
    public void Late_join_enters_from_given_round_with_zero_prior_score()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W);

        var joined = s.Then(new LateJoinPlayer("E", 2500, 2));
        var e = joined.Players.Values.Single(p => p.Name == "E");

        Assert.Equal(2, e.JoinRound);
        Assert.Equal(5, e.RegNo);
        var input = SwissRoundInput.FromState(joined, 2);
        Assert.Equal(0, input.Get(e.Id).Score);
        Assert.Equal(5, input.Count);
    }

    [Theory]
    [InlineData(1, "遲到加入的起始輪必須介於第 2 輪與第 5 輪之間。")]
    [InlineData(6, "遲到加入的起始輪必須介於第 2 輪與第 5 輪之間。")]
    public void Late_join_validates_round_range(int fromRound, string expected)
    {
        Assert.Equal(expected, SwissFlow.NewSwiss(4).RuleViolation(new LateJoinPlayer("E", 1, fromRound)));
    }

    [Fact]
    public void Late_join_rejects_published_round_and_existing_draft()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(W, W).NextRound();
        Assert.Contains("第 2 輪已發布", s.RuleViolation(new LateJoinPlayer("E", 1, 2)));

        var drafted = s.Results(W, W).Draft();
        Assert.Contains("已有草稿", drafted.RuleViolation(new LateJoinPlayer("E", 1, 3)));
    }

    [Fact]
    public void Late_join_is_not_allowed_for_round_robin()
    {
        var s = TournamentState.Create(TournamentSettings.Create("RR", TournamentFormat.RoundRobin));

        Assert.Equal("單循環開賽後不可遲到加入。", s.RuleViolation(new LateJoinPlayer("E", 1, 2)));
    }

    [Fact]
    public void Discard_draft_removes_it()
    {
        var s = SwissFlow.NewSwiss(4).Draft().Then(new DiscardDraft(1));

        Assert.Empty(s.Rounds);
    }
}
