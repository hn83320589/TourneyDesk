namespace TourneyDesk.Data;

/// <summary>
/// .tourney 檔（SQLite）的 schema。以 PRAGMA application_id 辨識本程式的檔案，PRAGMA user_version 為 schema 版本。
/// 新版本一律以「遷移」加入，舊檔開啟時先備份再依序升級。
/// </summary>
internal static class Schema
{
    /// <summary>'TDSH'</summary>
    public const int ApplicationId = 0x54445348;

    public const int CurrentVersion = 2;

    /// <summary>第 1 版（初始 schema）。</summary>
    public const string V1 = """
        CREATE TABLE settings (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            name TEXT NOT NULL,
            format INTEGER NOT NULL,
            swiss_rounds INTEGER NOT NULL,
            bye_points INTEGER NOT NULL,
            third_place INTEGER NOT NULL,
            next_player_id INTEGER NOT NULL,
            next_reg_no INTEGER NOT NULL
        );
        CREATE TABLE players (
            id INTEGER PRIMARY KEY,
            reg_no INTEGER NOT NULL UNIQUE,
            name TEXT NOT NULL,
            rating INTEGER NOT NULL CHECK (rating BETWEEN 0 AND 3000),
            join_round INTEGER,
            withdraw_round INTEGER
        );
        CREATE TABLE rounds (
            number INTEGER PRIMARY KEY,
            status INTEGER NOT NULL,
            bye INTEGER,
            forced_override INTEGER NOT NULL,
            has_proposal INTEGER NOT NULL,
            proposal_bye INTEGER
        );
        CREATE TABLE games (
            round INTEGER NOT NULL,
            board INTEGER NOT NULL,
            white INTEGER NOT NULL,
            black INTEGER NOT NULL,
            result INTEGER NOT NULL,
            PRIMARY KEY (round, board)
        );
        CREATE TABLE proposal_games (
            round INTEGER NOT NULL,
            board INTEGER NOT NULL,
            white INTEGER NOT NULL,
            black INTEGER NOT NULL,
            PRIMARY KEY (round, board)
        );
        CREATE TABLE bracket (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            size INTEGER NOT NULL
        );
        CREATE TABLE bracket_seeds (
            seed INTEGER PRIMARY KEY,
            player_id INTEGER NOT NULL
        );
        CREATE TABLE bracket_matches (
            id INTEGER PRIMARY KEY,
            round INTEGER NOT NULL,
            position INTEGER NOT NULL,
            a_kind INTEGER NOT NULL,
            a_value INTEGER NOT NULL,
            b_kind INTEGER NOT NULL,
            b_value INTEGER NOT NULL,
            third_place INTEGER NOT NULL,
            winner INTEGER,
            outcome_kind INTEGER
        );
        CREATE TABLE audit_log (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            kind TEXT NOT NULL,
            target TEXT NOT NULL,
            before TEXT,
            after TEXT
        );
        CREATE TRIGGER audit_log_no_update BEFORE UPDATE ON audit_log
        BEGIN SELECT RAISE(ABORT, 'audit_log 只能新增'); END;
        CREATE TRIGGER audit_log_no_delete BEFORE DELETE ON audit_log
        BEGIN SELECT RAISE(ABORT, 'audit_log 只能新增'); END;
        """;

    /// <summary>第 1 → 2 版：加入檔案資訊表（記錄建立與最後寫入的程式版本）。</summary>
    public const string V1ToV2 = """
        CREATE TABLE file_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        INSERT INTO file_info (key, value) VALUES ('created_by', 'TourneyDesk schema 1');
        """;

    /// <summary>依序套用的遷移：索引 i 為「第 i+1 版 → 第 i+2 版」。</summary>
    public static readonly string[] Migrations = [V1ToV2];
}
