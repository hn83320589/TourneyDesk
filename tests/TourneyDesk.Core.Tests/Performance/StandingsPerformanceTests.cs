using System.Diagnostics;
using TourneyDesk.Core.Standings;
using TourneyDesk.Core.Tests.TestSupport;
using Xunit.Abstractions;

namespace TourneyDesk.Core.Tests.Performance;

[Trait("Category", "Performance")]
public class StandingsPerformanceTests(ITestOutputHelper output)
{
    /// <summary>驗收 22（前半）：400 人、9 輪的積分榜重算符合上限。</summary>
    [ReleaseOnlyFact]
    public void Standings_for_400_players_are_computed_within_limit()
    {
        var (state, _) = SwissHistoryGenerator.Generate(new Random(22), 400, 9);
        StandingsCalculator.Compute(state); // 暖機

        var sw = Stopwatch.StartNew();
        var rows = StandingsCalculator.Compute(state);
        sw.Stop();

        output.WriteLine($"400 人積分榜重算：{sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(400, rows.Length);
        Assert.True(sw.Elapsed <= PerformanceLimits.Standings400, $"耗時 {sw.ElapsedMilliseconds} ms 超過上限");
    }
}
