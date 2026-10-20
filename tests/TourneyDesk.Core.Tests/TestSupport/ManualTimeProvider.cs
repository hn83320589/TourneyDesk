namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>可手動推進的時鐘。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8)))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => _now += delta;
}
