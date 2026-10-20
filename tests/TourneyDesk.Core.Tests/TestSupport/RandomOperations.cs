using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>依目前狀態隨機挑選一個（大多合法的）操作；涵蓋所有操作種類。</summary>
internal static class RandomOperations
{
    public static ITournamentCommand Next(TournamentState s, Random rng)
    {
        var options = new List<Func<ITournamentCommand?>>
        {
            () => new AddPlayer($"N{rng.Next(1000)}", rng.Next(0, 3001)),
            () => AnyPlayer(s, rng) is { } p ? new EditPlayer(p.Id, p.Name + "'", rng.Next(0, 3001)) : null,
            () => AnyPlayer(s, rng) is { } p ? new DeletePlayer(p.Id) : null,
            () => new ImportPlayers([.. Enumerable.Range(0, rng.Next(1, 4))
                .Select(i => new TourneyDesk.Core.Import.ImportedPlayer(i + 2, null, $"匯{rng.Next(1000)}", rng.Next(0, 3001)))]),
        };

        switch (s.Settings.Format)
        {
            case TournamentFormat.Swiss:
                options.AddRange(SwissOptions(s, rng));
                break;
            case TournamentFormat.SingleElimination:
                options.AddRange(KnockoutOptions(s, rng));
                break;
            default:
                options.Add(() => new StartRoundRobin());
                options.Add(() => RandomResult(s, rng));
                options.Add(() => AnyPlayer(s, rng) is { } p ? new WithdrawPlayer(p.Id, rng.Next(1, Math.Max(2, s.Rounds.Length + 1))) : null);
                break;
        }

        for (var attempt = 0; attempt < 20; attempt++)
            if (options[rng.Next(options.Count)]() is { } cmd)
                return cmd;
        return new AddPlayer("後備", 1000);
    }

    private static IEnumerable<Func<ITournamentCommand?>> SwissOptions(TournamentState s, Random rng)
    {
        var next = s.Rounds.Length + 1;
        yield return () => s.Players.Count >= 1 && next <= s.Settings.SwissRounds
            ? new GenerateSwissRound(next, SafePropose(s, next))
            : null;
        yield return () => s.Players.Count >= 1 && next <= s.Settings.SwissRounds
            ? new GenerateSwissRound(next, SafePropose(s, next))
            : null;
        yield return () => s.LastRound is { Status: RoundStatus.Draft } d && AnyPlayer(s, rng) is { } a && AnyPlayer(s, rng) is { } b
            ? new SwapInDraft(d.Number, a.Id, b.Id)
            : null;
        yield return () => s.LastRound is { Status: RoundStatus.Draft } d ? new PublishRound(d.Number, rng.Next(2) == 0) : null;
        yield return () => s.LastRound is { Status: RoundStatus.Draft } d ? new DiscardDraft(d.Number) : null;
        yield return () => RandomResult(s, rng);
        yield return () => RandomResult(s, rng);
        yield return () => AnyPlayer(s, rng) is { } p ? new WithdrawPlayer(p.Id, rng.Next(1, next + 1)) : null;
        yield return () => new LateJoinPlayer($"L{rng.Next(100)}", rng.Next(0, 3001), Math.Max(2, s.Rounds.Count(r => r.IsPublished) + 1));
        yield return () =>
        {
            if (s.Rounds.Length < 2)
                return null;
            var k = rng.Next(2, s.Rounds.Length + 1);
            return new RepairFromRound(k, SafePropose(s, k), RepairFromRound.Preview(s, k).Fingerprint);
        };
    }

    private static IEnumerable<Func<ITournamentCommand?>> KnockoutOptions(TournamentState s, Random rng)
    {
        yield return () => new StartKnockout();
        yield return () =>
        {
            if (s.Bracket is not { } b)
                return null;
            var open = b.Matches.Where(m => m.Outcome is null && b.Participants(m) is { A.IsKnown: true, B.IsKnown: true }).ToList();
            if (open.Count == 0)
                return null;
            var m = open[rng.Next(open.Count)];
            var (pa, pb) = b.Participants(m);
            return new RecordKnockoutWinner(m.Id, rng.Next(2) == 0 ? pa.PlayerId!.Value : pb.PlayerId!.Value);
        };
        yield return () =>
        {
            if (s.Bracket is not { } b)
                return null;
            var decided = b.Matches.Where(m => m.Outcome is { Kind: KnockoutOutcomeKind.Played }).ToList();
            if (decided.Count == 0)
                return null;
            var m = decided[rng.Next(decided.Count)];
            var (pa, pb) = b.Participants(m);
            var other = m.Outcome!.Winner == pa.PlayerId ? pb.PlayerId!.Value : pa.PlayerId!.Value;
            return new CorrectKnockoutWinner(m.Id, other, KnockoutRules.CascadeImpact(s, m.Id).Fingerprint);
        };
        yield return () => AnyPlayer(s, rng) is { } p ? new WithdrawFromKnockout(p.Id) : null;
    }

    private static ITournamentCommand? RandomResult(TournamentState s, Random rng)
    {
        var published = s.Rounds.Where(r => r.IsPublished && r.Games.Length > 0).ToList();
        if (published.Count == 0)
            return null;
        var r = published[rng.Next(published.Count)];
        return new SetResult(r.Number, rng.Next(1, r.Games.Length + 1), SwissHistoryGenerator.RandomResult(rng));
    }

    private static Pairing SafePropose(TournamentState s, int round)
    {
        // 狀態不允許產生時仍回傳一個方案，讓命令本身去拒絕（測試失敗路徑也不得改變狀態）
        var input = TourneyDesk.Core.Swiss.SwissRoundInput.FromState(s, round);
        return input.Count == 0 ? new Pairing([], null) : TourneyDesk.Core.Swiss.SwissPairingEngine.Pair(input).Pairing;
    }

    private static Player? AnyPlayer(TournamentState s, Random rng) =>
        s.Players.Count == 0 ? null : s.Players.Values.ElementAt(rng.Next(s.Players.Count));
}
