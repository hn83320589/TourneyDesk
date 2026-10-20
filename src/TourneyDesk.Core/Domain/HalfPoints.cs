using System.Globalization;

namespace TourneyDesk.Core.Domain;

/// <summary>所有分數一律以整數「半分」表示：勝 2、和 1、負 0。</summary>
public static class HalfPoints
{
    public const int Win = 2;
    public const int Draw = 1;
    public const int Loss = 0;

    /// <summary>格式化為一般分數顯示，例如 5 → "2.5"。</summary>
    public static string Format(long halfPoints)
    {
        var sign = halfPoints < 0 ? "-" : "";
        var abs = Math.Abs(halfPoints);
        var whole = (abs / 2).ToString(CultureInfo.InvariantCulture);
        return abs % 2 == 0 ? sign + whole : sign + whole + ".5";
    }
}
