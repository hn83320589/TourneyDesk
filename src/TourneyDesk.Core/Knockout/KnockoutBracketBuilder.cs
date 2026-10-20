using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Knockout;

/// <summary>
/// 建立單淘汰籤表：種子依（積分高→低、註冊編號小→大）；籤表大小 S 為 ≥ N 的最小 2 的冪次；
/// 位置採標準排法（S = 8 時由上到下為 1v8、4v5、2v7、3v6）。種子 &gt; N 的位置為空，
/// 其對手（必為最高的 S − N 位種子之一）第 1 輪輪空。
/// </summary>
public static class KnockoutBracketBuilder
{
    public static ImmutableArray<int> Seed(IEnumerable<Player> players) =>
        [.. players.OrderByDescending(p => p.Rating).ThenBy(p => p.RegNo).Select(p => p.Id)];

    /// <summary>標準排法：由上到下每個位置的種子編號。positions(2k) 由 positions(k) 的每個 s 展開為 s、2k+1−s。</summary>
    public static ImmutableArray<int> StandardPositions(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "籤表大小必須是 ≥ 2 的 2 的冪次。");
        var positions = new List<int> { 1, 2 };
        for (var k = 2; k < size; k *= 2)
            positions = [.. positions.SelectMany(s => new[] { s, (2 * k) + 1 - s })];
        return [.. positions];
    }

    public static int SizeFor(int playerCount)
    {
        var size = 2;
        while (size < playerCount)
            size *= 2;
        return size;
    }

    public static Bracket Build(ImmutableArray<int> seeds, bool thirdPlaceMatch)
    {
        if (seeds.Length < 2)
            throw new DomainException("淘汰賽至少需要 2 位選手。");

        var size = SizeFor(seeds.Length);
        var positions = StandardPositions(size);
        var matches = ImmutableArray.CreateBuilder<KnockoutMatch>();
        var previousRound = new List<int>();

        for (var p = 0; p < size / 2; p++)
        {
            var id = matches.Count + 1;
            matches.Add(new KnockoutMatch(id, 1, p, KnockoutSource.Seed(positions[2 * p]), KnockoutSource.Seed(positions[(2 * p) + 1])));
            previousRound.Add(id);
        }

        var round = 1;
        while (previousRound.Count > 1)
        {
            round++;
            var current = new List<int>();
            for (var p = 0; p < previousRound.Count / 2; p++)
            {
                var id = matches.Count + 1;
                matches.Add(new KnockoutMatch(id, round, p,
                    KnockoutSource.WinnerOf(previousRound[2 * p]), KnockoutSource.WinnerOf(previousRound[(2 * p) + 1])));
                current.Add(id);
            }

            if (current.Count == 1 && thirdPlaceMatch && previousRound.Count == 2)
            {
                matches.Add(new KnockoutMatch(matches.Count + 1, round, 1,
                    KnockoutSource.LoserOf(previousRound[0]), KnockoutSource.LoserOf(previousRound[1]), IsThirdPlace: true));
            }

            previousRound = current;
        }

        return new Bracket(size, seeds, matches.ToImmutable());
    }
}
