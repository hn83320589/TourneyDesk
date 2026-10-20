namespace TourneyDesk.Core.Domain;

/// <summary>對局結果。White = 先手，Black = 後手。輪空不屬於對局，由系統自動計分。</summary>
public enum GameResult
{
    None,
    WhiteWin,
    Draw,
    BlackWin,
    WhiteForfeit,
    BlackForfeit,
    DoubleForfeit,
}

public static class GameResultExtensions
{
    public static bool HasResult(this GameResult r) => r != GameResult.None;

    /// <summary>雙方皆到場、實際下完的對局（直接對戰、SB、勝場數只計這類）。</summary>
    public static bool IsPlayed(this GameResult r) =>
        r is GameResult.WhiteWin or GameResult.Draw or GameResult.BlackWin;

    public static bool IsForfeit(this GameResult r) =>
        r is GameResult.WhiteForfeit or GameResult.BlackForfeit or GameResult.DoubleForfeit;

    public static int WhitePoints(this GameResult r) => r switch
    {
        GameResult.WhiteWin or GameResult.BlackForfeit => HalfPoints.Win,
        GameResult.Draw => HalfPoints.Draw,
        _ => HalfPoints.Loss,
    };

    public static int BlackPoints(this GameResult r) => r switch
    {
        GameResult.BlackWin or GameResult.WhiteForfeit => HalfPoints.Win,
        GameResult.Draw => HalfPoints.Draw,
        _ => HalfPoints.Loss,
    };

    public static string DisplayName(this GameResult r) => r switch
    {
        GameResult.None => "未登錄",
        GameResult.WhiteWin => "先手勝",
        GameResult.Draw => "和",
        GameResult.BlackWin => "後手勝",
        GameResult.WhiteForfeit => "先手棄權",
        GameResult.BlackForfeit => "後手棄權",
        GameResult.DoubleForfeit => "雙方棄權",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null),
    };

    /// <summary>短格式（列印、匯出用）。</summary>
    public static string ShortText(this GameResult r) => r switch
    {
        GameResult.None => "",
        GameResult.WhiteWin => "1-0",
        GameResult.Draw => "½-½",
        GameResult.BlackWin => "0-1",
        GameResult.WhiteForfeit => "-/+",
        GameResult.BlackForfeit => "+/-",
        GameResult.DoubleForfeit => "-/-",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null),
    };
}
