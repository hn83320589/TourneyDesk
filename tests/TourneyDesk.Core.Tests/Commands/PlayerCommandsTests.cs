using System.Collections.Immutable;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Commands;

public class PlayerCommandsTests
{
    private static TournamentState Empty(TournamentFormat format = TournamentFormat.Swiss) =>
        TournamentState.Create(TournamentSettings.Create("測試賽", format));

    private static TournamentState WithStartedRound(TournamentState s) =>
        s.WithRound(new Round(1, RoundStatus.Published, [new Game(1, 2)], null));

    [Fact]
    public void AddPlayer_assigns_incrementing_registration_numbers()
    {
        var s = Empty().Then(new AddPlayer("王小明", 1500)).Then(new AddPlayer("李大華", 1400));

        Assert.Equal([1, 2], s.Players.Values.Select(p => p.RegNo));
        Assert.Equal(3, s.NextRegNo);
    }

    [Fact]
    public void Registration_numbers_are_not_reused_after_delete()
    {
        var s = Empty().Then(new AddPlayer("王小明", 1500)).Then(new DeletePlayer(1)).Then(new AddPlayer("李大華", 1400));

        Assert.Equal(2, s.Players.Values.Single().RegNo);
    }

    [Fact]
    public void AddPlayer_trims_name()
    {
        var s = Empty().Then(new AddPlayer("  王小明 ", 1500));

        Assert.Equal("王小明", s.Players[1].Name);
    }

    [Theory]
    [InlineData("", 1500, "姓名不可空白。")]
    [InlineData("   ", 1500, "姓名不可空白。")]
    [InlineData("王小明", -1, "積分必須是 0 到 3000 的整數。")]
    [InlineData("王小明", 3001, "積分必須是 0 到 3000 的整數。")]
    public void AddPlayer_validates_fields(string name, int rating, string expected)
    {
        Assert.Equal(expected, Empty().RuleViolation(new AddPlayer(name, rating)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3000)]
    public void AddPlayer_accepts_rating_bounds(int rating)
    {
        Assert.Single(Empty().Then(new AddPlayer("王小明", rating)).Players);
    }

    [Fact]
    public void AddPlayer_rejects_more_than_500_players()
    {
        var s = Empty();
        for (var i = 0; i < TournamentState.MaxPlayers; i++)
            s = s.Then(new AddPlayer($"P{i}", 1000));

        Assert.Equal("選手數已達上限 500 人。", s.RuleViolation(new AddPlayer("多一位", 1000)));
    }

    [Fact]
    public void AddPlayer_after_swiss_start_points_to_late_join()
    {
        var s = WithStartedRound(Empty().Then(new AddPlayer("A", 1)).Then(new AddPlayer("B", 1)));

        Assert.Contains("遲到加入", s.RuleViolation(new AddPlayer("C", 1)));
    }

    [Theory]
    [InlineData(TournamentFormat.RoundRobin, "單循環開賽後不可新增選手。")]
    [InlineData(TournamentFormat.SingleElimination, "單淘汰開賽後不可新增選手。")]
    public void AddPlayer_after_start_is_rejected_for_fixed_formats(TournamentFormat format, string expected)
    {
        var s = WithStartedRound(Empty(format).Then(new AddPlayer("A", 1)).Then(new AddPlayer("B", 1)));

        Assert.Equal(expected, s.RuleViolation(new AddPlayer("C", 1)));
    }

    [Fact]
    public void AddPlayer_audit_records_after_value()
    {
        var t = Empty().Run(new AddPlayer("王小明", 1500));

        var audit = Assert.Single(t.Audit);
        Assert.Equal("新增選手", audit.Kind);
        Assert.Equal("選手 #1 王小明", audit.Target);
        Assert.Null(audit.Before);
        Assert.Equal("姓名=王小明, 積分=1500", audit.After);
    }

    [Fact]
    public void EditPlayer_changes_fields_and_audits_before_and_after()
    {
        var s = Empty().Then(new AddPlayer("王小明", 1500));

        var t = s.Run(new EditPlayer(1, "王曉明", 1550));

        Assert.Equal(("王曉明", 1550), (t.NewState.Players[1].Name, t.NewState.Players[1].Rating));
        var audit = Assert.Single(t.Audit);
        Assert.Equal("姓名=王小明, 積分=1500", audit.Before);
        Assert.Equal("姓名=王曉明, 積分=1550", audit.After);
    }

    [Fact]
    public void EditPlayer_without_change_is_rejected()
    {
        var s = Empty().Then(new AddPlayer("王小明", 1500));

        Assert.Equal("資料沒有變更。", s.RuleViolation(new EditPlayer(1, "王小明", 1500)));
    }

    [Fact]
    public void EditPlayer_unknown_player_is_rejected()
    {
        Assert.Contains("找不到選手", Empty().RuleViolation(new EditPlayer(9, "X", 1)));
    }

    [Fact]
    public void DeletePlayer_removes_unscheduled_player()
    {
        var s = Empty().Then(new AddPlayer("王小明", 1500)).Then(new DeletePlayer(1));

        Assert.Empty(s.Players);
    }

    [Fact]
    public void DeletePlayer_rejects_scheduled_player()
    {
        var s = WithStartedRound(Empty().Then(new AddPlayer("A", 1)).Then(new AddPlayer("B", 1)));

        Assert.Contains("已排入賽程，不能刪除", s.RuleViolation(new DeletePlayer(1)));
    }

    [Fact]
    public void DeletePlayer_rejects_player_holding_a_bye()
    {
        var s = Empty().Then(new AddPlayer("A", 1)).Then(new AddPlayer("B", 1)).Then(new AddPlayer("C", 1))
            .WithRound(new Round(1, RoundStatus.Draft, ImmutableArray.Create(new Game(1, 2)), Bye: 3));

        Assert.Contains("已排入賽程", s.RuleViolation(new DeletePlayer(3)));
    }
}
