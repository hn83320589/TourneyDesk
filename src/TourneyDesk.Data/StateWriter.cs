using Microsoft.Data.Sqlite;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Data;

/// <summary>
/// 把狀態差異寫入資料庫（呼叫端負責交易）。<paramref name="before"/> 為 null 時寫入全部內容。
/// 不可變狀態之間大多共用參考，因此以參考相等快速略過未變動的部分。
/// </summary>
internal static class StateWriter
{
    public static void Write(SqliteConnection c, SqliteTransaction tx, TournamentState? before, TournamentState after)
    {
        if (before is null || before.Settings != after.Settings
            || before.NextPlayerId != after.NextPlayerId || before.NextRegNo != after.NextRegNo)
        {
            WriteSettings(c, tx, after);
        }

        WritePlayers(c, tx, before, after);
        WriteRounds(c, tx, before, after);
        WriteBracket(c, tx, before?.Bracket, after.Bracket);
    }

    private static void WriteSettings(SqliteConnection c, SqliteTransaction tx, TournamentState s) =>
        Exec(c, tx, """
            INSERT OR REPLACE INTO settings (id, name, format, swiss_rounds, bye_points, third_place, next_player_id, next_reg_no)
            VALUES (1, $name, $format, $rounds, $bye, $third, $nextId, $nextReg)
            """,
            ("$name", s.Settings.Name), ("$format", (int)s.Settings.Format), ("$rounds", s.Settings.SwissRounds),
            ("$bye", s.Settings.ByePoints), ("$third", s.Settings.ThirdPlaceMatch ? 1 : 0),
            ("$nextId", s.NextPlayerId), ("$nextReg", s.NextRegNo));

    private static void WritePlayers(SqliteConnection c, SqliteTransaction tx, TournamentState? before, TournamentState after)
    {
        if (before is not null && ReferenceEquals(before.Players, after.Players))
            return;

        if (before is not null)
            foreach (var id in before.Players.Keys.Where(id => !after.Players.ContainsKey(id)))
                Exec(c, tx, "DELETE FROM players WHERE id = $id", ("$id", id));

        foreach (var p in after.Players.Values)
        {
            if (before is not null && before.Players.TryGetValue(p.Id, out var old) && old == p)
                continue;
            Exec(c, tx, """
                INSERT INTO players (id, reg_no, name, rating, join_round, withdraw_round)
                VALUES ($id, $reg, $name, $rating, $join, $withdraw)
                ON CONFLICT (id) DO UPDATE SET
                    reg_no = excluded.reg_no, name = excluded.name, rating = excluded.rating,
                    join_round = excluded.join_round, withdraw_round = excluded.withdraw_round
                """,
                ("$id", p.Id), ("$reg", p.RegNo), ("$name", p.Name), ("$rating", p.Rating),
                ("$join", p.JoinRound), ("$withdraw", p.WithdrawRound));
        }
    }

    private static void WriteRounds(SqliteConnection c, SqliteTransaction tx, TournamentState? before, TournamentState after)
    {
        var oldRounds = before?.Rounds ?? [];
        if (before is not null && oldRounds == after.Rounds)
            return;

        var max = Math.Max(oldRounds.Length, after.Rounds.Length);
        for (var n = 1; n <= max; n++)
        {
            var old = n <= oldRounds.Length ? oldRounds[n - 1] : null;
            var now = n <= after.Rounds.Length ? after.Rounds[n - 1] : null;
            if (ReferenceEquals(old, now))
                continue;

            if (now is null)
            {
                DeleteRound(c, tx, n);
            }
            else if (old is not null && OnlyResultsDiffer(old, now))
            {
                for (var b = 0; b < now.Games.Length; b++)
                {
                    if (old.Games[b].Result != now.Games[b].Result)
                        Exec(c, tx, "UPDATE games SET result = $r WHERE round = $n AND board = $b",
                            ("$r", (int)now.Games[b].Result), ("$n", n), ("$b", b + 1));
                }
            }
            else
            {
                if (old is not null)
                    DeleteRound(c, tx, n);
                InsertRound(c, tx, now);
            }
        }
    }

    private static bool OnlyResultsDiffer(Round a, Round b) =>
        a.Status == b.Status && a.Bye == b.Bye && a.ForcedOverride == b.ForcedOverride
        && SameProposal(a.EngineProposal, b.EngineProposal)
        && a.Games.Length == b.Games.Length
        && a.Games.Zip(b.Games).All(p => p.First.White == p.Second.White && p.First.Black == p.Second.Black);

    private static bool SameProposal(Pairing? a, Pairing? b) =>
        a is null ? b is null : b is not null && a.SameAs(b);

    private static void DeleteRound(SqliteConnection c, SqliteTransaction tx, int n)
    {
        Exec(c, tx, "DELETE FROM rounds WHERE number = $n", ("$n", n));
        Exec(c, tx, "DELETE FROM games WHERE round = $n", ("$n", n));
        Exec(c, tx, "DELETE FROM proposal_games WHERE round = $n", ("$n", n));
    }

    private static void InsertRound(SqliteConnection c, SqliteTransaction tx, Round r)
    {
        Exec(c, tx, """
            INSERT INTO rounds (number, status, bye, forced_override, has_proposal, proposal_bye)
            VALUES ($n, $status, $bye, $forced, $hasProposal, $proposalBye)
            """,
            ("$n", r.Number), ("$status", (int)r.Status), ("$bye", r.Bye), ("$forced", r.ForcedOverride ? 1 : 0),
            ("$hasProposal", r.EngineProposal is null ? 0 : 1), ("$proposalBye", r.EngineProposal?.Bye));

        for (var b = 0; b < r.Games.Length; b++)
        {
            var g = r.Games[b];
            Exec(c, tx, "INSERT INTO games (round, board, white, black, result) VALUES ($n, $b, $w, $k, $r)",
                ("$n", r.Number), ("$b", b + 1), ("$w", g.White), ("$k", g.Black), ("$r", (int)g.Result));
        }

        if (r.EngineProposal is { } p)
        {
            for (var b = 0; b < p.Games.Length; b++)
                Exec(c, tx, "INSERT INTO proposal_games (round, board, white, black) VALUES ($n, $b, $w, $k)",
                    ("$n", r.Number), ("$b", b + 1), ("$w", p.Games[b].White), ("$k", p.Games[b].Black));
        }
    }

    private static void WriteBracket(SqliteConnection c, SqliteTransaction tx, Bracket? before, Bracket? after)
    {
        if (ReferenceEquals(before, after))
            return;

        var rewrite = before is null || after is null || before.Size != after.Size
            || !before.Seeds.SequenceEqual(after.Seeds) || before.Matches.Length != after.Matches.Length;
        if (rewrite)
        {
            Exec(c, tx, "DELETE FROM bracket");
            Exec(c, tx, "DELETE FROM bracket_seeds");
            Exec(c, tx, "DELETE FROM bracket_matches");
            if (after is null)
                return;

            Exec(c, tx, "INSERT INTO bracket (id, size) VALUES (1, $size)", ("$size", after.Size));
            for (var i = 0; i < after.Seeds.Length; i++)
                Exec(c, tx, "INSERT INTO bracket_seeds (seed, player_id) VALUES ($s, $p)", ("$s", i + 1), ("$p", after.Seeds[i]));
            foreach (var m in after.Matches)
                InsertMatch(c, tx, m);
            return;
        }

        for (var i = 0; i < after!.Matches.Length; i++)
        {
            var (old, now) = (before!.Matches[i], after.Matches[i]);
            if (old == now)
                continue;
            Exec(c, tx, "DELETE FROM bracket_matches WHERE id = $id", ("$id", now.Id));
            InsertMatch(c, tx, now);
        }
    }

    private static void InsertMatch(SqliteConnection c, SqliteTransaction tx, KnockoutMatch m) =>
        Exec(c, tx, """
            INSERT INTO bracket_matches (id, round, position, a_kind, a_value, b_kind, b_value, third_place, winner, outcome_kind)
            VALUES ($id, $round, $pos, $ak, $av, $bk, $bv, $third, $winner, $kind)
            """,
            ("$id", m.Id), ("$round", m.Round), ("$pos", m.Position), ("$ak", (int)m.A.Kind), ("$av", m.A.Value),
            ("$bk", (int)m.B.Kind), ("$bv", m.B.Value), ("$third", m.IsThirdPlace ? 1 : 0),
            ("$winner", m.Outcome?.Winner), ("$kind", m.Outcome is null ? null : (int?)m.Outcome.Kind));

    internal static void Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
