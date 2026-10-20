namespace TourneyDesk.Core.Domain;

public enum TournamentFormat
{
    Swiss,
    RoundRobin,
    DoubleRoundRobin,
    SingleElimination,
}

public static class TournamentFormatExtensions
{
    public static bool IsRoundRobin(this TournamentFormat format) =>
        format is TournamentFormat.RoundRobin or TournamentFormat.DoubleRoundRobin;

    public static string DisplayName(this TournamentFormat format) => format switch
    {
        TournamentFormat.Swiss => "瑞士制",
        TournamentFormat.RoundRobin => "單循環",
        TournamentFormat.DoubleRoundRobin => "雙循環",
        TournamentFormat.SingleElimination => "單淘汰",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
