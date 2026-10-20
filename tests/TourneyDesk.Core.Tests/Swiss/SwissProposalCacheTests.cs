using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Swiss;

/// <summary>過期判定以「各輪輸入指紋」快取引擎建議，只重算輸入有變的輪次。</summary>
public class SwissProposalCacheTests
{
    private const GameResult W = GameResult.WhiteWin;

    /// <summary>6 人、第 1～3 輪已完成、第 4 輪已發布未登錄。</summary>
    private static TournamentState FourRounds() =>
        SwissFlow.NewSwiss(6).NextRound().Results(W, W, W).NextRound().Results(W, W, W).NextRound().Results(W, W, W).NextRound();

    [Fact]
    public void Fingerprint_is_equal_for_equal_inputs_and_changes_with_any_relevant_field()
    {
        var s = FourRounds();
        var a = SwissRoundInput.FromState(s, 3).Fingerprint;

        Assert.Equal(a, SwissRoundInput.FromState(s, 3).Fingerprint);
        Assert.NotEqual(a, SwissRoundInput.FromState(s.Then(new SetResult(2, 1, GameResult.Draw)), 3).Fingerprint); // 分數
        Assert.NotEqual(a, SwissRoundInput.FromState(s.Then(new EditPlayer(1, "A", 1)), 3).Fingerprint);             // 積分 → 排名
        Assert.NotEqual(a, SwissRoundInput.FromState(s, 2).Fingerprint);
    }

    [Fact]
    public void Second_check_hits_cache_for_every_round()
    {
        var s = FourRounds();
        var cache = new SwissProposalCache();

        SwissStaleness.FindStaleRounds(s, cache: cache);
        var misses = cache.Misses;
        SwissStaleness.FindStaleRounds(s, cache: cache);

        Assert.Equal(4, misses); // 第 1～4 輪
        Assert.Equal(misses, cache.Misses);
        Assert.Equal(4, cache.Hits);
    }

    [Fact]
    public void Entering_results_in_the_latest_round_recomputes_nothing()
    {
        var s = FourRounds();
        var cache = new SwissProposalCache();
        SwissStaleness.FindStaleRounds(s, cache: cache);
        var misses = cache.Misses;

        SwissStaleness.FindStaleRounds(s.Then(new SetResult(4, 1, W)), cache: cache);

        Assert.Equal(misses, cache.Misses);
    }

    [Fact]
    public void Correction_recomputes_only_rounds_whose_input_changed()
    {
        var s = FourRounds();
        var cache = new SwissProposalCache();
        SwissStaleness.FindStaleRounds(s, cache: cache);
        var misses = cache.Misses;

        SwissStaleness.FindStaleRounds(s.Then(new SetResult(3, 1, GameResult.Draw)), cache: cache); // 只影響第 4 輪的輸入

        Assert.Equal(misses + 1, cache.Misses);
    }

    [Fact]
    public void Cached_and_uncached_results_are_identical()
    {
        Property.ForAll(seed: 33, cases: 40, (rng, _) =>
        {
            var s = SwissFlow.NewSwiss(rng.Next(3, 11), rounds: 6);
            for (var r = 0; r < rng.Next(2, 6); r++)
            {
                s = s.Draft();
                s = s.Then(new PublishRound(s.Rounds.Length, Force: true)); // 人少輪多時重複對戰無法避免
                for (var b = 1; b <= s.Rounds[^1].Games.Length; b++)
                    s = s.Then(new SetResult(s.Rounds.Length, b, SwissHistoryGenerator.RandomResult(rng)));
            }

            var cache = new SwissProposalCache();
            for (var step = 0; step < 4; step++)
            {
                var round = rng.Next(1, s.Rounds.Length + 1);
                var board = rng.Next(1, s.GetRound(round).Games.Length + 1);
                var result = SwissHistoryGenerator.RandomResult(rng);
                if (s.GetRound(round).Games[board - 1].Result != result)
                    s = s.Then(new SetResult(round, board, result));

                Assert.Equal(SwissStaleness.FindStaleRounds(s).ToArray(), SwissStaleness.FindStaleRounds(s, cache: cache).ToArray());
            }
        });
    }

    [Fact]
    public void Cache_is_bounded()
    {
        var cache = new SwissProposalCache(capacity: 2);
        var s = FourRounds();

        SwissStaleness.FindStaleRounds(s, cache: cache);

        Assert.True(cache.Count <= 2);
    }
}
