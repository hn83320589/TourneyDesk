using System.Collections.Immutable;

namespace TourneyDesk.Core.Domain;

/// <summary>
/// 一場賽事的完整狀態（不可變）。排名、同分值、過期標記等衍生資料一律即時計算，不存在這裡。
/// </summary>
public sealed record TournamentState(
    TournamentSettings Settings,
    ImmutableSortedDictionary<int, Player> Players,
    ImmutableArray<Round> Rounds,
    int NextPlayerId,
    int NextRegNo,
    Bracket? Bracket = null)
{
    public const int MaxPlayers = 500;

    public static TournamentState Create(TournamentSettings settings)
    {
        settings.Validate();
        return new TournamentState(settings, ImmutableSortedDictionary<int, Player>.Empty, [], 1, 1);
    }

    /// <summary>已產生任何賽程（輪次或籤表）即視為開賽（循環賽與淘汰賽此後不可增刪選手）。</summary>
    public bool IsStarted => !Rounds.IsEmpty || Bracket is not null;

    public Player GetPlayer(int id) =>
        Players.TryGetValue(id, out var p) ? p : throw new DomainException($"找不到選手（內部編號 {id}）。");

    public Round GetRound(int number) =>
        number >= 1 && number <= Rounds.Length
            ? Rounds[number - 1]
            : throw new DomainException($"第 {number} 輪不存在。");

    public Round? LastRound => Rounds.IsEmpty ? null : Rounds[^1];

    public bool IsScheduled(int playerId) =>
        Rounds.Any(r => r.Involves(playerId)) || (Bracket?.Contains(playerId) ?? false);

    public TournamentState WithPlayer(Player player) => this with { Players = Players.SetItem(player.Id, player) };

    public TournamentState WithRound(Round round)
    {
        if (round.Number < 1 || round.Number > Rounds.Length + 1)
            throw new ArgumentOutOfRangeException(nameof(round), $"輪次編號 {round.Number} 不連續。");
        return this with
        {
            Rounds = round.Number == Rounds.Length + 1 ? Rounds.Add(round) : Rounds.SetItem(round.Number - 1, round),
        };
    }

    /// <summary>移除第 <paramref name="fromRound"/> 輪（含）之後的所有輪次。</summary>
    public TournamentState TruncateRounds(int fromRound) =>
        this with { Rounds = Rounds.RemoveRange(fromRound - 1, Rounds.Length - fromRound + 1) };
}
