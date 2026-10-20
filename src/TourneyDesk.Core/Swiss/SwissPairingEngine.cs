using System.Collections.Immutable;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Matching;

namespace TourneyDesk.Core.Swiss;

public sealed record SwissPairingResult(Pairing Pairing, PairingEvaluation Evaluation);

/// <summary>
/// 瑞士制配對引擎：求字典序 ①～⑤ 的唯一最佳方案。
/// <para>
/// 作法：把本輪選手依賽前排名編號（奇數人數時加一個虛擬點 D，配到 D 即輪空），轉成完全圖上的最大權完美匹配。
/// ①～④ 可逐邊相加，合成一個整數「主要成本」P。
/// </para>
/// <para>
/// 第一階段只用 P 求解，得到最佳解與對偶解：輪空者由 ③ 唯一決定，直接固定；
/// 所有 P 最佳的方案只會使用「緊邊」，後續只在緊邊子圖上求解。
/// </para>
/// <para>
/// 第二階段處理 ⑤。⑤ 等價於「反覆讓目前排名最小的未配對者，對手排名越大越好」。
/// 以分層權重一次精確處理 k 個最小未配對者（第 t 層權重 B^(k−1−t) × 對手位置），
/// 固定這 k 人作為 hi 的配對後，對剩下的人重複，直到全部固定。每一段都仍以 P 為最高優先，所以固定的配對都屬於全域最佳解。
/// </para>
/// 最佳方案在 ①～⑤ 下唯一，因此結果與輸入順序、內部 Id 無關（決定性）。
/// </summary>
public static class SwissPairingEngine
{
    /// <summary>單邊權重上限的位元數（保留給 <see cref="Int128"/> 對偶變數運算的餘裕）。</summary>
    private const int WeightBitBudget = 100;

    /// <param name="maxLayers">測試用：限制每段處理的層數，以驗證分段固定的正確性。</param>
    public static SwissPairingResult Pair(
        SwissRoundInput input,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        int? maxLayers = null)
    {
        var n = input.Count;
        var costs = new PrimaryCosts(input);
        progress?.Report(0);

        var mate = Enumerable.Repeat(-1, n).ToArray();
        int? byeIndex = null;

        if (n > 0)
        {
            var (tight, bye) = SolvePrimary(costs, cancellationToken);
            byeIndex = bye;
            var remaining = Enumerable.Range(0, n).Where(i => i != bye).ToList();
            var fixedCount = n - remaining.Count;
            progress?.Report(Percent(fixedCount, n));

            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var newlyFixed = SolveLayered(costs, tight, remaining, maxLayers, cancellationToken);
                foreach (var (hi, lo) in newlyFixed)
                {
                    mate[hi] = lo;
                    mate[lo] = hi;
                }

                var done = newlyFixed.SelectMany(p => new[] { p.Hi, p.Lo }).ToHashSet();
                remaining.RemoveAll(done.Contains);
                fixedCount += done.Count;
                progress?.Report(Percent(fixedCount, n));
            }
        }

        var games = ImmutableArray.CreateBuilder<PairedGame>(n / 2);
        for (var i = 0; i < n; i++)
            if (mate[i] > i)
                games.Add(ColorAllocator.Assign(input, input.Ranked[i].Id, input.Ranked[mate[i]].Id));

        var pairing = new Pairing(games.ToImmutable(), byeIndex is { } b ? input.Ranked[b].Id : null);
        cancellationToken.ThrowIfCancellationRequested(); // 回報完成前取消，一律不交出結果
        progress?.Report(100);
        return new SwissPairingResult(pairing, SwissObjective.Evaluate(input, pairing));
    }

    private static int Percent(int done, int total) => total == 0 ? 100 : done * 100 / total;

    /// <summary>第一階段：只用主要成本求最佳解，回傳緊邊矩陣與輪空者（排名索引）。</summary>
    private static (bool[,] Tight, int? Bye) SolvePrimary(PrimaryCosts costs, CancellationToken ct)
    {
        var n = costs.N;
        var vertexCount = costs.HasBye ? n + 1 : n;
        var dummy = n;
        var edges = new List<WeightedEdge>(vertexCount * (vertexCount - 1) / 2);
        for (var a = 0; a < n; a++)
        {
            for (var b = a + 1; b < n; b++)
                edges.Add(new WeightedEdge(a, b, 2 * (Int128)(costs.Max - costs.Pair(a, b))));
            if (costs.HasBye)
                edges.Add(new WeightedEdge(a, dummy, 2 * (Int128)(costs.Max - costs.Bye(a))));
        }

        var solution = MaxWeightMatching.Solve(vertexCount, edges, maxCardinality: true, ct);
        EnsurePerfect(solution.Mate);

        var tight = new bool[n, n];
        for (var k = 0; k < edges.Count; k++)
        {
            var e = edges[k];
            if (solution.Tight[k] && e.V < n)
                tight[e.U, e.V] = tight[e.V, e.U] = true;
        }

        return (tight, costs.HasBye ? solution.Mate[dummy] : null);
    }

    /// <summary>第二階段的一段：在緊邊子圖上，以分層權重決定前 k 個最小未配對者（作為 hi）的對手。</summary>
    private static List<(int Hi, int Lo)> SolveLayered(
        PrimaryCosts costs,
        bool[,] tight,
        List<int> remaining,
        int? maxLayers,
        CancellationToken ct)
    {
        var m = remaining.Count; // remaining 依排名由小到大
        Int128 bas = m;
        var limit = Int128.One << WeightBitBudget;
        var primarySpan = (Int128)costs.Max + 1;
        var layers = 0;
        Int128 scale = 1;
        while (layers < m && (maxLayers is null || layers < maxLayers) && 2 * primarySpan * scale * bas < limit)
        {
            scale *= bas;
            layers++;
        }

        if (layers == 0)
            throw new InvalidOperationException("內部錯誤：權重位元不足以容納任何一層。");

        var levelWeight = new Int128[layers];
        levelWeight[layers - 1] = 1;
        for (var t = layers - 2; t >= 0; t--)
            levelWeight[t] = levelWeight[t + 1] * bas;

        var edges = new List<WeightedEdge>();
        for (var pa = 0; pa < m; pa++)
        {
            for (var pb = pa + 1; pb < m; pb++)
            {
                var a = remaining[pa];
                var b = remaining[pb];
                if (!tight[a, b])
                    continue;
                var lex = pa < layers ? levelWeight[pa] * pb : 0;
                var w = ((Int128)(costs.Max - costs.Pair(a, b)) * scale) + lex;
                edges.Add(new WeightedEdge(pa, pb, 2 * w));
            }
        }

        var mate = MaxWeightMatching.Solve(m, edges, maxCardinality: true, ct).Mate;
        EnsurePerfect(mate);

        var result = new List<(int Hi, int Lo)>();
        for (var pa = 0; pa < Math.Min(layers, m); pa++)
            if (mate[pa] > pa)
                result.Add((remaining[pa], remaining[mate[pa]]));
        return result;
    }

    private static void EnsurePerfect(int[] mate)
    {
        if (mate.Any(x => x == -1))
            throw new InvalidOperationException("內部錯誤：找不到完美匹配。");
    }

    /// <summary>
    /// 主要成本 P（越小越好）：把 ①～④ 合成一個整數，權值保證高位項嚴格支配所有低位項的總和。
    /// 一般邊 (a,b)：① × A + (分差)²；輪空邊 (a,D)：② × Bw + (n − 排名) × C。
    /// </summary>
    private sealed class PrimaryCosts
    {
        private readonly SwissRoundInput _input;
        private readonly long _repeatWeight;
        private readonly long _byeRepeatWeight;
        private readonly long _byeRankWeight;

        public PrimaryCosts(SwissRoundInput input)
        {
            _input = input;
            N = input.Count;
            HasBye = N % 2 == 1;
            var scores = input.Ranked.Select(p => (long)p.Score).DefaultIfEmpty(0).ToList();
            var maxDiff = scores.Max() - scores.Min();
            var maxSquaresTotal = maxDiff * maxDiff * (N / 2);
            _byeRankWeight = maxSquaresTotal + 1;
            _byeRepeatWeight = _byeRankWeight * Math.Max(N, 1);
            _repeatWeight = 2 * _byeRepeatWeight;

            long max = 0;
            for (var a = 0; a < N; a++)
            {
                for (var b = a + 1; b < N; b++)
                    max = Math.Max(max, Pair(a, b));
                if (HasBye)
                    max = Math.Max(max, Bye(a));
            }

            Max = max;
        }

        public int N { get; }

        public bool HasBye { get; }

        /// <summary>所有邊中最大的主要成本（用來把「最小化成本」轉為「最大化權重」）。</summary>
        public long Max { get; }

        public long Pair(int a, int b)
        {
            var pa = _input.Ranked[a];
            var pb = _input.Ranked[b];
            long diff = pa.Score - pb.Score;
            return (_input.HaveMet(pa.Id, pb.Id) ? _repeatWeight : 0) + (diff * diff);
        }

        public long Bye(int a) =>
            (_input.Ranked[a].HadBye ? _byeRepeatWeight : 0) + ((long)(N - 1 - a) * _byeRankWeight);
    }
}
