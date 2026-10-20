using Xunit.Sdk;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>
/// 自寫的性質測試輔助：每個案例使用由主種子衍生的固定子種子；
/// 失敗時訊息中印出子種子，可用 <see cref="Replay"/> 單獨重現。
/// </summary>
internal static class Property
{
    public static void ForAll(int seed, int cases, Action<Random, int> body)
    {
        var master = new Random(seed);
        for (var i = 0; i < cases; i++)
        {
            var caseSeed = master.Next();
            try
            {
                body(new Random(caseSeed), caseSeed);
            }
            catch (Exception ex) when (ex is XunitException or InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
            {
                throw new XunitException(
                    $"性質測試失敗：主種子={seed}，第 {i} 個案例，案例種子={caseSeed}（以 Property.Replay({caseSeed}, …) 重現）\n{ex}");
            }
        }
    }

    public static void Replay(int caseSeed, Action<Random, int> body) => body(new Random(caseSeed), caseSeed);
}
