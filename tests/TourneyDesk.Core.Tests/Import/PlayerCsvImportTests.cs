using System.Text;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Import;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Import;

/// <summary>驗收 1：CSV 匯入選手。</summary>
public class PlayerCsvImportTests
{
    private const string Sample = "註冊編號,姓名,積分\r\n,王小明,1500\r\n,李大華,1620\r\n";

    private static TournamentState Empty() => TournamentState.Create(TournamentSettings.Create("匯入", TournamentFormat.Swiss));

    private static byte[] Utf8(string s, bool bom) =>
        bom ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(s)] : Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData(true, "UTF-8（含 BOM）")]
    [InlineData(false, "UTF-8")]
    public void Utf8_with_and_without_bom_is_decoded(bool bom, string encoding)
    {
        var preview = PlayerCsvImport.Parse(Utf8(Sample, bom), Empty());

        Assert.Equal(encoding, preview.EncodingName);
        Assert.Empty(preview.Errors);
        Assert.Equal(["王小明", "李大華"], preview.Players.Select(p => p.Name));
    }

    [Fact]
    public void Big5_is_decoded()
    {
        var bytes = TextDecoding.Big5.GetBytes("姓名,積分\r\n張三豐,1800\r\n郭靖,1750\r\n");

        var preview = PlayerCsvImport.Parse(bytes, Empty());

        Assert.Equal("Big5", preview.EncodingName);
        Assert.Equal(["張三豐", "郭靖"], preview.Players.Select(p => p.Name));
        Assert.Equal([1800, 1750], preview.Players.Select(p => p.Rating));
    }

    [Fact]
    public void Invalid_rows_report_line_and_reason_while_others_import()
    {
        var csv = string.Join("\n",
            "註冊編號,姓名,積分",
            "1,王小明,1500",       // 第 2 行：與既有 #1 重複
            "7,李大華,1620",       // 第 3 行：OK
            "7,趙小六,1400",       // 第 4 行：檔內重複
            ",陳二,3001",         // 第 5 行：超出範圍
            ",林一,-5",           // 第 6 行：超出範圍
            ",張三",              // 第 7 行：缺欄位
            ",,1500",             // 第 8 行：姓名空白
            ",周八,abc",          // 第 9 行：非整數
            "x,孫七,1000",        // 第 10 行：編號非整數
            ",吳九,0");           // 第 11 行：OK
        var state = Empty().Then(new AddPlayer("既有", 1000));

        var preview = PlayerCsvImport.Parse(Utf8(csv, bom: false), state);

        Assert.Equal(["李大華", "吳九"], preview.Players.Select(p => p.Name));
        Assert.Equal(
        [
            (2, "註冊編號 1 重複"),
            (4, "註冊編號 7 重複"),
            (5, "積分 3001 超出範圍（0～3000）"),
            (6, "積分 -5 超出範圍（0～3000）"),
            (7, "缺少欄位：積分"),
            (8, "姓名不可空白"),
            (9, "積分「abc」不是整數"),
            (10, "註冊編號「x」不是正整數"),
        ], preview.Errors.Select(e => (e.LineNumber, e.Reason)));
    }

    [Fact]
    public void Header_can_be_english_and_in_any_order_without_regno_column()
    {
        var preview = PlayerCsvImport.Parse(Utf8("Rating,Name\n1500,Alice\n1400,Bob\n", false), Empty());

        Assert.Equal([("Alice", 1500), ("Bob", 1400)], preview.Players.Select(p => (p.Name, p.Rating)));
        Assert.All(preview.Players, p => Assert.Null(p.RegNo));
    }

    [Fact]
    public void Missing_required_header_is_reported()
    {
        var preview = PlayerCsvImport.Parse(Utf8("姓名,分數\n王小明,1500\n", false), Empty());

        Assert.Empty(preview.Players);
        Assert.Equal((1, "標題列缺少欄位：積分"), (preview.Errors[0].LineNumber, preview.Errors[0].Reason));
    }

    [Fact]
    public void Quoted_fields_with_commas_quotes_and_newlines_are_parsed_with_correct_line_numbers()
    {
        var csv = "姓名,積分\n\"王, 小明\",1500\n\"多\n行\",abc\n\"說\"\"好\"\"\",1200\n";

        var preview = PlayerCsvImport.Parse(Utf8(csv, false), Empty());

        Assert.Equal(["王, 小明", "說\"好\""], preview.Players.Select(p => p.Name));
        Assert.Equal((3, "積分「abc」不是整數"), (preview.Errors[0].LineNumber, preview.Errors[0].Reason));
        Assert.Equal(5, preview.Players[1].LineNumber);
    }

    [Fact]
    public void Blank_lines_are_ignored()
    {
        var preview = PlayerCsvImport.Parse(Utf8("姓名,積分\n\n王小明,1500\n\n", false), Empty());

        Assert.Single(preview.Players);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Import_assigns_missing_registration_numbers_avoiding_explicit_ones()
    {
        var state = Empty().Then(new AddPlayer("既有", 1000)); // #1
        var preview = PlayerCsvImport.Parse(Utf8("註冊編號,姓名,積分\n,甲,1\n2,乙,2\n,丙,3\n", false), state);

        var t = state.Run(new ImportPlayers(preview.Players));

        Assert.Equal([(1, "既有"), (2, "乙"), (3, "甲"), (4, "丙")],
            t.NewState.Players.Values.Select(p => (p.RegNo, p.Name)).OrderBy(x => x.RegNo));
        Assert.Equal(5, t.NewState.NextRegNo);
        Assert.Equal("匯入選手", Assert.Single(t.Audit).Kind);
    }

    [Fact]
    public void Import_is_rejected_after_start_or_when_empty()
    {
        var started = SwissFlow.NewSwiss(2).NextRound();

        Assert.Equal("賽事開始後不能匯入選手。", started.RuleViolation(new ImportPlayers([new ImportedPlayer(2, null, "X", 1)])));
        Assert.Equal("沒有可匯入的選手。", Empty().RuleViolation(new ImportPlayers([])));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+886", "'+886")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("王小明", "王小明")]
    public void Csv_writer_neutralizes_formula_injection(string value, string expected)
    {
        Assert.Equal(expected, Csv.FormatRow([value]));
    }

    [Fact]
    public void Csv_writer_escapes_and_prefixes_bom()
    {
        var bytes = Csv.ToUtf8WithBom([["姓名", "備註"], ["王, 小明", "說\"好\""]]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("姓名,備註\r\n\"王, 小明\",\"說\"\"好\"\"\"\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }
}
