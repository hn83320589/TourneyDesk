using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Standings;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.Reference;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Swiss;

/// <summary>samples/swiss-12：12 人 5 輪（遲到加入、退賽、輪空、棄權），每輪配對與最終積分榜皆與手算一致。</summary>
public class SwissSampleTests
{
    private sealed class Sample
    {
        public required string Name { get; init; }
        public int SwissRounds { get; init; }
        public int ByePoints { get; init; }
        public required List<ScenarioPlayer> Players { get; init; }
        public required List<List<string>> Results { get; init; }
    }

    private static string SamplePath(string file) => Path.Combine(AppContext.BaseDirectory, "samples", "swiss-12", file);

    private static (Sample Sample, TournamentState Initial) Load()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };
        var sample = JsonSerializer.Deserialize<Sample>(File.ReadAllText(SamplePath("scenario.json")), options)!;
        var players = sample.Players.ToImmutableSortedDictionary(p => p.RegNo,
            p => new Player(p.RegNo, p.RegNo, p.Name, p.Rating, p.JoinRound, p.WithdrawRound));
        var settings = TournamentSettings.Create(sample.Name, TournamentFormat.Swiss, sample.SwissRounds, sample.ByePoints);
        return (sample, new TournamentState(settings, players, [], players.Count + 1, players.Count + 1));
    }

    /// <summary>依序以引擎配對並登錄結果，回傳每輪配對（註冊編號）與最終狀態。</summary>
    private static (List<string> Lines, TournamentState Final, List<(TournamentState Before, int Round)> Snapshots) Play()
    {
        var (sample, state) = Load();
        var lines = new List<string>();
        var snapshots = new List<(TournamentState, int)>();
        for (var round = 1; round <= sample.Results.Count; round++)
        {
            snapshots.Add((state, round));
            var pairing = SwissPairingEngine.Pair(SwissRoundInput.FromState(state, round)).Pairing;
            for (var b = 0; b < pairing.Games.Length; b++)
                lines.Add($"{round} {b + 1} {pairing.Games[b].White} {pairing.Games[b].Black}");
            if (pairing.Bye is { } bye)
                lines.Add($"{round} bye {bye}");

            var results = sample.Results[round - 1];
            Assert.Equal(pairing.Games.Length, results.Count);
            var games = pairing.Games.Select((g, i) => new Game(g.White, g.Black, ScenarioFile.ParseResult(results[i])));
            state = state.WithRound(new Round(round, RoundStatus.Published, [.. games], pairing.Bye, pairing));
        }

        return (lines, state, snapshots);
    }

    [Fact]
    public void Every_round_matches_hand_calculated_pairings()
    {
        var expected = File.ReadAllLines(SamplePath("expected-pairings.txt")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

        Assert.Equal(expected, Play().Lines);
    }

    [Fact]
    public void Every_round_also_matches_brute_force_reference()
    {
        // 窮舉參考解只支援 N ≤ 12；第 3 輪有 13 人（M 加入），由手算預期檔驗證
        foreach (var (before, round) in Play().Snapshots.Where(s => ReferenceRoundData.From(s.Before, s.Round).Ranked.Length <= 12))
        {
            var reference = BruteForceSwissPairing.Solve(ReferenceRoundData.From(before, round));
            var engine = SwissPairingEngine.Pair(SwissRoundInput.FromState(before, round)).Pairing;

            Assert.Equal(reference.ByeRegNo, engine.Bye);
            Assert.Equal(reference.Games.OrderBy(x => x), engine.Games.Select(g => (g.White, g.Black)).OrderBy(x => x));
        }
    }

    [Fact]
    public void Final_standings_match_hand_calculation()
    {
        var expected = File.ReadAllLines(SamplePath("expected-standings.csv")).Skip(1).Where(l => l.Length > 0).ToList();

        var rows = StandingsCalculator.Compute(Play().Final);

        Assert.Equal(expected, rows.Select(r => string.Join(',',
            r.Rank, r.RegNo, r.Name, r.Score, r.DirectEncounter?.ToString(CultureInfo.InvariantCulture) ?? "",
            r.BuchholzCut1, r.Buchholz, r.SonnebornBerger, r.Wins, r.Withdrawn ? "true" : "false")).ToList());
    }
}
