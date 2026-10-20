using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.Reference;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Swiss;

public class SwissPropertyTests
{
    private static (List<(int, int)> Games, int? Bye) ByRegNo(TournamentState state, Pairing pairing) =>
        ([.. pairing.Games.Select(g => (state.Players[g.White].RegNo, state.Players[g.Black].RegNo)).OrderBy(x => x)],
         pairing.Bye is { } b ? state.Players[b].RegNo : null);

    /// <summary>驗收 3：N ≤ 12 與窮舉參考解逐案一致（含先後手），隨機限制分層數以驗證分段固定。</summary>
    [Fact]
    public void Pairing_matches_brute_force_reference_for_up_to_12_players()
    {
        Property.ForAll(seed: 3, cases: 600, (rng, _) =>
        {
            var (state, round) = SwissHistoryGenerator.Generate(rng, rng.Next(1, 13));
            int? maxLayers = rng.Next(4) switch { 0 => 1, 1 => 2, 2 => 3, _ => null };

            var engine = SwissPairingEngine.Pair(SwissRoundInput.FromState(state, round), maxLayers: maxLayers);
            var reference = BruteForceSwissPairing.Solve(ReferenceRoundData.From(state, round));

            var (games, bye) = ByRegNo(state, engine.Pairing);
            Assert.Equal(reference.ByeRegNo, bye);
            Assert.Equal(reference.Games.OrderBy(x => x).ToList(), games);
        });
    }

    /// <summary>驗收 4：N ≤ 18 的最佳化值 ①～④ 與位元遮罩 DP 參考解一致。</summary>
    [Fact]
    public void Optimum_values_match_bitmask_dp_reference_for_up_to_18_players()
    {
        Property.ForAll(seed: 4, cases: 220, (rng, _) =>
        {
            var (state, round) = SwissHistoryGenerator.Generate(rng, rng.Next(1, 19));

            var engine = SwissPairingEngine.Pair(SwissRoundInput.FromState(state, round)).Evaluation.Objective;
            var dp = BitmaskDpSwissPairing.Solve(ReferenceRoundData.From(state, round));

            Assert.Equal(
                (dp.Repeats, dp.ByeRepeat, dp.ByeRank, dp.ScoreDiffSquares),
                (engine.Repeats, engine.ByeRepeat, engine.ByeRank, engine.ScoreDiffSquares));
        });
    }

    /// <summary>驗收 5：打亂選手輸入順序、重新指派內部 Id，配對結果（以註冊編號表示）完全相同。</summary>
    [Fact]
    public void Pairing_is_independent_of_input_order_and_internal_ids()
    {
        Property.ForAll(seed: 5, cases: 220, (rng, _) =>
        {
            var (state, round) = SwissHistoryGenerator.Generate(rng, rng.Next(1, 41), rng.Next(0, 8));
            var remapped = SwissHistoryGenerator.RemapIds(state, rng);

            var input = SwissRoundInput.FromState(state, round);
            var shuffledInput = SwissRoundInput.FromState(remapped, round);
            var shuffled = new SwissRoundInput(
                shuffledInput.Ranked.OrderBy(_ => rng.Next()),
                remapped.Rounds.SelectMany(r => r.Games).Select(g => (g.White, g.Black)).OrderBy(_ => rng.Next()));

            var a = ByRegNo(state, SwissPairingEngine.Pair(input).Pairing);
            var b = ByRegNo(remapped, SwissPairingEngine.Pair(shuffled).Pairing);

            Assert.Equal(a.Bye, b.Bye);
            Assert.Equal(a.Games, b.Games);
        });
    }

    /// <summary>引擎結果的評估值不劣於任何隨機合法方案（大 N 時的健全性檢查）。</summary>
    [Fact]
    public void Engine_result_is_never_worse_than_random_alternatives()
    {
        Property.ForAll(seed: 6, cases: 60, (rng, _) =>
        {
            var (state, round) = SwissHistoryGenerator.Generate(rng, rng.Next(20, 61), rng.Next(1, 8));
            var input = SwissRoundInput.FromState(state, round);
            var best = SwissPairingEngine.Pair(input).Evaluation.Objective;

            for (var t = 0; t < 30; t++)
            {
                var ids = input.Ranked.Select(p => p.Id).OrderBy(_ => rng.Next()).ToList();
                int? bye = null;
                if (ids.Count % 2 == 1)
                {
                    bye = ids[^1];
                    ids.RemoveAt(ids.Count - 1);
                }

                var games = Enumerable.Range(0, ids.Count / 2).Select(i => new PairedGame(ids[2 * i], ids[2 * i + 1]));
                var alt = SwissObjective.Evaluate(input, new Pairing([.. games], bye)).Objective;
                Assert.True(best.CompareTo(alt) <= 0);
            }
        });
    }
}
