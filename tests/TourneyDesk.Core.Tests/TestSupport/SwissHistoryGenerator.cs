using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>
/// 產生隨機的瑞士制歷史（含遲到加入、退賽、輪空、各種棄權、重複對戰），回傳狀態與「下一輪」輪次。
/// 先前輪次的配對是隨機的（不一定合乎引擎規則），以涵蓋更多邊界情況。
/// </summary>
internal static class SwissHistoryGenerator
{
    public static (TournamentState State, int NextRound) Generate(Random rng, int playerCount, int? previousRounds = null)
    {
        var played = previousRounds ?? rng.Next(0, 6);
        var nextRound = played + 1;

        // 少量積分範圍讓積分相同的情況經常發生（考驗以註冊編號排序）
        var ratingPool = rng.Next(2) == 0 ? 3 : 3000;
        var players = new List<Player>();
        for (var regNo = 1; regNo <= playerCount; regNo++)
        {
            int? join = nextRound >= 2 && rng.NextDouble() < 0.15 ? rng.Next(2, nextRound + 1) : null;
            int? withdraw = nextRound >= 2 && rng.NextDouble() < 0.1 ? rng.Next(2, nextRound + 1) : null;
            if (join is not null && withdraw is not null && withdraw <= join)
                withdraw = null;
            players.Add(new Player(regNo, regNo, $"P{regNo}", rng.Next(0, ratingPool + 1), join, withdraw));
        }

        var byePoints = rng.Next(0, 3);
        var settings = TournamentSettings.Create("隨機", TournamentFormat.Swiss, 15, byePoints);
        var state = new TournamentState(settings, players.ToImmutableSortedDictionary(p => p.Id, p => p), [],
            playerCount + 1, playerCount + 1);

        for (var r = 1; r <= played; r++)
        {
            var active = players.Where(p => p.IsActiveIn(r)).Select(p => p.Id).OrderBy(_ => rng.Next()).ToList();
            int? bye = null;
            if (active.Count % 2 == 1)
            {
                bye = active[^1];
                active.RemoveAt(active.Count - 1);
            }

            var games = new List<Game>();
            for (var i = 0; i + 1 < active.Count; i += 2)
                games.Add(new Game(active[i], active[i + 1], RandomResult(rng)));
            state = state.WithRound(new Round(r, RoundStatus.Published, [.. games], bye));
        }

        return (state, nextRound);
    }

    public static GameResult RandomResult(Random rng) => rng.Next(100) switch
    {
        < 33 => GameResult.WhiteWin,
        < 66 => GameResult.BlackWin,
        < 85 => GameResult.Draw,
        < 90 => GameResult.WhiteForfeit,
        < 95 => GameResult.BlackForfeit,
        _ => GameResult.DoubleForfeit,
    };

    /// <summary>重新指派內部 Id（保留註冊編號），用於決定性測試。</summary>
    public static TournamentState RemapIds(TournamentState state, Random rng)
    {
        var ids = state.Players.Keys.ToList();
        var fresh = Enumerable.Range(1000, ids.Count * 3).OrderBy(_ => rng.Next()).Take(ids.Count).ToList();
        var map = ids.Zip(fresh).ToDictionary(x => x.First, x => x.Second);
        var players = state.Players.Values.Select(p => p with { Id = map[p.Id] }).ToImmutableSortedDictionary(p => p.Id, p => p);
        var rounds = state.Rounds.Select(r => r with
        {
            Games = [.. r.Games.Select(g => g with { White = map[g.White], Black = map[g.Black] })],
            Bye = r.Bye is { } b ? map[b] : null,
        }).ToImmutableArray();
        return state with { Players = players, Rounds = rounds, NextPlayerId = fresh.Max() + 1 };
    }
}
