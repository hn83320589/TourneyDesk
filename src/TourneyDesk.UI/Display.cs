using System.Globalization;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.UI;

/// <summary>畫面與匯出共用的文字格式（一律不受地區設定影響）。</summary>
public static class Display
{
    public static string Score(long halfPoints) => HalfPoints.Format(halfPoints);

    /// <summary>SB 以「半分 × 半分」儲存，顯示為一般分數（÷4）。</summary>
    public static string Quarter(long quarterPoints)
    {
        var sign = quarterPoints < 0 ? "-" : "";
        var abs = Math.Abs(quarterPoints);
        var whole = (abs / 4).ToString(CultureInfo.InvariantCulture);
        return (abs % 4) switch
        {
            0 => sign + whole,
            1 => sign + whole + ".25",
            2 => sign + whole + ".5",
            _ => sign + whole + ".75",
        };
    }

    public static string Player(TournamentState s, int id) =>
        s.Players.TryGetValue(id, out var p) ? $"#{p.RegNo} {p.Name}" : "—";

    public static string Name(TournamentState s, int id) =>
        s.Players.TryGetValue(id, out var p) ? p.Name : "—";

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Status(Player p) =>
        p.WithdrawRound is { } w ? $"第 {w} 輪起退賽"
        : p.JoinRound is { } j ? $"第 {j} 輪起加入"
        : "在籍";

    public static readonly GameResult[] EnterableResults =
    [
        GameResult.WhiteWin, GameResult.Draw, GameResult.BlackWin,
        GameResult.WhiteForfeit, GameResult.BlackForfeit, GameResult.DoubleForfeit,
    ];
}
