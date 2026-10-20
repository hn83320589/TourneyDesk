using TourneyDesk.Core.Domain;
using TourneyDesk.Core.RoundRobin;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

/// <summary>開始循環賽：一次產生全部賽程並發布；此後選手固定。</summary>
public sealed record StartRoundRobin : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (!state.Settings.Format.IsRoundRobin())
            throw new DomainException("此操作只適用於循環賽。");
        if (state.IsStarted)
            throw new DomainException("循環賽已經開始。");

        var seeded = RoundRobinScheduler.Seed(state.Players.Values);
        var rounds = RoundRobinScheduler.Build(seeded, state.Settings.Format == TournamentFormat.DoubleRoundRobin);
        var next = state with { Rounds = rounds };
        return new Transition(next, $"產生{state.Settings.Format.DisplayName()}賽程",
            [context.Audit("產生賽程", state.Settings.Format.DisplayName(), null,
                $"{seeded.Length} 位選手、{rounds.Length} 輪")]);
    }
}
