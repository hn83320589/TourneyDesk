using System.Collections.Immutable;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Layout;

public sealed record BracketLayoutOptions(
    int BoxWidth = 180,
    int BoxHeight = 48,
    int HorizontalGap = 40,
    int VerticalGap = 16,
    int Margin = 20);

/// <summary>一場比賽在頁面上的方框（整數座標，左上角為原點）。</summary>
public sealed record BoxLayout(int MatchId, int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>上方（A）參賽者那一列的垂直中心。</summary>
    public int SlotAY => Y + (Height / 4);

    /// <summary>下方（B）參賽者那一列的垂直中心。</summary>
    public int SlotBY => Y + (3 * Height / 4);

    public int CenterY => Y + (Height / 2);

    public bool Overlaps(BoxLayout o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>由子場次（勝者）連到父場次某一格的折線。</summary>
public sealed record Connector(int FromMatchId, int ToMatchId, bool ToSlotB, ImmutableArray<LayoutPoint> Points);

public readonly record struct LayoutPoint(int X, int Y);

public sealed record BracketPage(int Width, int Height, ImmutableArray<BoxLayout> Boxes, ImmutableArray<Connector> Connectors);

/// <summary>
/// 籤表版面計算（不引用任何 UI）：
/// 葉節點（該頁中子場次不在同頁者）由上到下排列，其餘場次的垂直位置為兩個子場次的中點；
/// 欄位 = 輪次。季軍戰放在決賽下方。連線自子場次右緣中點，經中間垂直線，接到父場次左緣對應格（A 上半／B 下半）。
/// </summary>
public static class BracketLayout
{
    public static BracketPage Full(Bracket bracket, BracketLayoutOptions? options = null) =>
        LayoutPage(bracket, bracket.Matches.Select(m => m.Id).ToHashSet(), options ?? new());

    /// <summary>
    /// 依頁面高度分頁：每頁容納 c 個葉方框（c 為 2 的冪次）。若某一輪的場次數超過 c，
    /// 就把該輪起的 log2(c)+1 輪依 c 個一組切成數頁（每頁一棵子樹），再對更上層的輪次遞迴處理。
    /// 每場比賽恰好出現在一頁。
    /// </summary>
    public static ImmutableArray<BracketPage> Paginate(Bracket bracket, int pageHeight, BracketLayoutOptions? options = null)
    {
        var o = options ?? new BracketLayoutOptions();
        var perPage = 1;
        while ((2 * perPage * (o.BoxHeight + o.VerticalGap)) + (2 * o.Margin) + o.BoxHeight + o.VerticalGap <= pageHeight)
            perPage *= 2;

        var pages = ImmutableArray.CreateBuilder<BracketPage>();
        var fromRound = 1;
        while (true)
        {
            var leaves = bracket.Matches.Where(m => m.Round == fromRound && !m.IsThirdPlace).OrderBy(m => m.Position).ToList();
            if (leaves.Count <= perPage)
            {
                var top = bracket.Matches.Where(m => m.Round >= fromRound).Select(m => m.Id).ToHashSet();
                pages.Add(LayoutPage(bracket, top, o));
                return pages.ToImmutable();
            }

            var depth = System.Numerics.BitOperations.Log2((uint)perPage); // 子樹包含 fromRound .. fromRound+depth
            for (var chunk = 0; chunk < leaves.Count / perPage; chunk++)
            {
                var ids = bracket.Matches
                    .Where(m => !m.IsThirdPlace && m.Round >= fromRound && m.Round <= fromRound + depth
                                && (m.Position << (m.Round - fromRound)) / perPage == chunk)
                    .Select(m => m.Id)
                    .ToHashSet();
                pages.Add(LayoutPage(bracket, ids, o));
            }

            fromRound += depth + 1;
        }
    }

    private static BracketPage LayoutPage(Bracket bracket, IReadOnlySet<int> ids, BracketLayoutOptions o)
    {
        var matches = bracket.Matches.Where(m => ids.Contains(m.Id)).ToList();
        var main = matches.Where(m => !m.IsThirdPlace).ToList();
        var minRound = main.Count == 0 ? 1 : main.Min(m => m.Round);
        var rowPitch = o.BoxHeight + o.VerticalGap;
        var centers = new Dictionary<int, int>();
        var leafIndex = 0;

        foreach (var m in main.OrderBy(m => m.Round).ThenBy(m => m.Position))
        {
            var children = ChildrenOnPage(m, ids);
            centers[m.Id] = children.Count == 0
                ? o.Margin + (leafIndex++ * rowPitch) + (o.BoxHeight / 2)
                : children.Sum(c => centers[c]) / children.Count;
        }

        var boxes = ImmutableArray.CreateBuilder<BoxLayout>();
        foreach (var m in main.OrderBy(m => m.Id))
        {
            var x = o.Margin + ((m.Round - minRound) * (o.BoxWidth + o.HorizontalGap));
            boxes.Add(new BoxLayout(m.Id, x, centers[m.Id] - (o.BoxHeight / 2), o.BoxWidth, o.BoxHeight));
        }

        var bottom = boxes.Count == 0 ? o.Margin : boxes.Max(b => b.Bottom);
        if (matches.FirstOrDefault(m => m.IsThirdPlace) is { } third && main.Any(m => m.Id == bracket.Final.Id))
        {
            var final = boxes.Single(b => b.MatchId == bracket.Final.Id);
            var y = Math.Max(final.Bottom, bottom) + (2 * o.VerticalGap);
            boxes.Add(new BoxLayout(third.Id, final.X, y, o.BoxWidth, o.BoxHeight));
        }

        var byId = boxes.ToDictionary(b => b.MatchId);
        var connectors = ImmutableArray.CreateBuilder<Connector>();
        foreach (var m in main.OrderBy(m => m.Id))
        {
            foreach (var (source, isB) in new[] { (m.A, false), (m.B, true) })
            {
                if (source.Kind != KnockoutSourceKind.Winner || !byId.TryGetValue(source.Value, out var child))
                    continue;
                var parent = byId[m.Id];
                var targetY = isB ? parent.SlotBY : parent.SlotAY;
                var midX = child.Right + (o.HorizontalGap / 2);
                connectors.Add(new Connector(child.MatchId, parent.MatchId, isB,
                    [new(child.Right, child.CenterY), new(midX, child.CenterY), new(midX, targetY), new(parent.X, targetY)]));
            }
        }

        var width = boxes.Count == 0 ? 2 * o.Margin : boxes.Max(b => b.Right) + o.Margin;
        var height = boxes.Count == 0 ? 2 * o.Margin : boxes.Max(b => b.Bottom) + o.Margin;
        return new BracketPage(width, height, boxes.ToImmutable(), connectors.ToImmutable());
    }

    private static List<int> ChildrenOnPage(KnockoutMatch m, IReadOnlySet<int> ids) =>
        new[] { m.A, m.B }
            .Where(s => s.Kind == KnockoutSourceKind.Winner && ids.Contains(s.Value))
            .Select(s => s.Value)
            .ToList();
}
