using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>以命令建立瑞士制賽事的測試輔助。</summary>
internal static class SwissFlow
{
    /// <summary>建立瑞士制賽事，選手依序為 A、B、C…，積分遞減（2000、1900、…）。</summary>
    public static TournamentState NewSwiss(int players, int rounds = 5, int byePoints = 2)
    {
        var s = TournamentState.Create(TournamentSettings.Create("瑞士制測試", TournamentFormat.Swiss, rounds, byePoints));
        for (var i = 0; i < players; i++)
            s = s.Then(new AddPlayer(((char)('A' + i)).ToString(), 2000 - (100 * i)));
        return s;
    }

    /// <summary>以引擎產生下一輪草稿。</summary>
    public static TournamentState Draft(this TournamentState s)
    {
        var round = s.Rounds.Length + 1;
        return s.Then(new GenerateSwissRound(round, SwissRoundPlanner.Propose(s, round).Pairing));
    }

    /// <summary>產生並發布下一輪。</summary>
    public static TournamentState NextRound(this TournamentState s)
    {
        var drafted = s.Draft();
        return drafted.Then(new PublishRound(drafted.Rounds.Length));
    }

    /// <summary>依桌號順序登錄最後一輪成績。</summary>
    public static TournamentState Results(this TournamentState s, params GameResult[] results)
    {
        var round = s.Rounds.Length;
        for (var i = 0; i < results.Length; i++)
            s = s.Then(new SetResult(round, i + 1, results[i]));
        return s;
    }

    public static int Id(this TournamentState s, string name) => s.Players.Values.Single(p => p.Name == name).Id;

    /// <summary>以「A-D B-C bye:E」格式描述一輪配對（先手在前）。</summary>
    public static string Show(this TournamentState s, int round)
    {
        var r = s.GetRound(round);
        var parts = r.Games.Select(g => $"{s.Players[g.White].Name}-{s.Players[g.Black].Name}").ToList();
        if (r.Bye is { } b)
            parts.Add($"bye:{s.Players[b].Name}");
        return string.Join(" ", parts);
    }
}
