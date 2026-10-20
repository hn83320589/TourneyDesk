namespace TourneyDesk.Core.Tests.Reference;

/// <summary>
/// 瑞士制配對的窮舉參考解（N ≤ 12）：列舉所有「輪空者 × 完美匹配」，
/// 逐一計算 ①～⑤ 並以字典序取最小者，再依先後手規則決定先手。
/// 結果以註冊編號表示。
/// </summary>
internal static class BruteForceSwissPairing
{
    public sealed record Result(List<(int WhiteRegNo, int BlackRegNo)> Games, int? ByeRegNo);

    public static Result Solve(ReferenceRoundData data)
    {
        var n = data.Ranked.Length;
        if (n > 12)
            throw new ArgumentException("窮舉參考解只支援 N ≤ 12。");

        long[]? bestKey = null;
        List<(int, int)>? bestPairs = null;
        int? bestBye = null;

        var byeCandidates = n % 2 == 1 ? Enumerable.Range(0, n).Select(i => (int?)i) : [null];
        foreach (var bye in byeCandidates)
        {
            var rest = Enumerable.Range(0, n).Where(i => i != bye).ToArray();
            foreach (var pairs in Matchings(rest))
            {
                var key = Key(data, pairs, bye);
                if (bestKey is null || Compare(key, bestKey) < 0)
                {
                    bestKey = key;
                    bestPairs = pairs;
                    bestBye = bye;
                }
            }
        }

        var games = (bestPairs ?? []).Select(p => Colors(data, p.Item1, p.Item2)).ToList();
        return new Result(games, bestBye is { } b ? data.Ranked[b].RegNo : null);
    }

    /// <summary>①～④ 再接上 ⑤ 的 (hi, −lo) 序列，整體以字典序比較（越小越好）。索引即賽前排名 − 1。</summary>
    private static long[] Key(ReferenceRoundData d, List<(int, int)> pairs, int? bye)
    {
        long repeats = 0, squares = 0;
        foreach (var (a, b) in pairs)
        {
            if (d.HaveMet(d.Ranked[a].Id, d.Ranked[b].Id))
                repeats++;
            long diff = d.Ranked[a].Score - d.Ranked[b].Score;
            squares += diff * diff;
        }

        var key = new List<long>
        {
            repeats,
            bye is { } b1 && d.Ranked[b1].HadBye ? 1 : 0,
            bye is { } b2 ? -(b2 + 1) : 0, // 排名越大越好 → 取負
            squares,
        };
        foreach (var (hi, lo) in pairs.OrderBy(p => p.Item1)) // pairs 內已保證 Item1 < Item2
        {
            key.Add(hi);
            key.Add(-lo);
        }

        return [.. key];
    }

    private static int Compare(long[] x, long[] y)
    {
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            if (x[i] != y[i])
                return x[i].CompareTo(y[i]);
        return x.Length.CompareTo(y.Length);
    }

    private static (int, int) Colors(ReferenceRoundData d, int a, int b)
    {
        var pa = d.Ranked[a];
        var pb = d.Ranked[b];
        bool aWhite;
        if (pa.Whites != pb.Whites)
            aWhite = pa.Whites < pb.Whites;
        else if ((pa.LastWasWhite == false) != (pb.LastWasWhite == false))
            aWhite = pa.LastWasWhite == false;
        else
            aWhite = a < b;
        return aWhite ? (pa.RegNo, pb.RegNo) : (pb.RegNo, pa.RegNo);
    }

    private static IEnumerable<List<(int, int)>> Matchings(int[] items)
    {
        if (items.Length == 0)
        {
            yield return [];
            yield break;
        }

        var first = items[0];
        for (var j = 1; j < items.Length; j++)
        {
            var partner = items[j];
            var rest = items.Where((_, idx) => idx != 0 && idx != j).ToArray();
            foreach (var m in Matchings(rest))
            {
                m.Insert(0, (first, partner));
                yield return m;
            }
        }
    }
}
