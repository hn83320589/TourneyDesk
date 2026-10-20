using TourneyDesk.Core.Matching;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>暴力列舉工具（測試用參考解，與正式程式碼無關）。</summary>
internal static class BruteForce
{
    /// <summary>列舉 0..n-1（n 為偶數）的所有完美匹配；每個匹配的邊以 (小, 大) 表示，依小端排序。</summary>
    public static IEnumerable<List<(int A, int B)>> PerfectMatchings(int n)
    {
        var used = new bool[n];
        var current = new List<(int, int)>();
        return Recurse();

        IEnumerable<List<(int A, int B)>> Recurse()
        {
            var first = Array.IndexOf(used, false);
            if (first < 0)
            {
                yield return [.. current];
                yield break;
            }

            used[first] = true;
            for (var j = first + 1; j < n; j++)
            {
                if (used[j])
                    continue;
                used[j] = true;
                current.Add((first, j));
                foreach (var m in Recurse())
                    yield return m;
                current.RemoveAt(current.Count - 1);
                used[j] = false;
            }

            used[first] = false;
        }
    }

    /// <summary>不限基數的最大權匹配（每個點可不配對）。</summary>
    public static Int128 MaxWeightMatching(int n, IReadOnlyList<WeightedEdge> edges)
    {
        var adj = new Dictionary<(int, int), Int128>();
        foreach (var e in edges)
            adj[(Math.Min(e.U, e.V), Math.Max(e.U, e.V))] = e.Weight;
        var memo = new Dictionary<int, Int128>();
        return Best(0);

        Int128 Best(int mask)
        {
            if (memo.TryGetValue(mask, out var cached))
                return cached;
            var first = -1;
            for (var i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
                return 0;
            var best = Best(mask | (1 << first)); // first 不配對
            for (var j = first + 1; j < n; j++)
                if ((mask & (1 << j)) == 0 && adj.TryGetValue((first, j), out var w))
                    best = Int128.Max(best, w + Best(mask | (1 << first) | (1 << j)));
            memo[mask] = best;
            return best;
        }
    }
}
