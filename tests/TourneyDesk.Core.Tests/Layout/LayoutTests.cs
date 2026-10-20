using System.Collections.Immutable;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Layout;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Layout;

/// <summary>驗收 21（版面計算部分）。</summary>
public class LayoutTests
{
    private static Bracket BracketOf(int n, bool thirdPlace = true) =>
        KnockoutBracketBuilder.Build([.. Enumerable.Range(1, n)], thirdPlace && n >= 4);

    [Fact]
    public void Bracket_boxes_never_overlap_and_stay_inside_page()
    {
        for (var n = 2; n <= 128; n++)
        {
            var page = BracketLayout.Full(BracketOf(n));

            Assert.Equal(BracketOf(n).Matches.Length, page.Boxes.Length);
            for (var i = 0; i < page.Boxes.Length; i++)
            {
                var b = page.Boxes[i];
                Assert.True(b.X >= 0 && b.Y >= 0 && b.Right <= page.Width && b.Bottom <= page.Height, $"N={n} 第 {b.MatchId} 場超出頁面");
                for (var j = i + 1; j < page.Boxes.Length; j++)
                    Assert.False(b.Overlaps(page.Boxes[j]), $"N={n} 第 {b.MatchId} 與 {page.Boxes[j].MatchId} 場重疊");
            }
        }
    }

    [Fact]
    public void Connector_endpoints_land_on_child_right_edge_and_parent_slot()
    {
        var bracket = BracketOf(11);
        var page = BracketLayout.Full(bracket);
        var boxes = page.Boxes.ToDictionary(b => b.MatchId);

        var expectedLinks = bracket.Matches
            .SelectMany(m => new[] { (m.A, false, m.Id), (m.B, true, m.Id) })
            .Where(x => x.Item1.Kind == KnockoutSourceKind.Winner)
            .Select(x => (From: x.Item1.Value, To: x.Item3, IsB: x.Item2))
            .ToHashSet();

        Assert.Equal(expectedLinks, page.Connectors.Select(c => (c.FromMatchId, c.ToMatchId, c.ToSlotB)).ToHashSet());
        foreach (var c in page.Connectors)
        {
            var from = boxes[c.FromMatchId];
            var to = boxes[c.ToMatchId];
            Assert.Equal(new LayoutPoint(from.Right, from.CenterY), c.Points[0]);
            Assert.Equal(new LayoutPoint(to.X, c.ToSlotB ? to.SlotBY : to.SlotAY), c.Points[^1]);
            for (var i = 1; i < c.Points.Length; i++)
                Assert.True(c.Points[i].X == c.Points[i - 1].X || c.Points[i].Y == c.Points[i - 1].Y, "連線只能水平或垂直");
        }
    }

    [Fact]
    public void Parent_box_is_vertically_centered_between_its_children()
    {
        var page = BracketLayout.Full(BracketOf(8, thirdPlace: false));
        var b = page.Boxes.ToDictionary(x => x.MatchId);

        Assert.Equal((b[1].CenterY + b[2].CenterY) / 2, b[5].CenterY);
        Assert.Equal((b[5].CenterY + b[6].CenterY) / 2, b[7].CenterY);
    }

    [Theory]
    [InlineData(128, 600)]
    [InlineData(128, 300)]
    [InlineData(64, 400)]
    [InlineData(33, 250)]
    [InlineData(11, 300)]
    [InlineData(5, 2000)]
    public void Bracket_pagination_covers_every_match_exactly_once(int n, int pageHeight)
    {
        var bracket = BracketOf(n);

        var pages = BracketLayout.Paginate(bracket, pageHeight);

        var ids = pages.SelectMany(p => p.Boxes.Select(b => b.MatchId)).ToList();
        Assert.Equal(bracket.Matches.Select(m => m.Id).Order(), ids.Order());
        foreach (var page in pages)
        {
            Assert.True(page.Height <= pageHeight || page.Boxes.Length == 1 || pages.Length == 1, $"頁高 {page.Height} 超過 {pageHeight}");
            for (var i = 0; i < page.Boxes.Length; i++)
                for (var j = i + 1; j < page.Boxes.Length; j++)
                    Assert.False(page.Boxes[i].Overlaps(page.Boxes[j]));
        }
    }

    [Fact]
    public void Bracket_layout_is_deterministic()
    {
        var a = BracketLayout.Full(BracketOf(37));
        var b = BracketLayout.Full(BracketOf(37));

        Assert.Equal(a.Boxes.ToArray(), b.Boxes.ToArray());
        Assert.Equal(a.Connectors.Select(c => string.Join(";", c.Points)), b.Connectors.Select(c => string.Join(";", c.Points)));
    }

    [Theory]
    [InlineData(400, 40, 20)]
    [InlineData(12, 5, 7)]
    [InlineData(7, 7, 7)]
    [InlineData(1, 3, 3)]
    public void Crosstable_pagination_covers_every_cell_exactly_once_without_overlap(int n, int rowsPerPage, int colsPerPage)
    {
        var pages = CrosstableLayout.Paginate(n, rowsPerPage, colsPerPage);

        var cells = pages.SelectMany(p => p.Cells.Select(c => (c.Row, c.Column))).ToList();
        Assert.Equal(n * n, cells.Count);
        Assert.Equal(n * n, cells.Distinct().Count());
        Assert.Equal(Math.Ceiling((double)n / rowsPerPage) * Math.Ceiling((double)n / colsPerPage), pages.Length);
        foreach (var page in pages)
        {
            var list = page.Cells;
            for (var i = 0; i < list.Length; i++)
            {
                Assert.True(list[i].X + list[i].Width <= page.Width && list[i].Y + list[i].Height <= page.Height);
                for (var j = i + 1; j < Math.Min(list.Length, i + colsPerPage + 2); j++)
                    Assert.False(list[i].Overlaps(list[j]));
            }
        }

        Assert.All(Enumerable.Range(0, n), r => Assert.Single(pages, p => p.ShowScore && p.Rows.Contains(r)));
    }

    [Fact]
    public void Crosstable_data_shows_results_from_each_players_perspective()
    {
        var s = ScenarioFile.Load("round-robin/double-4.json").ToState().Then(new StartRoundRobin())
            .Then(new SetResult(1, 1, GameResult.WhiteWin))       // #4 勝 #1
            .Then(new SetResult(4, 1, GameResult.Draw))           // 第二循環 #1 和 #4
            .Then(new SetResult(1, 2, GameResult.WhiteForfeit));  // #2 棄權，#3 勝

        var data = Crosstable.Build(s);
        int Row(int regNo) => data.Players.Select((p, i) => (p.RegNo, i)).Single(x => x.RegNo == regNo).i;

        Assert.Equal("1 ½", data.Cells[Row(4)][Row(1)]);
        Assert.Equal("0 ½", data.Cells[Row(1)][Row(4)]);
        Assert.Equal("+ ·", data.Cells[Row(3)][Row(2)]);
        Assert.Equal("- ·", data.Cells[Row(2)][Row(3)]);
        Assert.Equal("×", data.Cells[0][0]);
    }
}
