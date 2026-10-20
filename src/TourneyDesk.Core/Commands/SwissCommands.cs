using System.Collections.Immutable;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Swiss;

namespace TourneyDesk.Core.Commands;

internal static class SwissRules
{
    public static void RequireSwiss(TournamentState s)
    {
        if (s.Settings.Format != TournamentFormat.Swiss)
            throw new DomainException("此操作只適用於瑞士制。");
    }

    /// <summary>檢查可以產生第 <paramref name="round"/> 輪，並回傳該輪的配對輸入。</summary>
    public static SwissRoundInput RequireCanGenerate(TournamentState s, int round)
    {
        RequireSwiss(s);
        if (s.LastRound is { Status: RoundStatus.Draft } draft)
            throw new DomainException($"第 {draft.Number} 輪已有草稿，請先發布或作廢。");
        if (round != s.Rounds.Length + 1)
            throw new DomainException($"下一輪應為第 {s.Rounds.Length + 1} 輪。");
        if (round > s.Settings.SwissRounds)
            throw new DomainException($"已達總輪數 {s.Settings.SwissRounds} 輪。");
        if (s.LastRound is { IsCompleted: false } last)
            throw new DomainException($"第 {last.Number} 輪尚有未登錄的成績。");

        var input = SwissRoundInput.FromState(s, round);
        if (input.Count == 0)
            throw new DomainException("沒有可配對的選手。");
        return input;
    }

    public static Round DraftFrom(int number, Pairing proposal) =>
        new(number, RoundStatus.Draft, [.. proposal.Games.Select(g => new Game(g.White, g.Black))], proposal.Bye, proposal);
}

/// <summary>計算瑞士制下一輪的引擎建議（耗時，供背景執行；不改變狀態）。</summary>
public static class SwissRoundPlanner
{
    public static SwissPairingResult Propose(
        TournamentState state,
        int round,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        SwissPairingEngine.Pair(SwissRoundInput.FromState(state, round), progress, cancellationToken);

    /// <summary>以目前（未發布前）的配對與輸入評估方案。</summary>
    public static PairingEvaluation Evaluate(TournamentState state, Round round) =>
        SwissObjective.Evaluate(SwissRoundInput.FromState(state, round.Number), Pairing.Of(round));
}

/// <summary>以引擎建議建立第 k 輪草稿（建議由背景計算後傳入）。</summary>
public sealed record GenerateSwissRound(int RoundNumber, Pairing EngineProposal) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var input = SwissRules.RequireCanGenerate(state, RoundNumber);
        SwissObjective.Evaluate(input, EngineProposal); // 驗證涵蓋所有可配對選手
        var next = state.WithRound(SwissRules.DraftFrom(RoundNumber, EngineProposal));
        return new Transition(next, $"產生第 {RoundNumber} 輪配對",
            [context.Audit("產生配對", $"第 {RoundNumber} 輪", null, Describe.Pairing(state, EngineProposal))]);
    }
}

/// <summary>草稿中交換兩位選手的位置（可交換對局者與輪空者）。不同桌時兩桌的先後手依規則重新決定。</summary>
public sealed record SwapInDraft(int RoundNumber, int PlayerA, int PlayerB) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        SwissRules.RequireSwiss(state);
        var round = state.GetRound(RoundNumber);
        if (round.Status != RoundStatus.Draft)
            throw new DomainException("只有草稿可以手動交換。");
        if (PlayerA == PlayerB)
            throw new DomainException("請選擇兩位不同的選手。");
        if (!round.Involves(PlayerA) || !round.Involves(PlayerB))
            throw new DomainException("選手不在此輪配對中。");

        var games = round.Games.ToBuilder();
        int? bye = round.Bye;
        var ia = round.GameIndexOf(PlayerA);
        var ib = round.GameIndexOf(PlayerB);

        if (ia is { } same && ia == ib)
        {
            games[same] = new Game(games[same].Black, games[same].White); // 同桌：互換先後手
        }
        else
        {
            if (ia is { } a)
                games[a] = Replace(games[a], PlayerA, PlayerB);
            else
                bye = PlayerB;
            if (ib is { } b)
                games[b] = Replace(games[b], PlayerB, PlayerA);
            else
                bye = PlayerA;

            var input = SwissRoundInput.FromState(state, RoundNumber);
            foreach (var idx in new[] { ia, ib }.OfType<int>())
            {
                var colored = ColorAllocator.Assign(input, games[idx].White, games[idx].Black);
                games[idx] = new Game(colored.White, colored.Black);
            }
        }

        var after = round with { Games = games.ToImmutable(), Bye = bye };
        var before = Describe.Pairing(state, Pairing.Of(round));
        return new Transition(state.WithRound(after),
            $"交換 {Describe.Player(state, PlayerA)} 與 {Describe.Player(state, PlayerB)}",
            [context.Audit("手動交換", $"第 {RoundNumber} 輪草稿：{Describe.Player(state, PlayerA)} ↔ {Describe.Player(state, PlayerB)}",
                before, Describe.Pairing(state, Pairing.Of(after)))]);
    }

    private static Game Replace(Game g, int from, int to) =>
        g.White == from ? g with { White = to } : g with { Black = to };
}

/// <summary>發布草稿。含重複對戰或重複輪空時，必須 <paramref name="Force"/> 才能採用，並寫入異動紀錄。</summary>
public sealed record PublishRound(int RoundNumber, bool Force = false) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        SwissRules.RequireSwiss(state);
        var round = state.GetRound(RoundNumber);
        if (round.Status != RoundStatus.Draft)
            throw new DomainException($"第 {RoundNumber} 輪不是草稿。");

        var evaluation = SwissRoundPlanner.Evaluate(state, round);
        var violations = evaluation.Violations.Select(v => ViolationText(state, v)).ToList();
        if (violations.Count > 0 && !Force)
            throw new DomainException($"配對含違規：{string.Join("、", violations)}。如確定要採用，請勾選「強制採用」。");

        var published = round with { Status = RoundStatus.Published, ForcedOverride = violations.Count > 0 };
        var audit = new List<AuditEntry>();
        var current = Pairing.Of(round);
        if (round.EngineProposal is { } proposal && !proposal.SameAs(current))
        {
            audit.Add(context.Audit(violations.Count > 0 ? "強制採用" : "採用手動調整", $"第 {RoundNumber} 輪",
                Describe.Pairing(state, proposal),
                Describe.Pairing(state, current) + (violations.Count > 0 ? $"（違規：{string.Join("、", violations)}）" : "")));
        }

        audit.Add(context.Audit("發布配對", $"第 {RoundNumber} 輪", null, Describe.Pairing(state, current)));
        return new Transition(state.WithRound(published), $"發布第 {RoundNumber} 輪配對", audit);
    }

    internal static string ViolationText(TournamentState s, PairingViolation v) => v.Kind switch
    {
        ViolationKind.RepeatPairing => $"重複對戰 {Describe.Player(s, v.PlayerA)} vs {Describe.Player(s, v.PlayerB!.Value)}",
        ViolationKind.RepeatBye => $"重複輪空 {Describe.Player(s, v.PlayerA)}",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };
}

/// <summary>作廢最後一輪的草稿。</summary>
public sealed record DiscardDraft(int RoundNumber) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var round = state.GetRound(RoundNumber);
        if (round.Status != RoundStatus.Draft)
            throw new DomainException($"第 {RoundNumber} 輪不是草稿。");
        return new Transition(state.TruncateRounds(RoundNumber), $"作廢第 {RoundNumber} 輪草稿",
            [context.Audit("作廢草稿", $"第 {RoundNumber} 輪", Describe.Pairing(state, Pairing.Of(round)), null)]);
    }
}

/// <summary>「從第 k 輪起重新配對」：清除第 k 輪起的配對與成績，並以新的引擎建議建立第 k 輪草稿。</summary>
public sealed record RepairFromRound(int RoundNumber, Pairing NewProposal, string ConfirmedImpactFingerprint) : ITournamentCommand
{
    /// <summary>清除前列給使用者確認的影響範圍。</summary>
    public static ClearImpact Preview(TournamentState state, int fromRound)
    {
        SwissRules.RequireSwiss(state);
        if (fromRound < 1 || fromRound > state.Rounds.Length)
            throw new DomainException($"第 {fromRound} 輪不存在。");

        var items = ImmutableArray.CreateBuilder<string>();
        foreach (var r in state.Rounds.Where(r => r.Number >= fromRound))
        {
            var results = r.Games.Count(g => g.Result.HasResult());
            items.Add($"第 {r.Number} 輪：{(r.IsPublished ? "已發布" : "草稿")}，{r.Games.Length} 場配對、{results} 筆成績將被清除");
            for (var i = 0; i < r.Games.Length; i++)
                if (r.Games[i].Result.HasResult())
                    items.Add($"{Describe.Board(state, r.Number, i + 1)} {Describe.GameWithResult(state, r.Games[i])}");
        }

        return new ClearImpact(items.ToImmutable());
    }

    public Transition Apply(TournamentState state, CommandContext context)
    {
        var impact = Preview(state, RoundNumber);
        if (impact.Fingerprint != ConfirmedImpactFingerprint)
            throw new DomainException("影響範圍已變更，請重新檢視並確認。");

        var truncated = state.TruncateRounds(RoundNumber);
        var input = SwissRules.RequireCanGenerate(truncated, RoundNumber);
        SwissObjective.Evaluate(input, NewProposal);
        var next = truncated.WithRound(SwissRules.DraftFrom(RoundNumber, NewProposal));
        return new Transition(next, $"從第 {RoundNumber} 輪起重新配對",
        [
            context.Audit("重新配對", $"自第 {RoundNumber} 輪起", impact.Summary(), Describe.Pairing(state, NewProposal)),
        ]);
    }
}
