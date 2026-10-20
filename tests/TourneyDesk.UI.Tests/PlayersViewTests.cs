using System.Collections.Immutable;
using System.Text;
using Bunit;
using TourneyDesk.Core.Domain;
using TourneyDesk.UI.Pages;

namespace TourneyDesk.UI.Tests;

public class PlayersViewTests : UiTestBase
{
    private static TournamentState WithPlayers(int count)
    {
        var players = Enumerable.Range(1, count).Select(i => new Player(i, i, $"選手{i}", 1000 + i))
            .ToImmutableSortedDictionary(p => p.Id, p => p);
        return new TournamentState(TournamentSettings.Create("大型賽", TournamentFormat.Swiss), players, [], count + 1, count + 1);
    }

    /// <summary>
    /// 驗收 22：400 人清單虛擬化，DOM 中渲染的列數遠少於 400。
    /// bUnit 沒有實際版面，因此模擬瀏覽器端 Virtualize 回報「可視區高 520px」（與 .vlist-body 相同）。
    /// </summary>
    [Fact]
    public void Four_hundred_players_are_virtualized()
    {
        OpenWith(WithPlayers(400));
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));
        Assert.True(cut.FindAll(".player-row").Count <= 100, "未虛擬化：沒有可視區資訊時也不得渲染全部 400 列");

        ReportViewport(containerHeight: 520);
        cut.Render();

        var rendered = cut.FindAll(".player-row").Count;
        Assert.InRange(rendered, 10, 40); // 520 / 32 ≈ 17 列 + 前後各 5 列緩衝
        Assert.Contains("共 400 人", cut.Markup);
    }

    /// <summary>呼叫 Virtualize 在 JS 端註冊的 .NET 回呼 OnSpacerBeforeVisible(spacerSize, spacerSeparation, containerSize)。</summary>
    private void ReportViewport(float containerHeight)
    {
        var init = JSInterop.Invocations["Blazor._internal.Virtualize.init"].Single();
        var reference = init.Arguments[0]!;
        var target = reference.GetType().GetProperty("Value")!.GetValue(reference)!;
        var callback = target.GetType().GetMethod("OnSpacerBeforeVisible")!;
        Renderer.Dispatcher.InvokeAsync(() => callback.Invoke(target, [0f, 0f, containerHeight])).GetAwaiter().GetResult();
    }

    [Fact]
    public void Adding_a_player_updates_list()
    {
        OpenWith(WithPlayers(0));
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));

        cut.Find("input.new-name").Change("王小明");
        cut.Find("input.new-rating").Change("1550");
        cut.Find("button.add-player").Click();

        var player = Assert.Single(Workspace.State!.Players.Values);
        Assert.Equal(("王小明", 1550, 1), (player.Name, player.Rating, player.RegNo));
    }

    [Fact]
    public void Csv_preview_lists_error_lines_and_imports_valid_rows()
    {
        OpenWith(WithPlayers(1));
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));
        var csv = Encoding.UTF8.GetBytes("註冊編號,姓名,積分\n1,重複,1500\n,新人,1600\n,超分,9999\n");

        cut.InvokeAsync(() => cut.Instance.PreviewCsv(csv));
        cut.Render();

        var errors = cut.FindAll(".import-errors li").Select(li => li.TextContent).ToList();
        Assert.Equal(["第 2 行：註冊編號 1 重複", "第 4 行：積分 9999 超出範圍（0～3000）"], errors);

        cut.Find("button.confirm-import").Click();

        Assert.Contains(Workspace.State!.Players.Values, p => p.Name == "新人");
        Assert.Equal(2, Workspace.State.Players.Count);
        Assert.Equal("已匯入 1 位選手，2 列有錯誤未匯入", Workspace.Notice!.Message);
    }

    [Fact]
    public void Editing_a_player_changes_name_and_rating()
    {
        OpenWith(WithPlayers(1));
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));

        cut.Find(".player-row button.link").Click();
        cut.Find("input.edit-name").Change("新名字");
        cut.Find("input.edit-rating").Change("2100");
        cut.Find("button.save-edit").Click();

        Assert.Equal(("新名字", 2100), (Workspace.State!.Players[1].Name, Workspace.State.Players[1].Rating));
    }

    [Fact]
    public void Unscheduled_player_can_be_deleted()
    {
        OpenWith(WithPlayers(2));
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));

        cut.FindAll("button.delete-player")[0].Click();

        Assert.Single(Workspace.State!.Players);
    }

    [Fact]
    public void Exporting_writes_utf8_bom_csv()
    {
        OpenWith(WithPlayers(3));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        Dialogs.SavePath = path;
        var cut = Render<PlayersView>(p => p.Add(x => x.State, Workspace.State!));

        try
        {
            cut.FindAll("button").Single(b => b.TextContent.StartsWith("匯出", StringComparison.Ordinal)).Click();

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.StartsWith("註冊編號,姓名,積分,狀態", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
