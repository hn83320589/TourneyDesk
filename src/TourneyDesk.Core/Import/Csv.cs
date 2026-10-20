using System.Text;

namespace TourneyDesk.Core.Import;

/// <summary>一筆 CSV 紀錄與其在檔案中的起始行號（1 起算）。</summary>
public sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>RFC 4180 子集：逗號分隔、雙引號包住的欄位可含逗號、換行與 "" 跳脫；CRLF 或 LF 皆可。</summary>
public static class Csv
{
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordStart = 1;
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (ch == '\n')
                        line++;
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRecord();
                    line++;
                    recordStart = line;
                    break;
                default:
                    field.Append(ch);
                    fieldStarted = true;
                    break;
            }
        }

        EndRecord();
        return records;

        void EndRecord()
        {
            if (fieldStarted || field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                if (fields.Any(f => f.Length > 0))
                    records.Add(new CsvRecord(recordStart, [.. fields]));
            }

            fields = [];
            field.Clear();
            fieldStarted = false;
        }
    }

    /// <summary>
    /// 輸出一列 CSV（必要時加引號）。以 = + - @ Tab CR 開頭的欄位前加單引號，
    /// 避免試算表把（可能來自匯入資料的）姓名當成公式執行（CSV／公式注入）。
    /// </summary>
    public static string FormatRow(IEnumerable<string> values) =>
        string.Join(',', values.Select(raw =>
        {
            var v = raw.Length > 0 && "=+-@\t\r".Contains(raw[0], StringComparison.Ordinal) ? "'" + raw : raw;
            return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : v;
        }));

    /// <summary>UTF-8 with BOM 的完整 CSV 內容（Excel 可直接正確開啟中文）。</summary>
    public static byte[] ToUtf8WithBom(IEnumerable<IEnumerable<string>> rows)
    {
        var text = string.Join("\r\n", rows.Select(FormatRow)) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
    }
}

/// <summary>文字編碼偵測：有 UTF-8 BOM → UTF-8；否則嚴格 UTF-8 解碼成功 → UTF-8；否則視為 Big5。</summary>
public static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (bytes.AsSpan().StartsWith(bom))
            return (StrictUtf8.GetString(bytes, 3, bytes.Length - 3), "UTF-8（含 BOM）");

        try
        {
            return (StrictUtf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Big5.GetString(bytes), "Big5");
        }
    }

    public static Encoding Big5 { get; } = CodePagesEncodingProvider.Instance.GetEncoding(950)
        ?? throw new InvalidOperationException("此系統不支援 Big5 編碼。");
}
