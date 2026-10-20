using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Tests.TestSupport;

internal static class Commands
{
    public static readonly CommandContext Context = new(new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero));

    /// <summary>套用命令並回傳新狀態（測試中省略 Session 與檔案層）。</summary>
    public static TournamentState Then(this TournamentState state, ITournamentCommand command) =>
        command.Apply(state, Context).NewState;

    public static Transition Run(this TournamentState state, ITournamentCommand command) =>
        command.Apply(state, Context);

    public static string RuleViolation(this TournamentState state, ITournamentCommand command) =>
        Assert.Throws<DomainException>(() => command.Apply(state, Context)).Message;
}
