using System.Collections.Immutable;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Knockout;

public static class KnockoutRules
{
    /// <summary>
    /// 依規則自動決定的場次（依場次順序一次處理，後面的場次只依賴前面的場次）：
    /// 一方為空位 → 另一方輪空晉級；一方已退賽 → 另一方 walkover 晉級；雙方都退賽 → 較高種子晉級。
    /// </summary>
    public static Bracket Normalize(Bracket bracket, IReadOnlyDictionary<int, Player> players)
    {
        var current = bracket;
        foreach (var match in bracket.Matches)
        {
            var m = current.GetMatch(match.Id);
            if (m.Outcome is not null)
                continue;
            var (a, b) = current.Participants(m);
            KnockoutOutcome? outcome = null;
            if (a.IsKnown && b.IsEmpty)
                outcome = new KnockoutOutcome(a.PlayerId!.Value, KnockoutOutcomeKind.Bye);
            else if (b.IsKnown && a.IsEmpty)
                outcome = new KnockoutOutcome(b.PlayerId!.Value, KnockoutOutcomeKind.Bye);
            else if (a.IsKnown && b.IsKnown)
            {
                var aOut = players[a.PlayerId!.Value].IsWithdrawn;
                var bOut = players[b.PlayerId!.Value].IsWithdrawn;
                if (aOut || bOut)
                {
                    var winner = aOut && bOut
                        ? (current.SeedOf(a.PlayerId.Value) < current.SeedOf(b.PlayerId.Value) ? a : b)
                        : aOut ? b : a;
                    outcome = new KnockoutOutcome(winner.PlayerId!.Value, KnockoutOutcomeKind.Walkover);
                }
            }

            if (outcome is not null)
                current = current with { Matches = current.Matches.SetItem(m.Id - 1, m with { Outcome = outcome }) };
        }

        return current;
    }

    /// <summary>更正第 <paramref name="matchId"/> 場的勝者時，所有依賴它（勝者或敗者）的後續場次，含連鎖依賴。</summary>
    public static ImmutableArray<int> DependentMatches(Bracket bracket, int matchId)
    {
        var affected = new HashSet<int> { matchId };
        var result = ImmutableArray.CreateBuilder<int>();
        foreach (var m in bracket.Matches.Where(m => m.Id > matchId))
        {
            if (affected.Any(id => m.A.DependsOn(id) || m.B.DependsOn(id)))
            {
                affected.Add(m.Id);
                result.Add(m.Id);
            }
        }

        return result.ToImmutable();
    }

    /// <summary>連鎖清除的影響清單：依賴舊勝者而已有結果的後續場次。</summary>
    public static ClearImpact CascadeImpact(TournamentState state, int matchId)
    {
        var bracket = state.Bracket ?? throw new DomainException("尚未產生籤表。");
        var items = DependentMatches(bracket, matchId)
            .Select(bracket.GetMatch)
            .Where(m => m.Outcome is not null)
            .Select(m => $"{MatchLabel(bracket, m)}：勝者 {Describe.Player(state, m.Outcome!.Winner)}（{OutcomeText(m.Outcome.Kind)}）將被清除")
            .ToImmutableArray();
        return new ClearImpact(items);
    }

    public static string MatchLabel(Bracket bracket, KnockoutMatch m)
    {
        if (m.IsThirdPlace)
            return "季軍戰";
        var fromEnd = bracket.RoundCount - m.Round;
        var name = fromEnd switch
        {
            0 => "決賽",
            1 => "準決賽",
            2 => "八強",
            _ => $"第 {m.Round} 輪",
        };
        return fromEnd == 0 ? name : $"{name}第 {m.Position + 1} 場";
    }

    public static string OutcomeText(KnockoutOutcomeKind kind) => kind switch
    {
        KnockoutOutcomeKind.Played => "比賽",
        KnockoutOutcomeKind.Walkover => "對手退賽",
        KnockoutOutcomeKind.Bye => "輪空",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
