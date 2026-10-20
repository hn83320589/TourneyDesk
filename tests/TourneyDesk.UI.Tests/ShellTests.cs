using Bunit;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.UI.Tests;

public class ShellTests : UiTestBase
{
    [Fact]
    public void Shows_start_screen_when_no_tournament_is_open()
    {
        var cut = Render<Shell>();

        Assert.Equal("賽事裁判台 TourneyDesk", cut.Find(".title").TextContent);
        Assert.NotNull(cut.Find("button.new-tournament"));
        Assert.Empty(cut.FindAll(".tabs"));
    }

    [Fact]
    public void Creating_a_tournament_opens_it_with_format_specific_tabs()
    {
        var cut = Render<Shell>();

        cut.Find("button.new-tournament").Click();
        cut.Find("#nt-name").Change("春季瑞士制");
        cut.Find("button.create").Click();

        Assert.Equal("春季瑞士制", cut.Find(".tournament-name").TextContent);
        Assert.Equal(["選手", "輪次與配對", "成績登錄", "積分榜", "交叉表", "異動紀錄"], cut.FindAll(".tab").Select(b => b.TextContent));
    }

    [Fact]
    public void Knockout_tournament_shows_bracket_tab()
    {
        var cut = Render<Shell>();
        cut.Find("button.new-tournament").Click();
        cut.Find("#nt-format").Change(nameof(TournamentFormat.SingleElimination));
        cut.Find("button.create").Click();

        Assert.Contains("淘汰籤表", cut.FindAll(".tab").Select(b => b.TextContent));
    }

    [Fact]
    public void Round_robin_defaults_bye_points_to_zero_and_swiss_to_two()
    {
        var cut = Render<Shell>();
        cut.Find("button.new-tournament").Click();
        Assert.Equal("2", cut.Find("#nt-bye").GetAttribute("value"));

        cut.Find("#nt-format").Change(nameof(TournamentFormat.RoundRobin));

        Assert.Equal("0", cut.Find("#nt-bye").GetAttribute("value"));
    }

    [Fact]
    public void Rule_violation_shows_readable_error_notice()
    {
        OpenWith(TournamentState.Create(TournamentSettings.Create("T", TournamentFormat.Swiss)));
        var cut = Render<Shell>();

        cut.Find("input.new-name").Change("");
        cut.Find("button.add-player").Click();

        Assert.Equal("姓名不可空白。", cut.Find(".notice.error .message").TextContent);
    }

    [Fact]
    public void Undo_and_redo_buttons_follow_history()
    {
        OpenWith(TournamentState.Create(TournamentSettings.Create("T", TournamentFormat.Swiss)));
        var cut = Render<Shell>();
        Assert.True(cut.Find("button.undo").HasAttribute("disabled"));

        cut.Find("input.new-name").Change("王小明");
        cut.Find("button.add-player").Click();
        Assert.Single(cut.FindAll(".player-row"));

        cut.Find("button.undo").Click();
        Assert.Empty(cut.FindAll(".player-row"));
        cut.Find("button.redo").Click();
        Assert.Single(cut.FindAll(".player-row"));
    }

    [Fact]
    public void Denied_lock_shows_holder_and_cannot_be_taken_over()
    {
        Dialogs.OpenPath = @"C:\賽事\a.tourney";
        Gateway.Lock = new LockDecision(LockDecisionKind.Denied, null, "此賽事檔正由電腦「PC-B」的程序 42 使用中，無法開啟。");
        var cut = Render<Shell>();

        cut.Find("button.open-tournament").Click();

        Assert.Contains("PC-B", cut.Find(".lock-message").TextContent);
        Assert.Empty(cut.FindAll("button.take-over"));
        Assert.Empty(cut.FindAll("button.force"));
    }

    [Fact]
    public void Stale_lock_is_taken_over_after_confirmation()
    {
        Dialogs.OpenPath = @"C:\賽事\a.tourney";
        Gateway.Files[Dialogs.OpenPath] = TournamentState.Create(TournamentSettings.Create("殘留", TournamentFormat.Swiss));
        Gateway.Lock = new LockDecision(LockDecisionKind.ConfirmStaleTakeover, null, "發現殘留的鎖定紀錄");
        var cut = Render<Shell>();

        cut.Find("button.open-tournament").Click();
        cut.Find("button.take-over").Click();

        Assert.Equal((Dialogs.OpenPath, true, null), Gateway.Opens.Single());
        Assert.Equal("殘留", cut.Find(".tournament-name").TextContent);
    }

    [Fact]
    public void Foreign_lock_requires_exact_confirmation_text()
    {
        Dialogs.OpenPath = @"C:\賽事\a.tourney";
        Gateway.Files[Dialogs.OpenPath] = TournamentState.Create(TournamentSettings.Create("他機", TournamentFormat.Swiss));
        Gateway.Lock = new LockDecision(LockDecisionKind.RequireForceConfirmation, null, "鎖定紀錄屬於其他電腦");
        var cut = Render<Shell>();
        cut.Find("button.open-tournament").Click();

        cut.Find("input.force-text").Input("確定");
        Assert.True(cut.Find("button.force").HasAttribute("disabled"));

        cut.Find("input.force-text").Input(LockArbiter.ForceConfirmationText);
        cut.Find("button.force").Click();

        Assert.Equal(LockArbiter.ForceConfirmationText, Gateway.Opens.Single().Force);
    }

    [Fact]
    public void Startup_path_is_opened_once()
    {
        Startup.OpenPath = @"C:\賽事\雙擊.tourney";
        Gateway.Files[Startup.OpenPath] = TournamentState.Create(TournamentSettings.Create("雙擊開啟", TournamentFormat.Swiss));

        var cut = Render<Shell>();

        Assert.Equal("雙擊開啟", cut.Find(".tournament-name").TextContent);
        Assert.Null(Startup.OpenPath);
    }

    [Fact]
    public void Recovery_after_crash_is_reported_when_opening()
    {
        Dialogs.OpenPath = @"C:\賽事\當機.tourney";
        Gateway.Files[Dialogs.OpenPath] = TournamentState.Create(TournamentSettings.Create("當機後", TournamentFormat.Swiss));
        Gateway.OpenNotice = "已自動回復上次中斷（當機或斷電）時未完成的寫入，最後一個動作可能需要重做";
        var cut = Render<Shell>();

        cut.Find("button.open-tournament").Click();

        Assert.Contains("已自動回復上次中斷", cut.Find(".notice.info .message").TextContent);
    }

    [Fact]
    public void Open_failure_is_shown_as_notice()
    {
        Dialogs.OpenPath = @"C:\賽事\不存在.tourney";
        var cut = Render<Shell>();

        cut.Find("button.open-tournament").Click();

        Assert.StartsWith("找不到檔案", cut.Find(".notice.error .message").TextContent);
    }

    [Fact]
    public void Audit_tab_lists_recorded_changes()
    {
        OpenWith(SwissFlow.NewSwiss(2));
        Workspace.Execute(new EditPlayer(1, "改名", 2000));
        var cut = Render<Shell>();

        cut.FindAll(".tab").Single(b => b.TextContent == "異動紀錄").Click();

        Assert.Contains("編輯選手", cut.Find(".audit-row").TextContent);
    }
}
