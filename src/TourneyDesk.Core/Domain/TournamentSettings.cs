namespace TourneyDesk.Core.Domain;

/// <param name="ByePoints">輪空得分（半分）：0、1 或 2。</param>
/// <param name="SwissRounds">瑞士制總輪數 R；其他賽制不使用。</param>
/// <param name="ThirdPlaceMatch">單淘汰是否加賽季軍戰。</param>
public sealed record TournamentSettings(
    string Name,
    TournamentFormat Format,
    int SwissRounds,
    int ByePoints,
    bool ThirdPlaceMatch)
{
    public const int MaxNameLength = 100;
    public const int MinSwissRounds = 1;
    public const int MaxSwissRounds = 15;

    public static int DefaultByePoints(TournamentFormat format) =>
        format == TournamentFormat.Swiss ? HalfPoints.Win : 0;

    public static TournamentSettings Create(
        string name,
        TournamentFormat format,
        int swissRounds = 5,
        int? byePoints = null,
        bool thirdPlaceMatch = false)
    {
        var settings = new TournamentSettings(
            name.Trim(), format, swissRounds, byePoints ?? DefaultByePoints(format), thirdPlaceMatch);
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("賽事名稱不可空白。");
        if (Name.Length > MaxNameLength)
            throw new DomainException($"賽事名稱不可超過 {MaxNameLength} 個字元。");
        if (!Enum.IsDefined(Format))
            throw new DomainException("未知的賽制。");
        if (SwissRounds is < MinSwissRounds or > MaxSwissRounds)
            throw new DomainException($"瑞士制總輪數必須介於 {MinSwissRounds} 到 {MaxSwissRounds}。");
        if (ByePoints is < 0 or > HalfPoints.Win)
            throw new DomainException("輪空得分只能是 0、1 或 2 個半分。");
    }
}
