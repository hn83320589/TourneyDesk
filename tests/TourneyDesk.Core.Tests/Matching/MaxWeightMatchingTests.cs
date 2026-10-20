using TourneyDesk.Core.Matching;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Matching;

public class MaxWeightMatchingTests
{
    [Fact]
    public void Empty_graph_has_empty_matching()
    {
        var result = MaxWeightMatching.Solve(0, [], maxCardinality: true);

        Assert.Empty(result.Mate);
    }

    [Fact]
    public void Single_edge_is_matched()
    {
        var result = MaxWeightMatching.Solve(2, [new WeightedEdge(0, 1, 10)], maxCardinality: false);

        Assert.Equal([1, 0], result.Mate);
    }

    [Fact]
    public void Prefers_heavier_pair_of_edges_over_single_heavy_edge()
    {
        // 路徑 0-1-2-3：中間邊 6，兩側各 4 → 兩側合計 8 較大
        var edges = new[] { new WeightedEdge(0, 1, 4), new WeightedEdge(1, 2, 6), new WeightedEdge(2, 3, 4) };

        var result = MaxWeightMatching.Solve(4, edges, maxCardinality: false);

        Assert.Equal([1, 0, 3, 2], result.Mate);
    }

    [Fact]
    public void Max_cardinality_forces_perfect_matching_even_at_lower_weight()
    {
        // 0-1 權重 100；0-2、1-3 各 2。最大權（不限基數）只選 0-1；最大基數須選 0-2、1-3
        var edges = new[] { new WeightedEdge(0, 1, 100), new WeightedEdge(0, 2, 2), new WeightedEdge(1, 3, 2) };

        Assert.Equal([1, 0, -1, -1], MaxWeightMatching.Solve(4, edges, maxCardinality: false).Mate);
        Assert.Equal([2, 3, 0, 1], MaxWeightMatching.Solve(4, edges, maxCardinality: true).Mate);
    }

    [Fact]
    public void Handles_odd_cycle_blossom()
    {
        // 三角形 0-1-2 加上懸掛邊 2-3：需要收縮 blossom 才能找到 0-1、2-3
        var edges = new[]
        {
            new WeightedEdge(0, 1, 8), new WeightedEdge(1, 2, 10), new WeightedEdge(0, 2, 10), new WeightedEdge(2, 3, 6),
        };

        var result = MaxWeightMatching.Solve(4, edges, maxCardinality: true);

        Assert.Equal([1, 0, 3, 2], result.Mate);
    }

    [Fact]
    public void Odd_weights_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => MaxWeightMatching.Solve(2, [new WeightedEdge(0, 1, 3)], true));
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            MaxWeightMatching.Solve(2, [new WeightedEdge(0, 1, 2)], true, cts.Token));
    }

    /// <summary>性質測試：隨機完全圖上，最大權完美匹配的權重與暴力列舉一致，且所有最佳匹配只用緊邊。</summary>
    [Fact]
    public void Perfect_matching_weight_matches_brute_force_on_random_complete_graphs()
    {
        Property.ForAll(seed: 20261020, cases: 400, (rng, _) =>
        {
            var n = 2 * rng.Next(1, 6); // 2..10
            var maxW = rng.Next(1, 4) switch { 1 => 3, 2 => 20, _ => 1000 }; // 小範圍權重 → 大量平手、blossom
            var edges = new List<WeightedEdge>();
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                    edges.Add(new WeightedEdge(i, j, 2 * rng.Next(0, maxW)));

            var result = MaxWeightMatching.Solve(n, edges, maxCardinality: true);

            var weight = new Dictionary<(int, int), Int128>();
            for (var k = 0; k < edges.Count; k++)
                weight[(edges[k].U, edges[k].V)] = edges[k].Weight;
            Int128 W(int a, int b) => weight[a < b ? (a, b) : (b, a)];

            var all = BruteForce.PerfectMatchings(n).ToList();
            var best = all.Max(m => m.Aggregate(Int128.Zero, (s, e) => s + W(e.A, e.B)));
            Assert.All(result.Mate, m => Assert.NotEqual(-1, m));
            var got = Int128.Zero;
            for (var v = 0; v < n; v++)
                if (v < result.Mate[v])
                    got += W(v, result.Mate[v]);
            Assert.Equal(best, got);

            var tight = new HashSet<(int, int)>();
            for (var k = 0; k < edges.Count; k++)
                if (result.Tight[k])
                    tight.Add((edges[k].U, edges[k].V));
            foreach (var m in all.Where(m => m.Aggregate(Int128.Zero, (s, e) => s + W(e.A, e.B)) == best))
                Assert.All(m, e => Assert.Contains((Math.Min(e.A, e.B), Math.Max(e.A, e.B)), tight));
        });
    }

    /// <summary>性質測試：隨機稀疏圖（不限基數）的最大權匹配與暴力列舉一致。</summary>
    [Fact]
    public void Max_weight_matching_matches_brute_force_on_random_sparse_graphs()
    {
        Property.ForAll(seed: 7, cases: 300, (rng, _) =>
        {
            var n = rng.Next(1, 10);
            var edges = new List<WeightedEdge>();
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                    if (rng.Next(3) == 0)
                        edges.Add(new WeightedEdge(i, j, 2 * rng.Next(1, 30)));

            var result = MaxWeightMatching.Solve(n, edges, maxCardinality: false);

            var got = Int128.Zero;
            foreach (var e in edges)
                if (result.Mate[e.U] == e.V)
                    got += e.Weight;
            Assert.Equal(BruteForce.MaxWeightMatching(n, edges), got);
        });
    }
}
