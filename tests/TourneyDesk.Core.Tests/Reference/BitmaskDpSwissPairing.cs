namespace TourneyDesk.Core.Tests.Reference;

/// <summary>
/// 瑞士制最佳化值 ①～④ 的位元遮罩動態規劃參考解（N ≤ 18）。
/// dp[mask, 輪空已用] = 尚未配對的選手為 ~mask 時，剩餘部分可達到的最小 (①, ②, ③, ④)。
/// 每次取最小的未配對者，與另一位未配對者配對，或（奇數人數且尚未輪空時）讓他輪空。
/// </summary>
internal static class BitmaskDpSwissPairing
{
    public sealed record Optimum(int Repeats, int ByeRepeat, int? ByeRank, long ScoreDiffSquares);

    // 把 (①, ②, ③', ④) 打包成可直接比較的 long：① 佔 [44,..)、② 佔 [43]、③' 佔 [35,43)、④ 佔 [0,35)
    private const int ByeRankShift = 35;
    private const int ByeRepeatShift = 43;
    private const int RepeatShift = 44;

    public static Optimum Solve(ReferenceRoundData data)
    {
        var n = data.Ranked.Length;
        if (n > 18)
            throw new ArgumentException("DP 參考解只支援 N ≤ 18。");
        if (n == 0)
            return new Optimum(0, 0, null, 0);

        var hasBye = n % 2 == 1;
        var full = (1 << n) - 1;
        var dp = new long[1 << n, 2];
        var choice = new int[1 << n, 2];
        for (var mask = full; mask >= 0; mask--)
        {
            for (var byeUsed = 0; byeUsed <= 1; byeUsed++)
            {
                if (mask == full)
                {
                    dp[mask, byeUsed] = hasBye && byeUsed == 0 ? long.MaxValue : 0;
                    continue;
                }

                var i = 0;
                while ((mask & (1 << i)) != 0)
                    i++;

                var best = long.MaxValue;
                var bestChoice = -1;
                for (var j = i + 1; j < n; j++)
                {
                    if ((mask & (1 << j)) != 0)
                        continue;
                    var sub = dp[mask | (1 << i) | (1 << j), byeUsed];
                    if (sub == long.MaxValue)
                        continue;
                    var value = sub + PairCost(data, i, j);
                    if (value < best)
                    {
                        best = value;
                        bestChoice = j;
                    }
                }

                if (hasBye && byeUsed == 0)
                {
                    var sub = dp[mask | (1 << i), 1];
                    if (sub != long.MaxValue)
                    {
                        var value = sub + ByeCost(data, i);
                        if (value < best)
                        {
                            best = value;
                            bestChoice = n; // 代表輪空
                        }
                    }
                }

                dp[mask, byeUsed] = best;
                choice[mask, byeUsed] = bestChoice;
            }
        }

        var total = dp[0, 0];
        int? byeRank = null;
        if (hasBye)
        {
            // 依選擇重建輪空者（③ 在打包值中已唯一決定，但為了回傳排名仍沿路追蹤）
            var mask = 0;
            var used = 0;
            while (mask != full)
            {
                var i = 0;
                while ((mask & (1 << i)) != 0)
                    i++;
                var c = choice[mask, used];
                if (c == n)
                {
                    byeRank = i + 1;
                    mask |= 1 << i;
                    used = 1;
                }
                else
                {
                    mask |= (1 << i) | (1 << c);
                }
            }
        }

        return new Optimum(
            (int)(total >> RepeatShift),
            (int)((total >> ByeRepeatShift) & 1),
            byeRank,
            total & ((1L << ByeRankShift) - 1));
    }

    private static long PairCost(ReferenceRoundData d, int a, int b)
    {
        long diff = d.Ranked[a].Score - d.Ranked[b].Score;
        var repeat = d.HaveMet(d.Ranked[a].Id, d.Ranked[b].Id) ? 1L : 0L;
        return (repeat << RepeatShift) + (diff * diff);
    }

    private static long ByeCost(ReferenceRoundData d, int i)
    {
        var n = d.Ranked.Length;
        var hadBye = d.Ranked[i].HadBye ? 1L : 0L;
        return (hadBye << ByeRepeatShift) + ((long)(n - 1 - i) << ByeRankShift);
    }
}
