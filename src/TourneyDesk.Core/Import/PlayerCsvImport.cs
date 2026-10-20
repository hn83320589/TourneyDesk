using System.Collections.Immutable;
using System.Globalization;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Import;

/// <param name="RegNo">CSV 指定的註冊編號；空白時為 null（匯入時自動配發）。</param>
public sealed record ImportedPlayer(int LineNumber, int? RegNo, string Name, int Rating);

public sealed record ImportError(int LineNumber, string Reason);

public sealed record ImportPreview(string EncodingName, ImmutableArray<ImportedPlayer> Players, ImmutableArray<ImportError> Errors);

/// <summary>
/// 選手 CSV 匯入：第一列為標題（註冊編號／RegNo 選填；姓名／Name、積分／Rating 必填，順序不限）。
/// 每列獨立驗證：錯誤列記錄行號與原因，其餘列照常匯入。
/// </summary>
public static class PlayerCsvImport
{
    private static readonly string[] RegNoHeaders = ["註冊編號", "regno", "reg_no", "編號"];
    private static readonly string[] NameHeaders = ["姓名", "name", "名字"];
    private static readonly string[] RatingHeaders = ["積分", "rating", "等級分"];

    public static ImportPreview Parse(byte[] content, TournamentState state)
    {
        var (text, encoding) = TextDecoding.Decode(content);
        var records = Csv.Parse(text);
        if (records.Count == 0)
            return new ImportPreview(encoding, [], [new ImportError(1, "檔案沒有內容。")]);

        var header = records[0];
        var columns = header.Fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
        var regCol = IndexOf(columns, RegNoHeaders);
        var nameCol = IndexOf(columns, NameHeaders);
        var ratingCol = IndexOf(columns, RatingHeaders);
        var missingHeaders = new List<string>();
        if (nameCol < 0)
            missingHeaders.Add("姓名");
        if (ratingCol < 0)
            missingHeaders.Add("積分");
        if (missingHeaders.Count > 0)
            return new ImportPreview(encoding, [], [new ImportError(header.LineNumber, $"標題列缺少欄位：{string.Join("、", missingHeaders)}")]);

        var errors = ImmutableArray.CreateBuilder<ImportError>();
        var accepted = new List<ImportedPlayer>();
        var usedRegNos = state.Players.Values.Select(p => p.RegNo).ToHashSet();
        var capacity = TournamentState.MaxPlayers - state.Players.Count;

        foreach (var record in records.Skip(1))
        {
            var error = Validate(record, regCol, nameCol, ratingCol, usedRegNos, out var player);
            if (error is null && accepted.Count >= capacity)
                error = $"超過選手上限 {TournamentState.MaxPlayers} 人";
            if (error is not null)
            {
                errors.Add(new ImportError(record.LineNumber, error));
                continue;
            }

            if (player!.RegNo is { } reg)
                usedRegNos.Add(reg);
            accepted.Add(player);
        }

        return new ImportPreview(encoding, [.. accepted], errors.ToImmutable());
    }

    private static string? Validate(
        CsvRecord record, int regCol, int nameCol, int ratingCol, HashSet<int> usedRegNos, out ImportedPlayer? player)
    {
        player = null;
        string? Field(int col) => col >= 0 && col < record.Fields.Count ? record.Fields[col].Trim() : null;

        var name = Field(nameCol);
        var ratingText = Field(ratingCol);
        var missing = new List<string>();
        if (name is null)
            missing.Add("姓名");
        if (ratingText is null)
            missing.Add("積分");
        if (missing.Count > 0)
            return $"缺少欄位：{string.Join("、", missing)}";
        if (name!.Length == 0)
            return "姓名不可空白";
        if (name.Length > Player.MaxNameLength)
            return $"姓名超過 {Player.MaxNameLength} 個字元";
        if (ratingText!.Length == 0)
            return "積分不可空白";
        if (!int.TryParse(ratingText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var rating))
            return $"積分「{ratingText}」不是整數";
        if (rating is < Player.MinRating or > Player.MaxRating)
            return $"積分 {rating} 超出範圍（{Player.MinRating}～{Player.MaxRating}）";

        int? regNo = null;
        if (Field(regCol) is { Length: > 0 } regText)
        {
            if (!int.TryParse(regText, NumberStyles.None, CultureInfo.InvariantCulture, out var reg) || reg <= 0)
                return $"註冊編號「{regText}」不是正整數";
            if (usedRegNos.Contains(reg))
                return $"註冊編號 {reg} 重複";
            regNo = reg;
        }

        player = new ImportedPlayer(record.LineNumber, regNo, name, rating);
        return null;
    }

    private static int IndexOf(List<string> columns, string[] names) =>
        columns.FindIndex(c => names.Contains(c, StringComparer.Ordinal));
}
