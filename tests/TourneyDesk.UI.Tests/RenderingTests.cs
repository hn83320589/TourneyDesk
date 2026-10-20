using System.Text;
using Bunit;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Layout;
using TourneyDesk.Core.Tests.TestSupport;
using TourneyDesk.UI.Pages;
using TourneyDesk.UI.Rendering;
using TourneyDesk.UI.Services;

namespace TourneyDesk.UI.Tests;

/// <summary>驗收 21（bUnit 部分）：SVG 依版面計算繪製，且輸出為決定性的。</summary>
public class RenderingTests : UiTestBase
{
    private static TournamentState Knockout(int n)
    {
        var s = TournamentState.Create(TournamentSettings.Create("KO", TournamentFormat.SingleElimination, thirdPlaceMatch: true));
        for (var i = 0; i < n; i++)
            s = s.Then(new AddPlayer($"選手{i + 1}", 2000 - i));
        return s.Then(new StartKnockout());
    }

    private IRenderedComponent<BracketSvg> RenderBracket(TournamentState s) =>
        Render<BracketSvg>(p => p
            .Add(x => x.Bracket, s.Bracket!)
            .Add(x => x.Page, BracketLayout.Full(s.Bracket!))
            .Add(x => x.Players, s.Players));

    [Fact]
    public void Bracket_svg_is_deterministic()
    {
        var s = Knockout(11);

        Assert.Equal(RenderBracket(s).Markup, RenderBracket(s).Markup);
    }

    [Fact]
    public void Bracket_svg_draws_every_box_and_connector_at_layout_coordinates()
    {
        var s = Knockout(11);
        var layout = BracketLayout.Full(s.Bracket!);
        var cut = RenderBracket(s);

        var rects = cut.FindAll("rect.box");
        Assert.Equal(layout.Boxes.Length, rects.Count);
        Assert.Equal(layout.Connectors.Length, cut.FindAll("polyline.link").Count);
        var first = layout.Boxes[0];
        Assert.Equal((first.X.ToString(), first.Y.ToString()), (rects[0].GetAttribute("x"), rects[0].GetAttribute("y")));
        Assert.Contains("[1] 選手1", cut.Markup);
        Assert.Contains("（輪空）", cut.Markup);
    }

    [Fact]
    public void Crosstable_svg_is_deterministic_and_draws_every_cell()
    {
        var s = ScenarioFile("round-robin-4");
        var data = Crosstable.Build(s);
        var page = CrosstableLayout.Full(data.Players.Length);
        IRenderedComponent<CrosstableSvg> Draw() => Render<CrosstableSvg>(p => p.Add(x => x.Data, data).Add(x => x.Page, page));

        var a = Draw();

        Assert.Equal(a.Markup, Draw().Markup);
        Assert.Equal(16, a.FindAll("g.cell").Count);
    }

    [Fact]
    public void Printable_bracket_renders_each_paginated_page()
    {
        var s = Knockout(64);
        var expected = BracketLayout.Paginate(s.Bracket!, 680).Length;

        var cut = Render<PrintableBracket>(p => p.Add(x => x.State, s));

        Assert.Equal(expected, cut.FindAll(".print-page svg").Count);
        Assert.Equal(s.Bracket!.Matches.Length, cut.FindAll("rect.box").Count);
    }

    [Fact]
    public void Correcting_knockout_winner_lists_cascade_and_applies_after_confirmation()
    {
        var s = Knockout(4); // 1v4、2v3、決賽、季軍戰
        s = s.Then(new RecordKnockoutWinner(1, 1)).Then(new RecordKnockoutWinner(2, 2)).Then(new RecordKnockoutWinner(3, 1));
        OpenWith(s);
        var cut = Render<BracketView>(p => p.Add(x => x.State, Workspace.State!));

        cut.Find("g[data-match='1'] rect").Click();
        cut.Find("button.winner-choice[data-player='4']").Click();

        var items = cut.FindAll(".dialog ul.impact li").Select(li => li.TextContent).ToList();
        Assert.Equal(["決賽：勝者 #1 選手1（比賽）將被清除"], items);
        Assert.Equal(1, Workspace.State!.Bracket!.GetMatch(1).Outcome!.Winner); // 確認前不變

        cut.Find("button.confirm").Click();

        Assert.Equal(4, Workspace.State!.Bracket!.GetMatch(1).Outcome!.Winner);
        Assert.Null(Workspace.State.Bracket.GetMatch(3).Outcome);
    }

    [Fact]
    public void Standings_table_shows_tiebreak_columns()
    {
        var s = SwissFlow.NewSwiss(4).NextRound().Results(GameResult.WhiteWin, GameResult.Draw);

        var cut = Render<StandingsTable>(p => p.Add(x => x.State, s));

        var headers = cut.FindAll("th").Select(th => th.TextContent).ToList();
        Assert.Equal(["名次", "編號", "姓名", "總分", "直接對戰", "Cut-1", "Buchholz", "SB", "勝場", ""], headers);
        Assert.Equal("1", cut.Find("tbody tr td:nth-child(4)").TextContent);
    }

    [Fact]
    public async Task Exported_html_is_self_contained()
    {
        var s = Knockout(11);

        var bytes = await ExportService.HtmlAsync<PrintableBracket>("籤表", new Dictionary<string, object?> { ["State"] = s });
        var html = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        Assert.Contains("<style>", html);
        Assert.Contains("<svg class=\"bracket\"", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org/2000/svg", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Standings_csv_has_bom_and_tiebreak_columns()
    {
        var s = SwissFlow.NewSwiss(3).NextRound().Results(GameResult.Draw);

        var bytes = ExportService.StandingsCsv(s);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("名次,註冊編號,姓名,總分,直接對戰,Cut-1,Buchholz,SB,勝場,狀態", text);
    }

    [Fact]
    public void Quarter_points_are_formatted_without_floating_point()
    {
        Assert.Equal(["0", "0.25", "5.5", "2.75", "-1.25"], new long[] { 0, 1, 22, 11, -5 }.Select(Display.Quarter));
    }

    private static TournamentState ScenarioFile(string _)
    {
        var s = TournamentState.Create(TournamentSettings.Create("RR", TournamentFormat.DoubleRoundRobin));
        for (var i = 0; i < 4; i++)
            s = s.Then(new AddPlayer($"P{i}", 2000 - i));
        return s.Then(new StartRoundRobin()).Then(new SetResult(1, 1, GameResult.WhiteWin));
    }
}
