using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.RoundRobin;

/// <summary>
/// 循環賽排程（開賽時一次產生）。
/// <para>
/// 輪次：圓桌法。種子 1..m（m 為偶數；奇數人數時加入虛擬種子 m，配到它即輪空）。
/// 第 r 輪（1 ≤ r ≤ m−1）：種子 r 與種子 m 對戰；其餘種子 i、j 若 i + j ≡ 2r (mod m−1) 則對戰（餘數以 1..m−1 表示）。
/// 因 m−1 為奇數，2 在模 m−1 下可逆，每對種子恰好在一輪相遇；奇數人數時每人恰好輪空一次。
/// </para>
/// <para>
/// 先後手：與輪次無關，對種子 i &lt; j，i + j 為奇數時 i 先手，否則 j 先手。
/// 對每位選手，此規則使其先手與後手次數相差不超過 1（偶數場時恰好相等）。
/// </para>
/// 雙循環：第二循環複製第一循環的輪次並互換先後手。
/// </summary>
public static class RoundRobinScheduler
{
    /// <summary>種子順序：積分高→低、註冊編號小→大。</summary>
    public static ImmutableArray<int> Seed(IEnumerable<Player> players) =>
        [.. players.OrderByDescending(p => p.Rating).ThenBy(p => p.RegNo).Select(p => p.Id)];

    /// <param name="seeded">依種子順序排列的選手 Id（索引 0 = 種子 1）。</param>
    public static ImmutableArray<Round> Build(IReadOnlyList<int> seeded, bool doubleRoundRobin)
    {
        var n = seeded.Count;
        if (n < 2)
            throw new DomainException("循環賽至少需要 2 位選手。");

        var m = n % 2 == 0 ? n : n + 1;
        var cycle = new List<(List<(int White, int Black)> Games, int? Bye)>();
        for (var r = 1; r <= m - 1; r++)
        {
            var pairs = new List<(int A, int B)> { (r, m) };
            for (var i = 1; i <= m - 1; i++)
            {
                if (i == r)
                    continue;
                var j = Residue((2 * r) - i, m - 1);
                if (i < j)
                    pairs.Add((i, j));
            }

            int? bye = null;
            var games = new List<(int, int)>();
            foreach (var (a, b) in pairs.OrderBy(p => Math.Min(p.A, p.B)))
            {
                var (lo, hi) = a < b ? (a, b) : (b, a);
                if (hi > n)
                {
                    bye = seeded[lo - 1];
                    continue;
                }

                var loWhite = (lo + hi) % 2 == 1;
                games.Add(loWhite ? (seeded[lo - 1], seeded[hi - 1]) : (seeded[hi - 1], seeded[lo - 1]));
            }

            cycle.Add((games, bye));
        }

        var rounds = ImmutableArray.CreateBuilder<Round>();
        foreach (var (games, bye) in cycle)
            rounds.Add(NewRound(rounds.Count + 1, games, bye));
        if (doubleRoundRobin)
            foreach (var (games, bye) in cycle)
                rounds.Add(NewRound(rounds.Count + 1, [.. games.Select(g => (g.Black, g.White))], bye));
        return rounds.ToImmutable();
    }

    private static int Residue(int value, int modulus)
    {
        var r = ((value % modulus) + modulus) % modulus;
        return r == 0 ? modulus : r;
    }

    private static Round NewRound(int number, List<(int White, int Black)> games, int? bye) =>
        new(number, RoundStatus.Published, [.. games.Select(g => new Game(g.White, g.Black))], bye);
}
