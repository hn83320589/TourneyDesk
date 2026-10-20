using System.Globalization;
using Microsoft.Data.Sqlite;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Data;

/// <summary>
/// 一個開啟中的 .tourney 賽事檔。每次 <see cref="Commit"/> 在單一交易中寫入（不需要「儲存」）。
/// 呼叫端應先以 <see cref="Core.Locking.LockManager"/> 取得開檔鎖定。
/// </summary>
public sealed class TournamentFile : ITournamentStore, IDisposable
{
    public const string Extension = ".tourney";

    private readonly SqliteConnection _connection;

    private TournamentFile(string path, SqliteConnection connection)
    {
        Path = path;
        _connection = connection;
    }

    public string Path { get; }

    /// <summary>非致命的警告（例如自動備份失敗）；主要寫入已成功。</summary>
    public event EventHandler<string>? Warning;

    /// <summary>建立新的賽事檔（檔案不得已存在）。</summary>
    public static TournamentFile Create(string path, TournamentState initial)
    {
        if (File.Exists(path))
            throw new StorageException($"檔案已存在：{path}");

        try
        {
            var connection = Connect(path, SqliteOpenMode.ReadWriteCreate);
            using (var tx = connection.BeginTransaction())
            {
                StateWriter.Exec(connection, tx, Schema.V1);
                for (var v = 1; v < Schema.CurrentVersion; v++)
                    StateWriter.Exec(connection, tx, Schema.Migrations[v - 1]);
                StateWriter.Write(connection, tx, null, initial);
                StateWriter.Exec(connection, tx, $"PRAGMA application_id = {Schema.ApplicationId}");
                StateWriter.Exec(connection, tx, $"PRAGMA user_version = {Schema.CurrentVersion}");
                tx.Commit();
            }

            return new TournamentFile(path, connection);
        }
        catch (SqliteException ex)
        {
            throw new StorageException(SqliteMessages.Describe(ex), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException($"無法建立檔案：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 開啟賽事檔：先以唯讀方式驗證（不符時丟出 <see cref="StorageException"/>，且不修改檔案），
    /// 舊版 schema 先備份為 <c>{檔名}.v{舊版}.bak</c> 再升級（該名稱已存在時加上 <paramref name="time"/> 的時間戳）。
    /// </summary>
    public static (TournamentFile File, TournamentState State) Open(string path, TimeProvider time)
    {
        var check = TournamentFileInspector.Inspect(path);
        if (check.Kind is not (FileCheckKind.Ok or FileCheckKind.NeedsUpgrade))
            throw new StorageException(check.Message);

        if (check.Kind == FileCheckKind.NeedsUpgrade)
            Upgrade(path, check.Version, time);

        try
        {
            var connection = Connect(path, SqliteOpenMode.ReadWrite);
            try
            {
                return (new TournamentFile(path, connection), StateReader.Read(connection));
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        catch (SqliteException ex)
        {
            throw new StorageException(SqliteMessages.Describe(ex), ex);
        }
        catch (InvalidDataException ex)
        {
            throw new StorageException($"賽事檔內容不一致：{ex.Message}", ex);
        }
    }

    public IReadOnlyList<AuditEntry> ReadAudit() => StateReader.ReadAudit(_connection);

    public void Commit(TournamentState before, TournamentState after, IReadOnlyList<AuditEntry> audit)
    {
        try
        {
            using var tx = _connection.BeginTransaction();
            StateWriter.Write(_connection, tx, before, after);
            foreach (var a in audit)
            {
                StateWriter.Exec(_connection, tx,
                    "INSERT INTO audit_log (timestamp, kind, target, before, after) VALUES ($t, $k, $g, $b, $a)",
                    ("$t", a.Timestamp.ToString("O", CultureInfo.InvariantCulture)), ("$k", a.Kind), ("$g", a.Target),
                    ("$b", a.Before), ("$a", a.After));
            }

            tx.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(SqliteMessages.Describe(ex), ex);
        }

        foreach (var label in CompletedRounds.NewlyCompleted(before, after))
            Backup(label);
    }

    /// <summary>以 SQLite 線上備份產生一致的快照：<c>{檔名}.{label}.bak</c>。</summary>
    public string Backup(string label)
    {
        var target = $"{Path}.{label}.bak";
        try
        {
            using var destination = Connect(target, SqliteOpenMode.ReadWriteCreate);
            _connection.BackupDatabase(destination);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // 主要寫入已成功；備份失敗只提出警告，不影響賽事狀態
            Warning?.Invoke(this, $"自動備份失敗（{System.IO.Path.GetFileName(target)}）：{ex.Message}");
        }

        return target;
    }

    public void Dispose() => _connection.Dispose();

    internal static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false, // 關閉時立即釋放檔案 handle（鎖定、備份、刪除都需要）
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        if (mode != SqliteOpenMode.ReadOnly)
        {
            // 不用 WAL：WAL 需要共享記憶體，在網路磁碟上不可靠
            StateWriter.Exec(connection, null, "PRAGMA journal_mode = DELETE");
            StateWriter.Exec(connection, null, "PRAGMA synchronous = FULL");
        }

        return connection;
    }

    private static void Upgrade(string path, int fromVersion, TimeProvider time)
    {
        var backup = $"{path}.v{fromVersion}.bak";
        if (File.Exists(backup))
            backup = $"{path}.v{fromVersion}.{time.GetLocalNow():yyyyMMddHHmmss}.bak";
        try
        {
            File.Copy(path, backup);
            using var connection = Connect(path, SqliteOpenMode.ReadWrite);
            using var tx = connection.BeginTransaction();
            for (var v = fromVersion; v < Schema.CurrentVersion; v++)
                StateWriter.Exec(connection, tx, Schema.Migrations[v - 1]);
            StateWriter.Exec(connection, tx, $"PRAGMA user_version = {Schema.CurrentVersion}");
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException($"升級賽事檔失敗（已備份於 {backup}）：{SqliteMessages.Describe(ex)}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException($"升級前無法建立備份：{ex.Message}", ex);
        }
    }
}

internal static class SqliteMessages
{
    public static string Describe(SqliteException ex) => ex.SqliteErrorCode switch
    {
        5 or 6 => "賽事檔正被其他程式使用，請稍後再試。",
        8 => "賽事檔為唯讀，無法寫入。",
        10 => "磁碟讀寫錯誤，請檢查磁碟或網路連線。",
        11 or 26 => "賽事檔已損毀或不是 SQLite 檔案。",
        13 => "磁碟空間不足。",
        14 => "無法開啟賽事檔（路徑不存在或沒有權限）。",
        _ => $"資料庫錯誤（代碼 {ex.SqliteErrorCode}）：{ex.Message}",
    };
}

/// <summary>判斷哪些輪次在這次轉移中「剛完成」，用於自動備份。</summary>
internal static class CompletedRounds
{
    public static IEnumerable<string> NewlyCompleted(TournamentState before, TournamentState after)
    {
        foreach (var r in after.Rounds)
        {
            var old = r.Number <= before.Rounds.Length ? before.Rounds[r.Number - 1] : null;
            if (r.IsCompleted && old is not { IsCompleted: true })
                yield return $"r{r.Number}";
        }

        if (after.Bracket is { } b)
        {
            for (var round = 1; round <= b.RoundCount; round++)
            {
                var doneNow = b.Matches.Where(m => m.Round == round).All(m => m.Outcome is not null);
                var doneBefore = before.Bracket?.Matches.Where(m => m.Round == round).All(m => m.Outcome is not null) ?? false;
                if (doneNow && !doneBefore)
                    yield return $"ko-r{round}";
            }
        }
    }
}
