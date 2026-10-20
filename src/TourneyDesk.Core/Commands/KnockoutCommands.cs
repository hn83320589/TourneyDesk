using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

internal static class KnockoutGuard
{
    public static Bracket Require(TournamentState s)
    {
        if (s.Settings.Format != TournamentFormat.SingleElimination)
            throw new DomainException("此操作只適用於單淘汰。");
        return s.Bracket ?? throw new DomainException("尚未產生籤表。");
    }
}

/// <summary>開始單淘汰：建立籤表並自動處理輪空；此後選手固定。</summary>
public sealed record StartKnockout : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.Settings.Format != TournamentFormat.SingleElimination)
            throw new DomainException("此操作只適用於單淘汰。");
        if (state.IsStarted)
            throw new DomainException("籤表已經產生。");

        var thirdPlace = state.Settings.ThirdPlaceMatch && state.Players.Count >= 3;
        var bracket = KnockoutBracketBuilder.Build(KnockoutBracketBuilder.Seed(state.Players.Values), thirdPlace);
        bracket = KnockoutRules.Normalize(bracket, state.Players);
        return new Transition(state with { Bracket = bracket }, "產生淘汰籤表",
            [context.Audit("產生籤表", "單淘汰", null,
                $"{bracket.Seeds.Length} 位選手、籤表大小 {bracket.Size}、輪空 {bracket.Size - bracket.Seeds.Length} 位{(thirdPlace ? "、含季軍戰" : "")}")]);
    }
}

/// <summary>登錄某場勝者（淘汰賽不允許和局）。已有勝者時請用 <see cref="CorrectKnockoutWinner"/>。</summary>
public sealed record RecordKnockoutWinner(int MatchId, int Winner) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var bracket = KnockoutGuard.Require(state);
        var match = bracket.GetMatch(MatchId);
        var label = KnockoutRules.MatchLabel(bracket, match);
        if (match.Outcome is not null)
            throw new DomainException($"{label}已有勝者，請使用「更正勝者」。");
        var (a, b) = bracket.Participants(match);
        if (!a.IsKnown || !b.IsKnown)
            throw new DomainException($"{label}的對戰雙方尚未確定。");
        if (Winner != a.PlayerId && Winner != b.PlayerId)
            throw new DomainException("勝者必須是此場的參賽者之一。");

        var updated = bracket with { Matches = bracket.Matches.SetItem(MatchId - 1, match with { Outcome = new KnockoutOutcome(Winner, KnockoutOutcomeKind.Played) }) };
        updated = KnockoutRules.Normalize(updated, state.Players);
        var target = $"{label} {Describe.Player(state, a.PlayerId!.Value)} vs {Describe.Player(state, b.PlayerId!.Value)}";
        return new Transition(state with { Bracket = updated }, $"登錄勝者：{target}",
            [context.Audit("登錄勝者", target, null, Describe.Player(state, Winner))]);
    }
}

/// <summary>
/// 更正已完成場次的勝者：所有依賴舊勝者（含季軍戰依賴的敗者）的後續場次勝者與結果連鎖清除。
/// 執行前須以 <see cref="KnockoutRules.CascadeImpact"/> 列出影響並確認（比對指紋）。整個動作為一步，可復原。
/// </summary>
public sealed record CorrectKnockoutWinner(int MatchId, int NewWinner, string ConfirmedImpactFingerprint) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var bracket = KnockoutGuard.Require(state);
        var match = bracket.GetMatch(MatchId);
        var label = KnockoutRules.MatchLabel(bracket, match);
        if (match.Outcome is null)
            throw new DomainException($"{label}尚無勝者，請直接登錄。");
        if (match.Outcome.Kind == KnockoutOutcomeKind.Bye)
            throw new DomainException("輪空場次不能更正。");
        if (match.Outcome.Winner == NewWinner)
            throw new DomainException("勝者沒有變更。");
        var (a, b) = bracket.Participants(match);
        if (NewWinner != a.PlayerId && NewWinner != b.PlayerId)
            throw new DomainException("勝者必須是此場的參賽者之一。");

        var impact = KnockoutRules.CascadeImpact(state, MatchId);
        if (impact.Fingerprint != ConfirmedImpactFingerprint)
            throw new DomainException("影響範圍已變更，請重新檢視並確認。");

        var matches = bracket.Matches.SetItem(MatchId - 1, match with { Outcome = new KnockoutOutcome(NewWinner, KnockoutOutcomeKind.Played) });
        foreach (var id in KnockoutRules.DependentMatches(bracket, MatchId))
            matches = matches.SetItem(id - 1, matches[id - 1] with { Outcome = null });
        var updated = KnockoutRules.Normalize(bracket with { Matches = matches }, state.Players);

        var target = $"{label} {Describe.Player(state, a.PlayerId!.Value)} vs {Describe.Player(state, b.PlayerId!.Value)}";
        var audit = new List<AuditEntry>
        {
            context.Audit("更正勝者", target, Describe.Player(state, match.Outcome.Winner), Describe.Player(state, NewWinner)),
        };
        if (!impact.IsEmpty)
            audit.Add(context.Audit("連鎖清除", label + "之後", impact.Summary(), null));
        return new Transition(state with { Bracket = updated }, $"更正勝者：{target}", audit);
    }
}

/// <summary>淘汰賽退賽：尚未決定的場次以對手晉級處理（現在或對手確定時自動套用）。</summary>
public sealed record WithdrawFromKnockout(int PlayerId) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var bracket = KnockoutGuard.Require(state);
        var player = state.GetPlayer(PlayerId);
        if (player.IsWithdrawn)
            throw new DomainException($"{Describe.Player(state, PlayerId)} 已經退賽。");
        if (!bracket.Contains(PlayerId))
            throw new DomainException("選手不在籤表中。");

        // 退賽生效輪 = 其尚未決定的那一場所在輪次（已淘汰者為最後一輪之後）
        var pending = bracket.Matches.FirstOrDefault(m =>
        {
            if (m.Outcome is not null)
                return false;
            var (a, b) = bracket.Participants(m);
            return a.PlayerId == PlayerId || b.PlayerId == PlayerId;
        });
        var fromRound = pending?.Round ?? bracket.RoundCount + 1;
        var players = state.Players.SetItem(PlayerId, player with { WithdrawRound = fromRound });
        var updated = KnockoutRules.Normalize(bracket, players);
        return new Transition(state with { Players = players, Bracket = updated }, $"{Describe.Player(state, PlayerId)} 退賽",
            [context.Audit("退賽", Describe.Player(state, PlayerId), null, "淘汰賽：尚未決定的場次由對手晉級")]);
    }
}
