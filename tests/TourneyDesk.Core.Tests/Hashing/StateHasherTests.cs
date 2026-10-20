using System.Collections.Immutable;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Hashing;

public class StateHasherTests
{
    private static TournamentState Sample()
    {
        var s = TournamentState.Create(TournamentSettings.Create("測試賽", TournamentFormat.Swiss))
            .Then(new AddPlayer("A", 1500)).Then(new AddPlayer("B", 1400)).Then(new AddPlayer("C", 1300));
        return s.WithRound(new Round(1, RoundStatus.Published, [new Game(1, 2, GameResult.Draw)], 3,
            new Pairing([new PairedGame(1, 2)], 3)));
    }

    [Fact]
    public void Equal_content_built_separately_has_equal_hash()
    {
        Assert.Equal(StateHasher.Compute(Sample()), StateHasher.Compute(Sample()));
    }

    public static TheoryData<string, Func<TournamentState, TournamentState>> Mutations => new()
    {
        { "name", s => s with { Settings = s.Settings with { Name = "X" } } },
        { "bye points", s => s with { Settings = s.Settings with { ByePoints = 1 } } },
        { "rounds", s => s with { Settings = s.Settings with { SwissRounds = 7 } } },
        { "third place", s => s with { Settings = s.Settings with { ThirdPlaceMatch = true } } },
        { "player name", s => s.WithPlayer(s.Players[1] with { Name = "Z" }) },
        { "rating", s => s.WithPlayer(s.Players[1] with { Rating = 1 }) },
        { "join", s => s.WithPlayer(s.Players[1] with { JoinRound = 2 }) },
        { "withdraw", s => s.WithPlayer(s.Players[1] with { WithdrawRound = 2 }) },
        { "next reg", s => s with { NextRegNo = 99 } },
        { "result", s => s.WithRound(s.Rounds[0] with { Games = [new Game(1, 2, GameResult.WhiteWin)] }) },
        { "colors", s => s.WithRound(s.Rounds[0] with { Games = [new Game(2, 1, GameResult.Draw)] }) },
        { "status", s => s.WithRound(s.Rounds[0] with { Status = RoundStatus.Draft }) },
        { "bye", s => s.WithRound(s.Rounds[0] with { Bye = null }) },
        { "forced", s => s.WithRound(s.Rounds[0] with { ForcedOverride = true }) },
        { "proposal", s => s.WithRound(s.Rounds[0] with { EngineProposal = null }) },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Any_field_change_changes_hash(string _, Func<TournamentState, TournamentState> mutate)
    {
        Assert.NotEqual(StateHasher.Compute(Sample()), StateHasher.Compute(mutate(Sample())));
    }

    /// <summary>守衛：狀態型別新增欄位時，這個測試會失敗，提醒同步更新 StateHasher。</summary>
    [Theory]
    [InlineData(typeof(TournamentState), 6)]
    [InlineData(typeof(Bracket), 3)]
    [InlineData(typeof(KnockoutMatch), 7)]
    [InlineData(typeof(KnockoutOutcome), 2)]
    [InlineData(typeof(TournamentSettings), 5)]
    [InlineData(typeof(Player), 6)]
    [InlineData(typeof(Round), 6)]
    [InlineData(typeof(Game), 3)]
    [InlineData(typeof(Pairing), 2)]
    public void Hashed_types_have_expected_constructor_shape(Type type, int parameterCount)
    {
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        Assert.Equal(parameterCount, ctor.GetParameters().Length);
    }

    [Fact]
    public void Pairing_hash_depends_on_colors_and_bye()
    {
        var a = new Pairing(ImmutableArray.Create(new PairedGame(1, 2)), 3);
        var b = new Pairing(ImmutableArray.Create(new PairedGame(2, 1)), 3);
        var c = new Pairing(ImmutableArray.Create(new PairedGame(1, 2)), null);

        Assert.Equal(StateHasher.Compute(a), StateHasher.Compute(a with { }));
        Assert.NotEqual(StateHasher.Compute(a), StateHasher.Compute(b));
        Assert.NotEqual(StateHasher.Compute(a), StateHasher.Compute(c));
    }
}
