using System.Security.Cryptography;
using System.Text;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Hashing;

/// <summary>
/// 整個賽事狀態的正規化雜湊（SHA-256，十六進位）。用於驗證復原的正確性與配對方案指紋。
/// 新增狀態欄位時必須同步更新這裡（<c>StateHasherTests</c> 有欄位數量守衛）。
/// </summary>
public static class StateHasher
{
    private const byte FormatVersion = 1;

    public static string Compute(TournamentState state)
    {
        using var w = new CanonicalWriter();
        w.Byte(FormatVersion);
        WriteSettings(w, state.Settings);
        w.Int(state.NextPlayerId);
        w.Int(state.NextRegNo);
        w.Int(state.Players.Count);
        foreach (var p in state.Players.Values) // ImmutableSortedDictionary：依內部 Id 排序
            WritePlayer(w, p);
        w.Int(state.Rounds.Length);
        foreach (var r in state.Rounds)
            WriteRound(w, r);
        w.Bool(state.Bracket is not null);
        if (state.Bracket is not null)
            WriteBracket(w, state.Bracket);
        return w.Finish();
    }

    /// <summary>配對方案指紋（不含成績）。</summary>
    public static string Compute(Pairing pairing)
    {
        using var w = new CanonicalWriter();
        WritePairing(w, pairing);
        return w.Finish();
    }

    private static void WriteSettings(CanonicalWriter w, TournamentSettings s)
    {
        w.String(s.Name);
        w.Int((int)s.Format);
        w.Int(s.SwissRounds);
        w.Int(s.ByePoints);
        w.Bool(s.ThirdPlaceMatch);
    }

    private static void WritePlayer(CanonicalWriter w, Player p)
    {
        w.Int(p.Id);
        w.Int(p.RegNo);
        w.String(p.Name);
        w.Int(p.Rating);
        w.NullableInt(p.JoinRound);
        w.NullableInt(p.WithdrawRound);
    }

    private static void WriteRound(CanonicalWriter w, Round r)
    {
        w.Int(r.Number);
        w.Int((int)r.Status);
        w.NullableInt(r.Bye);
        w.Bool(r.ForcedOverride);
        w.Int(r.Games.Length);
        foreach (var g in r.Games)
        {
            w.Int(g.White);
            w.Int(g.Black);
            w.Int((int)g.Result);
        }

        w.Bool(r.EngineProposal is not null);
        if (r.EngineProposal is not null)
            WritePairing(w, r.EngineProposal);
    }

    private static void WriteBracket(CanonicalWriter w, Bracket b)
    {
        w.Int(b.Size);
        w.Int(b.Seeds.Length);
        foreach (var id in b.Seeds)
            w.Int(id);
        w.Int(b.Matches.Length);
        foreach (var m in b.Matches)
        {
            w.Int(m.Id);
            w.Int(m.Round);
            w.Int(m.Position);
            w.Int((int)m.A.Kind);
            w.Int(m.A.Value);
            w.Int((int)m.B.Kind);
            w.Int(m.B.Value);
            w.Bool(m.IsThirdPlace);
            w.Bool(m.Outcome is not null);
            if (m.Outcome is not null)
            {
                w.Int(m.Outcome.Winner);
                w.Int((int)m.Outcome.Kind);
            }
        }
    }

    private static void WritePairing(CanonicalWriter w, Pairing p)
    {
        w.NullableInt(p.Bye);
        w.Int(p.Games.Length);
        foreach (var g in p.Games)
        {
            w.Int(g.White);
            w.Int(g.Black);
        }
    }

    private sealed class CanonicalWriter : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[8];

        public void Byte(byte value) => _hash.AppendData([value]);

        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void Int(int value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(_buffer, value);
            _hash.AppendData(_buffer, 0, 4);
        }

        public void NullableInt(int? value)
        {
            Bool(value.HasValue);
            if (value.HasValue)
                Int(value.Value);
        }

        public void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Int(bytes.Length);
            _hash.AppendData(bytes);
        }

        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());

        public void Dispose() => _hash.Dispose();
    }
}
