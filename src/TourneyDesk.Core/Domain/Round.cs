using System.Collections.Immutable;

namespace TourneyDesk.Core.Domain;

public enum RoundStatus
{
    /// <summary>草稿：可手動交換，尚不可登錄成績。</summary>
    Draft,

    /// <summary>已發布：配對固定，可登錄成績。</summary>
    Published,
}

/// <summary>一場對局。桌號 = 在輪次中的索引 + 1。</summary>
public sealed record Game(int White, int Black, GameResult Result = GameResult.None)
{
    public bool Involves(int playerId) => White == playerId || Black == playerId;

    public int OpponentOf(int playerId) =>
        playerId == White ? Black
        : playerId == Black ? White
        : throw new ArgumentException($"選手 {playerId} 不在此對局中。", nameof(playerId));

    public int PointsFor(int playerId) =>
        playerId == White ? Result.WhitePoints()
        : playerId == Black ? Result.BlackPoints()
        : throw new ArgumentException($"選手 {playerId} 不在此對局中。", nameof(playerId));
}

/// <summary>一場不含成績的配對（White = 先手）。</summary>
public readonly record struct PairedGame(int White, int Black);

/// <summary>不含成績的配對方案（引擎建議、手動調整比較用）。</summary>
public sealed record Pairing(ImmutableArray<PairedGame> Games, int? Bye)
{
    public static Pairing Of(Round round) =>
        new([.. round.Games.Select(g => new PairedGame(g.White, g.Black))], round.Bye);

    /// <summary>內容相等（ImmutableArray 的預設相等是參考相等，不適合比較方案）。</summary>
    public bool SameAs(Pairing other) => Bye == other.Bye && Games.SequenceEqual(other.Games);
}

/// <param name="EngineProposal">發布時引擎給的最佳建議；循環賽與淘汰賽為 null。用於「過期」判定與差異顯示。</param>
/// <param name="ForcedOverride">配對含違規（重複對戰／重複輪空）且經使用者強制採用。</param>
public sealed record Round(
    int Number,
    RoundStatus Status,
    ImmutableArray<Game> Games,
    int? Bye,
    Pairing? EngineProposal = null,
    bool ForcedOverride = false)
{
    public bool IsPublished => Status == RoundStatus.Published;

    /// <summary>已發布且每場都有結果（輪空由系統自動登錄）。</summary>
    public bool IsCompleted => IsPublished && Games.All(g => g.Result.HasResult());

    public bool Involves(int playerId) => Bye == playerId || Games.Any(g => g.Involves(playerId));

    public int? GameIndexOf(int playerId)
    {
        for (var i = 0; i < Games.Length; i++)
            if (Games[i].Involves(playerId))
                return i;
        return null;
    }
}
