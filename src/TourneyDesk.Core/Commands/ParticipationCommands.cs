using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

/// <summary>
/// 退賽，自第 <paramref name="FromRound"/> 輪起生效：該輪起不再配對；
/// 已發布但尚未輸入結果的對局判退賽者棄權（對手得 2 半分）；含該選手的草稿作廢。
/// 已退賽選手保留在積分榜，分數維持退賽當下的累計值。
/// </summary>
public sealed record WithdrawPlayer(int PlayerId, int FromRound) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.Settings.Format == TournamentFormat.SingleElimination)
            throw new DomainException("淘汰賽請在籤表中處理退賽。");
        var player = state.GetPlayer(PlayerId);
        if (!state.IsStarted)
            throw new DomainException("賽事尚未開始，請直接刪除選手。");
        if (player.IsWithdrawn)
            throw new DomainException($"{Describe.Player(state, PlayerId)} 已經退賽。");
        if (FromRound < player.FirstRound)
            throw new DomainException($"退賽生效輪不可早於其開始參賽的第 {player.FirstRound} 輪。");

        var next = state.WithPlayer(player with { WithdrawRound = FromRound });
        var audit = new List<AuditEntry>
        {
            context.Audit("退賽", Describe.Player(state, PlayerId), null, $"自第 {FromRound} 輪起"),
        };

        foreach (var round in state.Rounds.Where(r => r.Number >= FromRound && r.Involves(PlayerId)))
        {
            if (round.Status == RoundStatus.Draft)
            {
                next = next.TruncateRounds(round.Number);
                audit.Add(context.Audit("作廢草稿", $"第 {round.Number} 輪", "含退賽選手", null));
                continue;
            }

            if (round.Bye == PlayerId)
            {
                // 分數凍結：退賽後的輪空（循環賽固定賽程）不再計分
                next = next.WithRound(next.GetRound(round.Number) with { Bye = null });
                audit.Add(context.Audit("取消輪空", $"第 {round.Number} 輪 {Describe.Player(state, PlayerId)}", "輪空", null));
                continue;
            }

            if (round.GameIndexOf(PlayerId) is not { } index || round.Games[index].Result.HasResult())
                continue;

            var game = round.Games[index];
            var forfeit = game.White == PlayerId ? GameResult.WhiteForfeit : GameResult.BlackForfeit;
            next = next.WithRound(next.GetRound(round.Number) with { Games = round.Games.SetItem(index, game with { Result = forfeit }) });
            audit.Add(context.Audit("登錄成績", $"{Describe.Board(state, round.Number, index + 1)} {Describe.Game(state, game)}",
                GameResult.None.DisplayName(), $"{forfeit.DisplayName()}（退賽）"));
        }

        return new Transition(next, $"{Describe.Player(state, PlayerId)} 自第 {FromRound} 輪起退賽", audit);
    }
}

/// <summary>瑞士制遲到加入：自第 <paramref name="FromRound"/> 輪起參賽，之前輪次得分為 0、不產生虛擬對手。</summary>
public sealed record LateJoinPlayer(string Name, int Rating, int FromRound) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.Settings.Format != TournamentFormat.Swiss)
            throw new DomainException($"{state.Settings.Format.DisplayName()}開賽後不可遲到加入。");
        if (FromRound < 2 || FromRound > state.Settings.SwissRounds)
            throw new DomainException($"遲到加入的起始輪必須介於第 2 輪與第 {state.Settings.SwissRounds} 輪之間。");
        var published = state.Rounds.Count(r => r.IsPublished);
        if (FromRound <= published)
            throw new DomainException($"第 {FromRound} 輪已發布，遲到加入最早只能從第 {published + 1} 輪開始。");
        if (state.LastRound is { Status: RoundStatus.Draft } draft && draft.Number >= FromRound)
            throw new DomainException($"第 {draft.Number} 輪已有草稿，請先作廢後再加入選手。");

        var (next, player) = PlayerFactory.Add(state, Name, Rating, FromRound);
        return new Transition(next, $"遲到加入 #{player.RegNo} {player.Name}",
            [context.Audit("遲到加入", $"#{player.RegNo} {player.Name}", null,
                $"{PlayerText.Fields(player)}，自第 {FromRound} 輪起")]);
    }
}
