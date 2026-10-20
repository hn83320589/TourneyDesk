using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Swiss;

/// <summary>
/// 「過期」判定：對每個由引擎產生且已發布的輪次 k，以「第 k 輪之前的現有成績 + 目前的參賽者」重新計算引擎建議，
/// 與發布當時儲存的引擎建議比較（含先後手）。不同即為過期。
/// 比較的是「建議 vs 建議」而非「建議 vs 已發布」，因此曾手動交換的輪次不會被誤判。
/// 此判定只讀取狀態，絕不改動已發布的配對。
/// </summary>
public static class SwissStaleness
{
    public static ImmutableArray<int> FindStaleRounds(
        TournamentState state,
        int fromRound = 1,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (state.Settings.Format != TournamentFormat.Swiss)
            return [];

        var candidates = state.Rounds
            .Where(r => r.Number >= fromRound && r.IsPublished && r.EngineProposal is not null)
            .ToList();
        var stale = ImmutableArray.CreateBuilder<int>();
        progress?.Report(0);
        for (var i = 0; i < candidates.Count; i++)
        {
            var round = candidates[i];
            var input = SwissRoundInput.FromState(state, round.Number);
            if (!SameParticipants(input, round.EngineProposal!) ||
                !SwissPairingEngine.Pair(input, null, cancellationToken).Pairing.SameAs(round.EngineProposal!))
            {
                stale.Add(round.Number);
            }

            progress?.Report((i + 1) * 100 / candidates.Count);
        }

        progress?.Report(100);
        return stale.ToImmutable();
    }

    private static bool SameParticipants(SwissRoundInput input, Pairing proposal)
    {
        var ids = proposal.Games.SelectMany(g => new[] { g.White, g.Black }).ToHashSet();
        if (proposal.Bye is { } bye)
            ids.Add(bye);
        return ids.SetEquals(input.Ranked.Select(p => p.Id));
    }
}
