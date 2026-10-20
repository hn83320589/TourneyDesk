using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

/// <summary>
/// 登錄或更正一場對局的成績（瑞士制與循環賽）。已有成績者為更正。
/// 更正不會改動任何已發布的配對；較後輪次是否「過期」由 <see cref="Swiss.SwissStaleness"/> 另行判定。
/// </summary>
public sealed record SetResult(int RoundNumber, int Board, GameResult Result) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.Settings.Format == TournamentFormat.SingleElimination)
            throw new DomainException("淘汰賽請在籤表中登錄勝者。");
        if (!Enum.IsDefined(Result))
            throw new DomainException("未知的結果類型。");

        var round = state.GetRound(RoundNumber);
        if (!round.IsPublished)
            throw new DomainException($"第 {RoundNumber} 輪尚未發布，不能登錄成績。");
        if (Board < 1 || Board > round.Games.Length)
            throw new DomainException($"第 {RoundNumber} 輪沒有第 {Board} 桌。");

        var game = round.Games[Board - 1];
        if (game.Result == Result)
            throw new DomainException("成績沒有變更。");
        if (Result == GameResult.None && state.Settings.Format == TournamentFormat.Swiss && RoundNumber < state.Rounds.Length)
            throw new DomainException("已有後續輪次，不能清除此成績（可改為更正）。");

        var updated = round with { Games = round.Games.SetItem(Board - 1, game with { Result = Result }) };
        var kind = game.Result.HasResult() ? "更正成績" : "登錄成績";
        var target = $"{Describe.Board(state, RoundNumber, Board)} {Describe.Game(state, game)}";
        return new Transition(state.WithRound(updated), $"{kind}：{target} {Result.DisplayName()}",
            [context.Audit(kind, target, game.Result.DisplayName(), Result.DisplayName())]);
    }
}
