using System.Collections.Immutable;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Standings;

namespace TourneyDesk.Core.Layout;

/// <summary>交叉表資料：選手依名次排列；Cells[i][j] 為第 i 位對第 j 位的結果文字（多場以空白分隔）。</summary>
public sealed record CrosstableData(ImmutableArray<StandingRow> Players, ImmutableArray<ImmutableArray<string>> Cells);

public static class Crosstable
{
    /// <summary>
    /// 結果符號（以列選手的角度）：1 勝、½ 和、0 負、+ 棄權勝、- 棄權負、= 雙方棄權、空白 未登錄；對角線為 ×。
    /// </summary>
    public static CrosstableData Build(TournamentState state)
    {
        var rows = StandingsCalculator.Compute(state);
        var index = rows.Select((r, i) => (r.PlayerId, i)).ToDictionary(x => x.PlayerId, x => x.i);
        var cells = rows.Select(_ => rows.Select(_ => new List<string>()).ToArray()).ToArray();

        foreach (var g in state.Rounds.Where(r => r.IsPublished).SelectMany(r => r.Games))
        {
            if (!index.TryGetValue(g.White, out var w) || !index.TryGetValue(g.Black, out var b))
                continue;
            cells[w][b].Add(Symbol(g.Result, white: true));
            cells[b][w].Add(Symbol(g.Result, white: false));
        }

        return new CrosstableData(rows, [.. cells.Select((row, i) => row.Select((c, j) =>
            i == j ? "×" : string.Join(' ', c)).ToImmutableArray())]);
    }

    private static string Symbol(GameResult r, bool white) => (r, white) switch
    {
        (GameResult.None, _) => "·",
        (GameResult.Draw, _) => "½",
        (GameResult.DoubleForfeit, _) => "=",
        (GameResult.WhiteWin, true) or (GameResult.BlackWin, false) => "1",
        (GameResult.WhiteWin, false) or (GameResult.BlackWin, true) => "0",
        (GameResult.BlackForfeit, true) or (GameResult.WhiteForfeit, false) => "+",
        _ => "-",
    };
}

public sealed record CrosstableLayoutOptions(
    int RankWidth = 40,
    int NameWidth = 140,
    int CellWidth = 36,
    int ScoreWidth = 56,
    int HeaderHeight = 32,
    int RowHeight = 24);

/// <summary>交叉表某一格在頁面上的位置。<paramref name="Row"/>／<paramref name="Column"/> 為全表索引。</summary>
public sealed record CellLayout(int Row, int Column, int X, int Y, int Width, int Height)
{
    public bool Overlaps(CellLayout o) => X < o.X + o.Width && o.X < X + Width && Y < o.Y + o.Height && o.Y < Y + Height;
}

/// <param name="Rows">此頁包含的列（全表索引）。</param>
/// <param name="Columns">此頁包含的欄（全表索引）。</param>
public sealed record CrosstablePage(
    int Width,
    int Height,
    ImmutableArray<int> Rows,
    ImmutableArray<int> Columns,
    bool ShowScore,
    ImmutableArray<CellLayout> Cells);

/// <summary>
/// 交叉表版面計算與分頁（列 × 欄兩個方向）。每頁都帶自己的列標題（名次、姓名）與欄標題；
/// 每一格恰好出現在一頁；總分欄放在最後一欄所在的頁面。
/// </summary>
public static class CrosstableLayout
{
    public static ImmutableArray<CrosstablePage> Paginate(int playerCount, int rowsPerPage, int columnsPerPage, CrosstableLayoutOptions? options = null)
    {
        if (rowsPerPage < 1 || columnsPerPage < 1)
            throw new ArgumentOutOfRangeException(nameof(rowsPerPage), "每頁至少要有一列與一欄。");
        var o = options ?? new CrosstableLayoutOptions();
        var pages = ImmutableArray.CreateBuilder<CrosstablePage>();
        for (var r0 = 0; r0 < Math.Max(playerCount, 1); r0 += rowsPerPage)
        {
            for (var c0 = 0; c0 < Math.Max(playerCount, 1); c0 += columnsPerPage)
            {
                var rows = Enumerable.Range(r0, Math.Min(rowsPerPage, playerCount - r0)).ToImmutableArray();
                var cols = Enumerable.Range(c0, Math.Min(columnsPerPage, playerCount - c0)).ToImmutableArray();
                var showScore = c0 + columnsPerPage >= playerCount;
                var cells = ImmutableArray.CreateBuilder<CellLayout>(rows.Length * cols.Length);
                for (var i = 0; i < rows.Length; i++)
                {
                    for (var j = 0; j < cols.Length; j++)
                    {
                        cells.Add(new CellLayout(rows[i], cols[j],
                            o.RankWidth + o.NameWidth + (j * o.CellWidth), o.HeaderHeight + (i * o.RowHeight),
                            o.CellWidth, o.RowHeight));
                    }
                }

                var width = o.RankWidth + o.NameWidth + (cols.Length * o.CellWidth) + (showScore ? o.ScoreWidth : 0);
                var height = o.HeaderHeight + (rows.Length * o.RowHeight);
                pages.Add(new CrosstablePage(width, height, rows, cols, showScore, cells.MoveToImmutable()));
            }
        }

        return pages.ToImmutable();
    }

    /// <summary>單頁完整版（畫面顯示用）。</summary>
    public static CrosstablePage Full(int playerCount, CrosstableLayoutOptions? options = null) =>
        Paginate(playerCount, Math.Max(playerCount, 1), Math.Max(playerCount, 1), options)[0];
}
