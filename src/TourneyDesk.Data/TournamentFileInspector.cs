using Microsoft.Data.Sqlite;

namespace TourneyDesk.Data;

public enum FileCheckKind
{
    Ok,
    NeedsUpgrade,
    Missing,
    NotTourneyFile,
    TooNew,
    Corrupt,
}

public sealed record FileCheckResult(FileCheckKind Kind, int Version, string Message);

/// <summary>以唯讀方式檢查賽事檔，絕不修改檔案。</summary>
public static class TournamentFileInspector
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public static FileCheckResult Inspect(string path)
    {
        if (!File.Exists(path))
            return new FileCheckResult(FileCheckKind.Missing, 0, $"找不到檔案：{path}");

        try
        {
            var header = new byte[SqliteHeader.Length];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Read(header, 0, header.Length) < header.Length || !header.AsSpan().SequenceEqual(SqliteHeader))
                    return new FileCheckResult(FileCheckKind.NotTourneyFile, 0, "這不是 TourneyDesk 賽事檔（檔案格式不符）。");
            }

            using var connection = TournamentFile.Connect(path, SqliteOpenMode.ReadOnly);
            var appId = Scalar(connection, "PRAGMA application_id");
            if (appId != Schema.ApplicationId)
                return new FileCheckResult(FileCheckKind.NotTourneyFile, 0, "這是 SQLite 檔案，但不是 TourneyDesk 賽事檔。");

            var version = (int)Scalar(connection, "PRAGMA user_version");
            if (version > Schema.CurrentVersion)
            {
                return new FileCheckResult(FileCheckKind.TooNew, version,
                    $"此賽事檔由較新版本的 TourneyDesk 建立（檔案版本 {version}，本程式支援到 {Schema.CurrentVersion}），請更新程式後再開啟。");
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check";
                var result = cmd.ExecuteScalar() as string;
                if (result != "ok")
                    return new FileCheckResult(FileCheckKind.Corrupt, version, $"賽事檔已損毀（完整性檢查：{result}）。");
            }

            return version < Schema.CurrentVersion
                ? new FileCheckResult(FileCheckKind.NeedsUpgrade, version, $"賽事檔為舊版格式（版本 {version}），開啟時將先備份再升級。")
                : new FileCheckResult(FileCheckKind.Ok, version, "");
        }
        catch (SqliteException ex)
        {
            return new FileCheckResult(FileCheckKind.Corrupt, 0, $"賽事檔已損毀或無法讀取：{SqliteMessages.Describe(ex)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileCheckResult(FileCheckKind.Corrupt, 0, $"無法讀取檔案：{ex.Message}");
        }
    }

    private static long Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
