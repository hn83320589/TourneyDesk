using System.Collections.Immutable;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Standings;

namespace TourneyDesk.Core.Swiss;

public enum PieceColor
{
    White,
    Black,
}

/// <summary>一位可配對選手在本輪開始前的狀態。</summary>
/// <param name="Score">賽前累計分數（半分）。</param>
/// <param name="HadBye">先前是否輪空過（因棄權獲勝不算輪空）。</param>
/// <param name="WhiteCount">先前排定為先手的場次數（含棄權場次，不含輪空）。</param>
/// <param name="LastColor">最近一場排定對局的先後手；沒有對局過為 null。</param>
public sealed record SwissCandidate(
    int Id,
    int RegNo,
    int Rating,
    int Score,
    bool HadBye,
    int WhiteCount,
    PieceColor? LastColor);

/// <summary>瑞士制單輪配對的完整輸入（與 <see cref="TournamentState"/> 無關，方便測試）。</summary>
public sealed class SwissRoundInput
{
    private readonly HashSet<(int, int)> _metPairs;

    public SwissRoundInput(IEnumerable<SwissCandidate> players, IEnumerable<(int A, int B)> metPairs)
    {
        // 賽前排名：累計分數高→低、積分高→低、註冊編號小→大
        Ranked = [.. players
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Rating)
            .ThenBy(p => p.RegNo)];
        if (Ranked.Select(p => p.Id).Distinct().Count() != Ranked.Length)
            throw new ArgumentException("選手 Id 重複。", nameof(players));
        if (Ranked.Select(p => p.RegNo).Distinct().Count() != Ranked.Length)
            throw new ArgumentException("註冊編號重複。", nameof(players));
        _metPairs = metPairs.Select(p => Key(p.A, p.B)).ToHashSet();
        RankOf = Ranked.Select((p, i) => (p.Id, Rank: i + 1)).ToImmutableDictionary(x => x.Id, x => x.Rank);
    }

    /// <summary>依賽前排名排序的選手（索引 0 = 第 1 名）。</summary>
    public ImmutableArray<SwissCandidate> Ranked { get; }

    /// <summary>選手 Id → 賽前排名（1 起算）。</summary>
    public ImmutableDictionary<int, int> RankOf { get; }

    public int Count => Ranked.Length;

    public bool HaveMet(int idA, int idB) => _metPairs.Contains(Key(idA, idB));

    /// <summary>
    /// 輸入指紋：涵蓋引擎用到的全部資訊（依排名的選手及其分數、積分、輪空紀錄、先後手狀態，
    /// 以及參賽者之間的交手紀錄）。引擎結果完全由輸入決定，因此可作為快取鍵。
    /// </summary>
    public string Fingerprint => _fingerprint ??= ComputeFingerprint();

    private string? _fingerprint;

    private string ComputeFingerprint()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in Ranked)
            sb.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"{p.Id},{p.RegNo},{p.Rating},{p.Score},{(p.HadBye ? 1 : 0)},{p.WhiteCount},{(int?)p.LastColor ?? -1};");
        sb.Append('|');
        var ids = Ranked.Select(p => p.Id).ToHashSet();
        foreach (var (a, b) in _metPairs.Where(m => ids.Contains(m.Item1) && ids.Contains(m.Item2)).Order())
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{a}-{b};");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public SwissCandidate Get(int id) => Ranked[RankOf[id] - 1];

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>從賽事狀態建立第 <paramref name="round"/> 輪的配對輸入（只看較前面的已發布輪次）。</summary>
    public static SwissRoundInput FromState(TournamentState state, int round)
    {
        var sheet = ScoreSheet.Build(state, beforeRound: round);
        var previous = state.Rounds.Where(r => r.Number < round && r.IsPublished).ToList();

        var candidates = state.Players.Values
            .Where(p => p.IsActiveIn(round))
            .Select(p =>
            {
                var whites = 0;
                PieceColor? last = null;
                var hadBye = false;
                foreach (var r in previous)
                {
                    if (r.Bye == p.Id)
                        hadBye = true;
                    if (r.GameIndexOf(p.Id) is { } gi)
                    {
                        var isWhite = r.Games[gi].White == p.Id;
                        if (isWhite)
                            whites++;
                        last = isWhite ? PieceColor.White : PieceColor.Black;
                    }
                }

                return new SwissCandidate(p.Id, p.RegNo, p.Rating, sheet.Total(p.Id), hadBye, whites, last);
            });

        var met = previous.SelectMany(r => r.Games).Select(g => (g.White, g.Black));
        return new SwissRoundInput(candidates, met);
    }
}
