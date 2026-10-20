using Bunit;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Tests.TestSupport;
using TourneyDesk.UI.Pages;

namespace TourneyDesk.UI.Tests;

public class RoundsAndResultsTests : UiTestBase
{
    private IRenderedComponent<RoundsView> RenderRounds() =>
        Render<RoundsView>(p => p.Add(x => x.State, Workspace.State!));

    [Fact]
    public void Generating_a_round_runs_in_background_and_creates_draft()
    {
        OpenWith(SwissFlow.NewSwiss(6));
        var cut = RenderRounds();

        cut.Find("button.generate-round").Click();
        cut.WaitForAssertion(() => Assert.Single(Workspace.State!.Rounds), TimeSpan.FromSeconds(5));

        Assert.Equal(RoundStatus.Draft, Workspace.State!.Rounds[0].Status);
        Assert.Null(Workspace.BackgroundWork);
    }

    [Fact]
    public void Draft_swap_shows_difference_and_violations_and_requires_force_to_publish()
    {
        // 第 1 輪 A-D B-C，第 2 輪最佳 A-B C-D；把 B 與 D 交換 → 兩場重複對戰
        OpenWith(SwissFlow.NewSwiss(4).NextRound().Results(GameResult.WhiteWin, GameResult.WhiteWin).Draft());
        var cut = RenderRounds();
        Assert.Contains("與最佳解相同", cut.Find(".evaluation").TextContent);

        cut.Find("[data-player='2']").Click();
        cut.Find("[data-player='4']").Click();
        cut.Render(p => p.Add(x => x.State, Workspace.State!));

        Assert.NotNull(cut.Find(".badge.differs"));
        Assert.Equal(2, cut.FindAll(".violations li").Count);
        Assert.Equal("2", cut.Find(".current-objective td.num").TextContent);
        Assert.True(cut.Find("button.publish").HasAttribute("disabled"));

        cut.Find("input.force-publish").Change(true);
        cut.Find("button.publish").Click();

        Assert.True(Workspace.State!.GetRound(2).ForcedOverride);
        Assert.Contains(Gateway.Store.Audit, a => a.Kind == "強制採用");
    }

    [Fact]
    public void Round_robin_schedule_is_generated_from_rounds_page()
    {
        var state = TournamentState.Create(TournamentSettings.Create("RR", TournamentFormat.RoundRobin));
        for (var i = 0; i < 5; i++)
            state = state.Then(new AddPlayer($"P{i}", 1000 + i));
        OpenWith(state);
        var cut = RenderRounds();

        cut.Find("button.start-rr").Click();

        Assert.Equal(5, Workspace.State!.Rounds.Length);
    }

    [Fact]
    public void Stale_rounds_are_flagged_and_repair_requires_confirmation()
    {
        OpenWith(SwissFlow.NewSwiss(4).NextRound().Results(GameResult.WhiteWin, GameResult.WhiteWin).NextRound());
        Workspace.Execute(new SetResult(1, 2, GameResult.BlackWin)); // 更正 → 第 2 輪過期
        var cut = RenderRounds();
        cut.WaitForAssertion(() => Assert.Equal([2], Workspace.StaleRounds.ToArray()), TimeSpan.FromSeconds(5));
        cut.Render(p => p.Add(x => x.State, Workspace.State!));

        Assert.Contains("第 2 輪的配對已與發布時的最佳建議不同", cut.Find(".stale-warning").TextContent);
        Assert.Equal(2, Workspace.State!.Rounds.Length); // 系統未自行改動

        cut.Find("button.repair").Click();
        cut.WaitForElement(".dialog ul.impact", TimeSpan.FromSeconds(5));
        Assert.Contains("第 2 輪：已發布，2 場配對、0 筆成績將被清除", cut.Find(".dialog").TextContent);

        cut.Find("button.confirm").Click();

        Assert.Equal(RoundStatus.Draft, Workspace.State!.GetRound(2).Status);
        Assert.True(Workspace.Undo().Succeeded);
        Assert.Equal(RoundStatus.Published, Workspace.State!.GetRound(2).Status);
    }

    [Fact]
    public void Printing_pairing_sheet_uses_print_service_and_renders_boards()
    {
        OpenWith(SwissFlow.NewSwiss(5).NextRound());
        var cut = RenderRounds();

        cut.FindAll("button").Single(b => b.TextContent == "列印對戰表").Click();

        Assert.Equal(1, Printer.Calls);
        var sheet = cut.Find(".print-only .pairing-sheet");
        Assert.Equal(2, sheet.QuerySelectorAll("tbody tr").Length - 1); // 2 桌 + 1 列輪空
        Assert.Contains("輪空", sheet.TextContent);
    }

    [Fact]
    public void Results_page_records_result_through_dropdown()
    {
        OpenWith(SwissFlow.NewSwiss(4).NextRound());
        var cut = Render<ResultsView>(p => p.Add(x => x.State, Workspace.State!));

        cut.Find("select.result-select[data-board='2']").Change(nameof(GameResult.Draw));

        Assert.Equal(GameResult.Draw, Workspace.State!.GetRound(1).Games[1].Result);
        Assert.Equal("登錄成績", Gateway.Store.Audit[^1].Kind);
    }

    [Fact]
    public async Task Background_work_can_be_cancelled_without_changing_state()
    {
        OpenWith(SwissFlow.NewSwiss(4));
        var started = new TaskCompletionSource();
        var before = Workspace.State;

        var task = Workspace.RunInBackgroundAsync<object>("長時間計算", (_, _, ct) =>
        {
            started.SetResult();
            while (!ct.IsCancellationRequested)
                Thread.Sleep(5);
            ct.ThrowIfCancellationRequested();
            return new object();
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Workspace.Execute(new AddPlayer("X", 1)).Succeeded); // 計算中拒絕變更
        Workspace.CancelBackground();

        Assert.Null(await task);
        Assert.Same(before, Workspace.State);
        Assert.Contains("已取消", Workspace.Notice!.Message);
    }
}
