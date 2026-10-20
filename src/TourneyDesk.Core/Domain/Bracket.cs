using System.Collections.Immutable;

namespace TourneyDesk.Core.Domain;

public enum KnockoutSourceKind
{
    /// <summary>籤表位置上的種子（種子編號 &gt; 人數者為空位 → 輪空）。</summary>
    Seed,

    /// <summary>某場的勝者。</summary>
    Winner,

    /// <summary>某場的敗者（季軍戰）。</summary>
    Loser,
}

/// <param name="Value">Seed：種子編號（1 起算）；Winner／Loser：場次 Id。</param>
public readonly record struct KnockoutSource(KnockoutSourceKind Kind, int Value)
{
    public static KnockoutSource Seed(int seed) => new(KnockoutSourceKind.Seed, seed);

    public static KnockoutSource WinnerOf(int matchId) => new(KnockoutSourceKind.Winner, matchId);

    public static KnockoutSource LoserOf(int matchId) => new(KnockoutSourceKind.Loser, matchId);

    public bool DependsOn(int matchId) => Kind != KnockoutSourceKind.Seed && Value == matchId;
}

public enum KnockoutOutcomeKind
{
    /// <summary>實際比賽決定。</summary>
    Played,

    /// <summary>對手退賽，晉級（系統自動）。</summary>
    Walkover,

    /// <summary>對手位置為空，輪空晉級（系統自動，只出現在第 1 輪）。</summary>
    Bye,
}

public sealed record KnockoutOutcome(int Winner, KnockoutOutcomeKind Kind);

/// <param name="Round">輪次（1 = 第一輪）；季軍戰與決賽同輪。</param>
/// <param name="Position">該輪由上到下的位置（0 起算）。</param>
public sealed record KnockoutMatch(
    int Id,
    int Round,
    int Position,
    KnockoutSource A,
    KnockoutSource B,
    KnockoutOutcome? Outcome = null,
    bool IsThirdPlace = false);

/// <summary>單淘汰籤表。</summary>
/// <param name="Size">籤表大小 S（2 的冪次，S ≥ N）。</param>
/// <param name="Seeds">依種子順序的選手 Id（索引 0 = 種子 1），長度 N。</param>
public sealed record Bracket(int Size, ImmutableArray<int> Seeds, ImmutableArray<KnockoutMatch> Matches)
{
    public int RoundCount => System.Numerics.BitOperations.Log2((uint)Size);

    public KnockoutMatch GetMatch(int id) =>
        id >= 1 && id <= Matches.Length ? Matches[id - 1] : throw new DomainException($"找不到第 {id} 場。");

    public KnockoutMatch Final => Matches.Single(m => m.Round == RoundCount && !m.IsThirdPlace);

    public KnockoutMatch? ThirdPlace => Matches.SingleOrDefault(m => m.IsThirdPlace);

    public bool Contains(int playerId) => Seeds.Contains(playerId);

    public int SeedOf(int playerId) => Seeds.IndexOf(playerId) + 1;

    public Participant Resolve(KnockoutSource source)
    {
        switch (source.Kind)
        {
            case KnockoutSourceKind.Seed:
                return source.Value <= Seeds.Length ? Participant.Of(Seeds[source.Value - 1]) : Participant.Empty;
            case KnockoutSourceKind.Winner:
                return GetMatch(source.Value).Outcome is { } o ? Participant.Of(o.Winner) : Participant.Pending;
            case KnockoutSourceKind.Loser:
                var m = GetMatch(source.Value);
                if (m.Outcome is null)
                    return Participant.Pending;
                if (m.Outcome.Kind == KnockoutOutcomeKind.Bye)
                    return Participant.Empty;
                var (a, b) = (Resolve(m.A), Resolve(m.B));
                return Participant.Of(a.PlayerId == m.Outcome.Winner ? b.PlayerId!.Value : a.PlayerId!.Value);
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    public (Participant A, Participant B) Participants(KnockoutMatch m) => (Resolve(m.A), Resolve(m.B));
}

/// <summary>某一格的參賽者：已確定的選手、尚待前一場決定，或空位。</summary>
public readonly record struct Participant(int? PlayerId, bool IsEmpty)
{
    public static Participant Pending => new(null, false);

    public static Participant Empty => new(null, true);

    public static Participant Of(int playerId) => new(playerId, false);

    public bool IsKnown => PlayerId is not null;
}
