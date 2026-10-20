using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Data.Tests;

public class TournamentFileTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    /// <summary>注入的時鐘（2026-10-01 01:00 UTC）；ManualTimeProvider 的本地時區為 UTC。</summary>
    private static readonly ManualTimeProvider Time = new();

    public void Dispose() => _dir.Dispose();

    private string NewPath() => _dir.File($"{Guid.NewGuid():N}.tourney");

    private static string FileHash(string path)
    {
        // 允許 SQLite 連線同時開著檔案
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static TournamentState SampleSwiss() =>
        SwissFlow.NewSwiss(5).NextRound().Results(GameResult.WhiteWin, GameResult.Draw).Draft();

    private static void RawSql(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Create_then_open_round_trips_state()
    {
        var path = NewPath();
        var state = SampleSwiss();
        TournamentFile.Create(path, state).Dispose();

        var (file, loaded) = TournamentFile.Open(path, Time);
        using (file)
            Assert.Equal(StateHasher.Compute(state), StateHasher.Compute(loaded));
    }

    [Fact]
    public void Create_refuses_to_overwrite_existing_file()
    {
        var path = NewPath();
        File.WriteAllText(path, "x");

        Assert.Throws<StorageException>(() => TournamentFile.Create(path, SampleSwiss()));
        Assert.Equal("x", File.ReadAllText(path));
    }

    /// <summary>驗收 18：隨機操作序列逐筆寫入（差異寫入的各種路徑），任何時點重新開檔都與記憶體狀態完全相同。</summary>
    [Fact]
    public void Committed_transitions_round_trip_losslessly()
    {
        Property.ForAll(seed: 18, cases: 24, (rng, _) =>
        {
            var path = NewPath();
            var format = (TournamentFormat)rng.Next(4);
            var initial = TournamentState.Create(TournamentSettings.Create("往返", format, 5, thirdPlaceMatch: rng.Next(2) == 0));
            for (var i = 0; i < rng.Next(2, 9); i++)
                initial = initial.Then(new AddPlayer($"P{i}", rng.Next(0, 3001)));

            using var file = TournamentFile.Create(path, initial);
            var session = new TournamentSession(initial, file, new ManualTimeProvider());
            for (var step = 0; step < 40; step++)
            {
                if (rng.Next(6) == 0 && session.CanUndo)
                    session.Undo();
                else
                    session.Execute(RandomOperations.Next(session.State, rng));

                if (step % 8 == 7)
                {
                    var (reopened, loaded) = TournamentFile.Open(path, Time);
                    reopened.Dispose();
                    Assert.Equal(StateHasher.Compute(session.State), StateHasher.Compute(loaded));
                }
            }
        });
    }

    [Fact]
    public void Audit_log_is_persisted_and_append_only()
    {
        var path = NewPath();
        var initial = TournamentState.Create(TournamentSettings.Create("稽核", TournamentFormat.Swiss));
        using (var file = TournamentFile.Create(path, initial))
        {
            var session = new TournamentSession(initial, file, new ManualTimeProvider());
            session.Execute(new AddPlayer("王小明", 1500));
            session.Execute(new EditPlayer(1, "王曉明", 1500));
            session.Undo();

            var audit = file.ReadAudit();
            Assert.Equal(["新增選手", "編輯選手", "復原"], audit.Select(a => a.Kind));
            Assert.Equal("姓名=王小明, 積分=1500", audit[1].Before);
        }

        var update = Assert.Throws<SqliteException>(() => RawSql(path, "UPDATE audit_log SET kind = 'x'"));
        Assert.Contains("只能新增", update.Message);
        Assert.Throws<SqliteException>(() => RawSql(path, "DELETE FROM audit_log"));
    }

    [Fact]
    public void Too_new_version_is_rejected_without_modifying_file()
    {
        var path = NewPath();
        TournamentFile.Create(path, SampleSwiss()).Dispose();
        RawSql(path, "PRAGMA user_version = 99");
        var hash = FileHash(path);

        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(path, Time));

        Assert.Contains("較新版本", ex.Message);
        Assert.Equal(hash, FileHash(path));
    }

    [Fact]
    public void Non_sqlite_file_is_rejected_without_modifying_file()
    {
        var path = NewPath();
        File.WriteAllText(path, "姓名,積分\n王小明,1500\n");
        var hash = FileHash(path);

        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(path, Time));

        Assert.Contains("不是 TourneyDesk 賽事檔", ex.Message);
        Assert.Equal(hash, FileHash(path));
    }

    [Fact]
    public void Foreign_sqlite_database_is_rejected_without_modifying_file()
    {
        var path = NewPath();
        RawSql(path, "CREATE TABLE t (x INTEGER)");
        var hash = FileHash(path);

        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(path, Time));

        Assert.Equal("這是 SQLite 檔案，但不是 TourneyDesk 賽事檔。", ex.Message);
        Assert.Equal(hash, FileHash(path));
    }

    [Fact]
    public void Corrupted_file_is_rejected_without_modifying_file()
    {
        var path = NewPath();
        TournamentFile.Create(path, SampleSwiss()).Dispose();
        var bytes = File.ReadAllBytes(path);
        // 保留 SQLite 檔頭，破壞其餘頁面
        for (var i = 100; i < bytes.Length; i += 7)
            bytes[i] ^= 0x5A;
        File.WriteAllBytes(path, bytes);
        var hash = FileHash(path);

        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(path, Time));

        Assert.Contains("損毀", ex.Message);
        Assert.Equal(hash, FileHash(path));
    }

    [Fact]
    public void Truncated_file_is_rejected()
    {
        var path = NewPath();
        TournamentFile.Create(path, SampleSwiss()).Dispose();
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 3)]);

        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(path, Time));

        Assert.Contains("損毀", ex.Message);
    }

    [Fact]
    public void Missing_file_has_readable_message()
    {
        var ex = Assert.Throws<StorageException>(() => TournamentFile.Open(NewPath(), Time));

        Assert.StartsWith("找不到檔案", ex.Message);
    }

    [Fact]
    public void Old_version_is_backed_up_then_upgraded()
    {
        var path = NewPath();
        TournamentFile.Create(path, SampleSwiss()).Dispose();
        // 還原成第 1 版的樣子：移除第 2 版加入的表並把版本設回 1
        RawSql(path, "DROP TABLE file_info; PRAGMA user_version = 1;");
        var original = File.ReadAllBytes(path);

        var (file, state) = TournamentFile.Open(path, Time);
        file.Dispose();

        var backup = path + ".v1.bak";
        Assert.True(File.Exists(backup));
        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.Equal(FileCheckKind.Ok, TournamentFileInspector.Inspect(path).Kind);
        Assert.Equal(StateHasher.Compute(SampleSwiss()), StateHasher.Compute(state));
    }

    [Fact]
    public void Second_upgrade_backup_name_uses_injected_clock()
    {
        var path = NewPath();
        TournamentFile.Create(path, SampleSwiss()).Dispose();
        RawSql(path, "DROP TABLE file_info; PRAGMA user_version = 1;");
        File.WriteAllText(path + ".v1.bak", "先前的備份"); // 名稱已被占用

        TournamentFile.Open(path, Time).File.Dispose();

        Assert.Equal("先前的備份", File.ReadAllText(path + ".v1.bak"));
        Assert.True(File.Exists(path + ".v1.20261001010000.bak"));
    }

    [Fact]
    public void Failed_commit_rolls_back_entirely()
    {
        var path = NewPath();
        var state = SampleSwiss();
        using var file = TournamentFile.Create(path, state);
        var p1 = state.Players[1];
        var p2 = state.Players[2];
        // 兩位選手同一註冊編號 → UNIQUE 違規；同一交易中的設定變更也必須一起撤銷
        var broken = state.WithPlayer(p2 with { RegNo = p1.RegNo }) with { Settings = state.Settings with { Name = "改名" } };

        Assert.Throws<StorageException>(() => file.Commit(state, broken, [new AuditEntry(DateTimeOffset.UnixEpoch, "x", "y", null, null)]));

        var (reopened, loaded) = TournamentFile.Open(path, Time);
        using (reopened)
        {
            Assert.Equal(StateHasher.Compute(state), StateHasher.Compute(loaded));
            Assert.Empty(reopened.ReadAudit());
        }
    }

    [Fact]
    public void Completing_a_round_creates_backup()
    {
        var path = NewPath();
        var initial = SwissFlow.NewSwiss(4).NextRound();
        using var file = TournamentFile.Create(path, initial);
        var session = new TournamentSession(initial, file, new ManualTimeProvider());

        session.Execute(new SetResult(1, 1, GameResult.WhiteWin));
        Assert.False(File.Exists(path + ".r1.bak"));
        session.Execute(new SetResult(1, 2, GameResult.Draw));

        Assert.True(File.Exists(path + ".r1.bak"));
        var (backup, state) = TournamentFile.Open(path + ".r1.bak", Time);
        backup.Dispose();
        Assert.True(state.GetRound(1).IsCompleted);
    }

    [Fact]
    public void Completing_a_knockout_round_creates_backup()
    {
        var path = NewPath();
        var initial = TournamentState.Create(TournamentSettings.Create("KO", TournamentFormat.SingleElimination))
            .Then(new AddPlayer("A", 3)).Then(new AddPlayer("B", 2)).Then(new AddPlayer("C", 1)).Then(new StartKnockout());
        using var file = TournamentFile.Create(path, initial);
        var session = new TournamentSession(initial, file, new ManualTimeProvider());

        session.Execute(new RecordKnockoutWinner(2, 2));

        Assert.True(File.Exists(path + ".ko-r1.bak"));
    }

    /// <summary>驗收 9：在任何時間點取消配對，賽事檔內容與取消前逐位元組一致。</summary>
    [Fact]
    public void Cancelled_pairing_leaves_file_byte_identical()
    {
        var path = NewPath();
        var (history, round) = SwissHistoryGenerator.Generate(new Random(9), 60, 3);
        var state = history with { Settings = history.Settings with { SwissRounds = 10 } };
        using var file = TournamentFile.Create(path, state);
        var session = new TournamentSession(state, file, new ManualTimeProvider());
        var before = FileHash(path);
        var points = 0;
        SwissRoundPlanner.Propose(session.State, round, new CallbackProgress(_ => points++));

        // 在每一個進度回報點（完成之前）取消
        for (var cancelAfter = 0; cancelAfter < points - 1; cancelAfter++)
        {
            using var cts = new CancellationTokenSource();
            var reports = 0;
            var stopAt = cancelAfter;
            using (session.BeginBackgroundWork("配對中"))
            {
                Assert.ThrowsAny<OperationCanceledException>(() => SwissRoundPlanner.Propose(session.State, round,
                    new CallbackProgress(_ =>
                    {
                        if (reports++ == stopAt)
                            cts.Cancel();
                    }), cts.Token));
            }

            Assert.Equal(before, FileHash(path));
        }
    }

    private sealed class CallbackProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
