using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Commands;

/// <summary>異動紀錄與確認清單使用的文字描述。</summary>
internal static class Describe
{
    public static string Player(TournamentState s, int id) =>
        s.Players.TryGetValue(id, out var p) ? $"#{p.RegNo} {p.Name}" : $"(內部編號 {id})";

    public static string Board(TournamentState s, int round, int board) =>
        $"第 {round} 輪 第 {board} 桌";

    public static string Game(TournamentState s, Game g) =>
        $"{Player(s, g.White)} vs {Player(s, g.Black)}";

    public static string GameWithResult(TournamentState s, Game g) =>
        g.Result.HasResult() ? $"{Game(s, g)} {g.Result.DisplayName()}" : Game(s, g);

    public static string Pairing(TournamentState s, Pairing p)
    {
        var parts = p.Games.Select((g, i) => $"{i + 1}:{Player(s, g.White)}-{Player(s, g.Black)}").ToList();
        if (p.Bye is { } bye)
            parts.Add($"輪空:{Player(s, bye)}");
        return string.Join(", ", parts);
    }
}
