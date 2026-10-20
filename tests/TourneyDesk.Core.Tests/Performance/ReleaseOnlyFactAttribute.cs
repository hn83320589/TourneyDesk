namespace TourneyDesk.Core.Tests.Performance;

/// <summary>
/// 效能測試只在 Release 組態執行：Debug 組態關閉了編譯最佳化，耗時不具代表性。
/// 執行方式：dotnet test -c Release --filter Category=Performance
/// </summary>
public sealed class ReleaseOnlyFactAttribute : FactAttribute
{
    public ReleaseOnlyFactAttribute()
    {
#if DEBUG
        Skip = "效能測試只在 Release 組態下執行（dotnet test -c Release）。";
#endif
    }
}
