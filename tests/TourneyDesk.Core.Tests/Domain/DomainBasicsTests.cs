using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Tests.Domain;

public class DomainBasicsTests
{
    [Theory]
    [InlineData(GameResult.WhiteWin, 2, 0)]
    [InlineData(GameResult.Draw, 1, 1)]
    [InlineData(GameResult.BlackWin, 0, 2)]
    [InlineData(GameResult.WhiteForfeit, 0, 2)]
    [InlineData(GameResult.BlackForfeit, 2, 0)]
    [InlineData(GameResult.DoubleForfeit, 0, 0)]
    [InlineData(GameResult.None, 0, 0)]
    public void Result_points_follow_assumptions(GameResult result, int white, int black)
    {
        Assert.Equal((white, black), (result.WhitePoints(), result.BlackPoints()));
    }

    [Theory]
    [InlineData(GameResult.WhiteWin, true, false)]
    [InlineData(GameResult.Draw, true, false)]
    [InlineData(GameResult.BlackWin, true, false)]
    [InlineData(GameResult.WhiteForfeit, false, true)]
    [InlineData(GameResult.BlackForfeit, false, true)]
    [InlineData(GameResult.DoubleForfeit, false, true)]
    [InlineData(GameResult.None, false, false)]
    public void Played_and_forfeit_classification(GameResult result, bool played, bool forfeit)
    {
        Assert.Equal((played, forfeit), (result.IsPlayed(), result.IsForfeit()));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.5")]
    [InlineData(5, "2.5")]
    [InlineData(8, "4")]
    [InlineData(-3, "-1.5")]
    public void Half_points_format(long value, string expected)
    {
        Assert.Equal(expected, HalfPoints.Format(value));
    }

    [Theory]
    [InlineData(null, null, 1, true)]
    [InlineData(3, null, 2, false)]
    [InlineData(3, null, 3, true)]
    [InlineData(null, 4, 3, true)]
    [InlineData(null, 4, 4, false)]
    [InlineData(2, 4, 5, false)]
    public void Player_activity_by_round(int? join, int? withdraw, int round, bool active)
    {
        var p = new Player(1, 1, "A", 1000, join, withdraw);

        Assert.Equal(active, p.IsActiveIn(round));
    }

    [Theory]
    [InlineData(TournamentFormat.Swiss, 2)]
    [InlineData(TournamentFormat.RoundRobin, 0)]
    [InlineData(TournamentFormat.DoubleRoundRobin, 0)]
    public void Default_bye_points_depend_on_format(TournamentFormat format, int expected)
    {
        Assert.Equal(expected, TournamentSettings.Create("賽事", format).ByePoints);
    }

    [Theory]
    [InlineData("", 5, 2, "賽事名稱不可空白。")]
    [InlineData("A", 0, 2, "瑞士制總輪數必須介於 1 到 15。")]
    [InlineData("A", 16, 2, "瑞士制總輪數必須介於 1 到 15。")]
    [InlineData("A", 5, 3, "輪空得分只能是 0、1 或 2 個半分。")]
    [InlineData("A", 5, -1, "輪空得分只能是 0、1 或 2 個半分。")]
    public void Settings_validation(string name, int rounds, int bye, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => TournamentSettings.Create(name, TournamentFormat.Swiss, rounds, bye));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void Round_completion_requires_publication_and_all_results()
    {
        var draft = new Round(1, RoundStatus.Draft, [new Game(1, 2, GameResult.Draw)], null);
        var open = new Round(1, RoundStatus.Published, [new Game(1, 2, GameResult.Draw), new Game(3, 4)], null);
        var done = open with { Games = [new Game(1, 2, GameResult.Draw), new Game(3, 4, GameResult.DoubleForfeit)] };

        Assert.False(draft.IsCompleted);
        Assert.False(open.IsCompleted);
        Assert.True(done.IsCompleted);
    }

    [Fact]
    public void Game_opponent_and_points()
    {
        var g = new Game(1, 2, GameResult.BlackForfeit);

        Assert.Equal(2, g.OpponentOf(1));
        Assert.Equal(1, g.OpponentOf(2));
        Assert.Equal(2, g.PointsFor(1));
        Assert.Equal(0, g.PointsFor(2));
        Assert.Throws<ArgumentException>(() => g.OpponentOf(3));
    }
}
