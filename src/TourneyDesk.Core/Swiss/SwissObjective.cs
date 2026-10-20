using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Swiss;

/// <summary>
/// 配對方案的字典序目標值（越小越好）：
/// ① 重複對戰場數 → ② 輪空者是否曾輪空 → ③ 輪空者賽前排名（越後面越好）→ ④ 分差平方和 → ⑤ (hi, lo) 序列。
/// </summary>
/// <param name="ByeRank">輪空者的賽前排名；沒有輪空為 null。</param>
/// <param name="Pairs">每場的 (hi, lo) 賽前排名（hi &lt; lo），依 hi 由小到大。</param>
public sealed record ObjectiveVector(
    int Repeats,
    int ByeRepeat,
    int? ByeRank,
    long ScoreDiffSquares,
    ImmutableArray<(int Hi, int Lo)> Pairs) : IComparable<ObjectiveVector>
{
    public int CompareTo(ObjectiveVector? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var c = Repeats.CompareTo(other.Repeats);
        if (c != 0)
            return c;
        c = ByeRepeat.CompareTo(other.ByeRepeat);
        if (c != 0)
            return c;
        c = (other.ByeRank ?? 0).CompareTo(ByeRank ?? 0); // 排名數字越大越好
        if (c != 0)
            return c;
        c = ScoreDiffSquares.CompareTo(other.ScoreDiffSquares);
        if (c != 0)
            return c;
        for (var i = 0; i < Math.Min(Pairs.Length, other.Pairs.Length); i++)
        {
            c = Pairs[i].Hi.CompareTo(other.Pairs[i].Hi); // hi 小者優先
            if (c != 0)
                return c;
            c = other.Pairs[i].Lo.CompareTo(Pairs[i].Lo); // hi 相同時 lo 大者優先
            if (c != 0)
                return c;
        }

        return Pairs.Length.CompareTo(other.Pairs.Length);
    }

    /// <summary>與另一方案在 ①～④ 上是否相同。</summary>
    public bool SamePrimaryAs(ObjectiveVector other) =>
        Repeats == other.Repeats && ByeRepeat == other.ByeRepeat && ByeRank == other.ByeRank
        && ScoreDiffSquares == other.ScoreDiffSquares;
}

public enum ViolationKind
{
    RepeatPairing,
    RepeatBye,
}

/// <param name="PlayerB">重複對戰的另一位；重複輪空為 null。</param>
public sealed record PairingViolation(ViolationKind Kind, int PlayerA, int? PlayerB);

public sealed record PairingEvaluation(ObjectiveVector Objective, ImmutableArray<PairingViolation> Violations);

public static class SwissObjective
{
    /// <summary>評估任意配對方案（例如手動交換後）。方案必須恰好涵蓋每位可配對選手一次。</summary>
    public static PairingEvaluation Evaluate(SwissRoundInput input, Pairing pairing)
    {
        ValidateCoverage(input, pairing);

        var violations = ImmutableArray.CreateBuilder<PairingViolation>();
        var repeats = 0;
        long squares = 0;
        var pairs = new List<(int Hi, int Lo)>(pairing.Games.Length);

        foreach (var g in pairing.Games)
        {
            if (input.HaveMet(g.White, g.Black))
            {
                repeats++;
                violations.Add(new PairingViolation(ViolationKind.RepeatPairing, g.White, g.Black));
            }

            long diff = input.Get(g.White).Score - input.Get(g.Black).Score;
            squares += diff * diff;
            var ra = input.RankOf[g.White];
            var rb = input.RankOf[g.Black];
            pairs.Add((Math.Min(ra, rb), Math.Max(ra, rb)));
        }

        var byeRepeat = 0;
        int? byeRank = null;
        if (pairing.Bye is { } bye)
        {
            byeRank = input.RankOf[bye];
            if (input.Get(bye).HadBye)
            {
                byeRepeat = 1;
                violations.Add(new PairingViolation(ViolationKind.RepeatBye, bye, null));
            }
        }

        pairs.Sort((x, y) => x.Hi.CompareTo(y.Hi));
        return new PairingEvaluation(
            new ObjectiveVector(repeats, byeRepeat, byeRank, squares, [.. pairs]),
            violations.ToImmutable());
    }

    private static void ValidateCoverage(SwissRoundInput input, Pairing pairing)
    {
        var seen = new HashSet<int>();
        foreach (var id in pairing.Games.SelectMany(g => new[] { g.White, g.Black }).Concat(pairing.Bye is { } b ? [b] : []))
        {
            if (!input.RankOf.ContainsKey(id))
                throw new DomainException($"配對中有不可配對的選手（內部編號 {id}）。");
            if (!seen.Add(id))
                throw new DomainException($"配對中選手（內部編號 {id}）出現超過一次。");
        }

        if (seen.Count != input.Count)
            throw new DomainException("配對未涵蓋所有可配對選手。");
        if ((input.Count % 2 == 1) != pairing.Bye.HasValue)
            throw new DomainException(input.Count % 2 == 1 ? "人數為奇數時必須有一位輪空。" : "人數為偶數時不得有輪空。");
    }
}
