using System.Globalization;

namespace TourneyDesk.Core.Tests.Performance;

/// <summary>
/// 效能門檻。CI 雲端 runner 較慢，可用環境變數 TOURNEYDESK_PERF_FACTOR 放寬倍數（CI 設為 2）；
/// README 的數據以開發機實測為準。
/// </summary>
internal static class PerformanceLimits
{
    public static double Factor =>
        double.TryParse(Environment.GetEnvironmentVariable("TOURNEYDESK_PERF_FACTOR"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 1.0;

    public static TimeSpan SwissRound400 => TimeSpan.FromMilliseconds(3000 * Factor);

    public static TimeSpan Standings400 => TimeSpan.FromMilliseconds(200 * Factor);
}
