using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Data;

/// <summary>從資料庫讀出完整狀態與異動紀錄。</summary>
internal static class StateReader
{
    public static TournamentState Read(SqliteConnection c)
    {
        TournamentSettings? settings = null;
        int nextPlayerId = 0, nextRegNo = 0;
        ForEach(c, "SELECT name, format, swiss_rounds, bye_points, third_place, next_player_id, next_reg_no FROM settings WHERE id = 1", r =>
        {
            settings = new TournamentSettings(r.GetString(0), (TournamentFormat)r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4) != 0);
            nextPlayerId = r.GetInt32(5);
            nextRegNo = r.GetInt32(6);
        });
        if (settings is null)
            throw new InvalidDataException("缺少賽事設定。");
        settings.Validate();

        var players = ImmutableSortedDictionary.CreateBuilder<int, Player>();
        ForEach(c, "SELECT id, reg_no, name, rating, join_round, withdraw_round FROM players ORDER BY id", r =>
        {
            var p = new Player(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), NullableInt(r, 4), NullableInt(r, 5));
            players.Add(p.Id, p);
        });

        var games = ReadGames(c, "SELECT round, board, white, black, result FROM games ORDER BY round, board", withResult: true);
        var proposals = ReadGames(c, "SELECT round, board, white, black FROM proposal_games ORDER BY round, board", withResult: false);
        var rounds = ImmutableArray.CreateBuilder<Round>();
        ForEach(c, "SELECT number, status, bye, forced_override, has_proposal, proposal_bye FROM rounds ORDER BY number", r =>
        {
            var n = r.GetInt32(0);
            if (n != rounds.Count + 1)
                throw new InvalidDataException($"輪次編號不連續（第 {n} 輪）。");
            Pairing? proposal = r.GetInt32(4) != 0
                ? new Pairing([.. proposals.GetValueOrDefault(n, []).Select(g => new PairedGame(g.White, g.Black))], NullableInt(r, 5))
                : null;
            rounds.Add(new Round(n, (RoundStatus)r.GetInt32(1), [.. games.GetValueOrDefault(n, [])], NullableInt(r, 2), proposal, r.GetInt32(3) != 0));
        });

        return new TournamentState(settings, players.ToImmutable(), rounds.ToImmutable(), nextPlayerId, nextRegNo, ReadBracket(c));
    }

    public static IReadOnlyList<AuditEntry> ReadAudit(SqliteConnection c)
    {
        var list = new List<AuditEntry>();
        ForEach(c, "SELECT timestamp, kind, target, before, after FROM audit_log ORDER BY seq", r =>
            list.Add(new AuditEntry(
                DateTimeOffset.Parse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                r.GetString(1), r.GetString(2), NullableString(r, 3), NullableString(r, 4))));
        return list;
    }

    private static Bracket? ReadBracket(SqliteConnection c)
    {
        int? size = null;
        ForEach(c, "SELECT size FROM bracket WHERE id = 1", r => size = r.GetInt32(0));
        if (size is null)
            return null;

        var seeds = ImmutableArray.CreateBuilder<int>();
        ForEach(c, "SELECT seed, player_id FROM bracket_seeds ORDER BY seed", r => seeds.Add(r.GetInt32(1)));

        var matches = ImmutableArray.CreateBuilder<KnockoutMatch>();
        ForEach(c, "SELECT id, round, position, a_kind, a_value, b_kind, b_value, third_place, winner, outcome_kind FROM bracket_matches ORDER BY id", r =>
        {
            KnockoutOutcome? outcome = r.IsDBNull(8) ? null : new KnockoutOutcome(r.GetInt32(8), (KnockoutOutcomeKind)r.GetInt32(9));
            matches.Add(new KnockoutMatch(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                new KnockoutSource((KnockoutSourceKind)r.GetInt32(3), r.GetInt32(4)),
                new KnockoutSource((KnockoutSourceKind)r.GetInt32(5), r.GetInt32(6)),
                outcome, r.GetInt32(7) != 0));
        });

        return new Bracket(size.Value, seeds.ToImmutable(), matches.ToImmutable());
    }

    private static Dictionary<int, List<Game>> ReadGames(SqliteConnection c, string sql, bool withResult)
    {
        var result = new Dictionary<int, List<Game>>();
        ForEach(c, sql, r =>
        {
            var round = r.GetInt32(0);
            if (!result.TryGetValue(round, out var list))
                result[round] = list = [];
            if (r.GetInt32(1) != list.Count + 1)
                throw new InvalidDataException($"第 {round} 輪桌號不連續。");
            list.Add(new Game(r.GetInt32(2), r.GetInt32(3), withResult ? (GameResult)r.GetInt32(4) : GameResult.None));
        });
        return result;
    }

    private static void ForEach(SqliteConnection c, string sql, Action<SqliteDataReader> row)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            row(reader);
    }

    private static int? NullableInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
