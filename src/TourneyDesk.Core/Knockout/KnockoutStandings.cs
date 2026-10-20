using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Knockout;

public sealed record KnockoutPlace(int Rank, int PlayerId, int RegNo, string Name, string Placement);

/// <summary>
/// 淘汰賽名次由籤表位置決定（不使用同分判定）：冠軍、亞軍、季軍戰勝負（無季軍戰時準決賽敗者並列第 3），
/// 其餘依被淘汰的輪次並列；尚未被淘汰者並列在最前。並列者依註冊編號排列。
/// </summary>
public static class KnockoutStandings
{
    public static ImmutableArray<KnockoutPlace> Compute(TournamentState state)
    {
        var bracket = state.Bracket ?? throw new DomainException("尚未產生籤表。");

        // 排序鍵：越大越好。冠軍 = 3R+3、亞軍 = 3R+2、季軍 = 3R+1、殿軍 = 3R、第 r 輪敗者 = 3r
        var key = new Dictionary<int, (int Key, string Label)>();
        var finalRound = bracket.RoundCount;
        foreach (var m in bracket.Matches.Where(m => !m.IsThirdPlace && m.Outcome is { Kind: not KnockoutOutcomeKind.Bye }))
        {
            var loser = Loser(bracket, m);
            key[loser] = m.Round == finalRound ? ((3 * finalRound) + 2, "亞軍") : (3 * m.Round, m.Round == finalRound - 1 ? "準決賽" : $"第 {m.Round} 輪淘汰");
        }

        if (bracket.Final.Outcome is { } final)
            key[final.Winner] = ((3 * finalRound) + 3, "冠軍");
        if (bracket.ThirdPlace is { Outcome: { } third } tp)
        {
            key[third.Winner] = ((3 * finalRound) + 1, "季軍");
            if (third.Kind != KnockoutOutcomeKind.Bye)
                key[Loser(bracket, tp)] = (3 * finalRound, "殿軍");
        }

        var ordered = bracket.Seeds
            .Select(id => (Id: id, Entry: key.TryGetValue(id, out var k) ? k : (int.MaxValue, "晉級中")))
            .OrderByDescending(x => x.Entry.Item1)
            .ThenBy(x => state.Players[x.Id].RegNo)
            .ToList();

        var rows = ImmutableArray.CreateBuilder<KnockoutPlace>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var rank = i > 0 && ordered[i].Entry.Item1 == ordered[i - 1].Entry.Item1 ? rows[i - 1].Rank : i + 1;
            var p = state.Players[ordered[i].Id];
            rows.Add(new KnockoutPlace(rank, p.Id, p.RegNo, p.Name, ordered[i].Entry.Item2));
        }

        return rows.MoveToImmutable();
    }

    private static int Loser(Bracket bracket, KnockoutMatch m)
    {
        var (a, b) = bracket.Participants(m);
        return a.PlayerId == m.Outcome!.Winner ? b.PlayerId!.Value : a.PlayerId!.Value;
    }
}
