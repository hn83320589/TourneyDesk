using System.Globalization;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

internal static class PlayerText
{
    public static string Label(Player p) => $"選手 #{p.RegNo} {p.Name}";

    public static string Fields(Player p) =>
        string.Create(CultureInfo.InvariantCulture, $"姓名={p.Name}, 積分={p.Rating}");
}

/// <summary>開賽前新增選手（註冊編號自動遞增）。瑞士制開賽後請用遲到加入。</summary>
public sealed record AddPlayer(string Name, int Rating) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.IsStarted)
        {
            throw new DomainException(state.Settings.Format == TournamentFormat.Swiss
                ? "賽事已開始，請使用「遲到加入」新增選手。"
                : $"{state.Settings.Format.DisplayName()}開賽後不可新增選手。");
        }

        var (next, player) = PlayerFactory.Add(state, Name, Rating, joinRound: null);
        return new Transition(next, $"新增{PlayerText.Label(player)}",
            [context.Audit("新增選手", PlayerText.Label(player), null, PlayerText.Fields(player))]);
    }
}

public sealed record EditPlayer(int PlayerId, string Name, int Rating) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var before = state.GetPlayer(PlayerId);
        var name = Name.Trim();
        Player.ValidateFields(name, Rating);
        var after = before with { Name = name, Rating = Rating };
        if (after == before)
            throw new DomainException("資料沒有變更。");

        return new Transition(state.WithPlayer(after), $"編輯{PlayerText.Label(after)}",
            [context.Audit("編輯選手", PlayerText.Label(before), PlayerText.Fields(before), PlayerText.Fields(after))]);
    }
}

/// <summary>刪除選手：只有尚未排入任何賽程者才能刪除。</summary>
public sealed record DeletePlayer(int PlayerId) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var player = state.GetPlayer(PlayerId);
        if (state.IsScheduled(PlayerId))
            throw new DomainException($"{PlayerText.Label(player)} 已排入賽程，不能刪除（可改為退賽）。");

        return new Transition(state with { Players = state.Players.Remove(PlayerId) }, $"刪除{PlayerText.Label(player)}",
            [context.Audit("刪除選手", PlayerText.Label(player), PlayerText.Fields(player), null)]);
    }
}

internal static class PlayerFactory
{
    public static (TournamentState State, Player Player) Add(TournamentState state, string name, int rating, int? joinRound)
    {
        name = name.Trim();
        Player.ValidateFields(name, rating);
        if (state.Players.Count >= TournamentState.MaxPlayers)
            throw new DomainException($"選手數已達上限 {TournamentState.MaxPlayers} 人。");

        var player = new Player(state.NextPlayerId, state.NextRegNo, name, rating, joinRound);
        var next = state.WithPlayer(player) with
        {
            NextPlayerId = state.NextPlayerId + 1,
            NextRegNo = state.NextRegNo + 1,
        };
        return (next, player);
    }
}
