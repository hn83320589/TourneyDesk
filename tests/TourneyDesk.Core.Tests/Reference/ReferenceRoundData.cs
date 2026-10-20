using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Tests.Reference;

/// <summary>
/// 參考解自行從賽事原始資料整理出的配對輸入。
/// 刻意不使用正式程式碼的任何計算（Standings、Swiss、GameResult 擴充方法等），只讀取資料欄位，
/// 讓參考解與正式實作互相獨立。
/// </summary>
internal sealed class ReferenceRoundData
{
    public required RefPlayer[] Ranked { get; init; }
    public required HashSet<(int, int)> Met { get; init; }

    public sealed record RefPlayer(int Id, int RegNo, int Rating, int Score, bool HadBye, int Whites, bool? LastWasWhite);

    public bool HaveMet(int a, int b) => Met.Contains(a < b ? (a, b) : (b, a));

    public static ReferenceRoundData From(TournamentState state, int round)
    {
        var previous = state.Rounds.Where(r => r.Number < round && r.Status == RoundStatus.Published).ToList();
        var players = new List<RefPlayer>();
        foreach (var p in state.Players.Values)
        {
            var first = p.JoinRound ?? 1;
            var active = round >= first && (p.WithdrawRound is null || round < p.WithdrawRound.Value);
            if (!active)
                continue;

            var score = 0;
            var hadBye = false;
            var whites = 0;
            bool? lastWhite = null;
            foreach (var r in previous)
            {
                if (r.Bye == p.Id)
                {
                    hadBye = true;
                    score += state.Settings.ByePoints;
                }

                foreach (var g in r.Games)
                {
                    if (g.White == p.Id)
                    {
                        whites++;
                        lastWhite = true;
                        score += Points(g.Result, white: true);
                    }
                    else if (g.Black == p.Id)
                    {
                        lastWhite = false;
                        score += Points(g.Result, white: false);
                    }
                }
            }

            players.Add(new RefPlayer(p.Id, p.RegNo, p.Rating, score, hadBye, whites, lastWhite));
        }

        var ranked = players.ToArray();
        Array.Sort(ranked, (x, y) =>
            x.Score != y.Score ? y.Score.CompareTo(x.Score)
            : x.Rating != y.Rating ? y.Rating.CompareTo(x.Rating)
            : x.RegNo.CompareTo(y.RegNo));

        var met = new HashSet<(int, int)>();
        foreach (var g in previous.SelectMany(r => r.Games))
            met.Add(g.White < g.Black ? (g.White, g.Black) : (g.Black, g.White));

        return new ReferenceRoundData { Ranked = ranked, Met = met };
    }

    private static int Points(GameResult r, bool white) => (r, white) switch
    {
        (GameResult.WhiteWin, true) or (GameResult.BlackForfeit, true) => 2,
        (GameResult.BlackWin, false) or (GameResult.WhiteForfeit, false) => 2,
        (GameResult.Draw, _) => 1,
        _ => 0,
    };
}
