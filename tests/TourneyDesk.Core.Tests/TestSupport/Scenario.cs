using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Tests.TestSupport;

/// <summary>
/// 範例賽事檔（samples/*.json）格式。對局以註冊編號表示：「先手 後手 結果」，
/// 結果為 1-0、=、0-1、-/+（先手棄權）、+/-（後手棄權）、-/-（雙方棄權）、?（未登錄）。
/// </summary>
internal sealed class ScenarioFile
{
    public required string Name { get; init; }
    public required TournamentFormat Format { get; init; }
    public int SwissRounds { get; init; } = 5;
    public int? ByePoints { get; init; }
    public bool ThirdPlaceMatch { get; init; }
    public required List<ScenarioPlayer> Players { get; init; }
    public List<ScenarioRound> Rounds { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ScenarioFile Load(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", relativePath);
        return JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"無法讀取 {path}");
    }

    /// <summary>建立狀態；<paramref name="idOf"/> 可把註冊編號對應到任意內部 Id（決定性測試用）。</summary>
    public TournamentState ToState(Func<int, int>? idOf = null)
    {
        idOf ??= regNo => regNo;
        var settings = TournamentSettings.Create(Name, Format, SwissRounds, ByePoints, ThirdPlaceMatch);
        var players = Players.ToImmutableSortedDictionary(
            p => idOf(p.RegNo),
            p => new Player(idOf(p.RegNo), p.RegNo, p.Name, p.Rating, p.JoinRound, p.WithdrawRound));
        var rounds = Rounds.Select((r, i) => new Round(
            i + 1,
            r.Draft ? RoundStatus.Draft : RoundStatus.Published,
            [.. r.Games.Select(g => ParseGame(g, idOf))],
            r.Bye is { } bye ? idOf(bye) : null)).ToImmutableArray();
        return new TournamentState(settings, players, rounds,
            players.Keys.DefaultIfEmpty(0).Max() + 1, Players.Max(p => p.RegNo) + 1);
    }

    private static Game ParseGame(string text, Func<int, int> idOf)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            throw new FormatException($"對局格式應為「先手 後手 結果」：{text}");
        return new Game(idOf(int.Parse(parts[0])), idOf(int.Parse(parts[1])), ParseResult(parts[2]));
    }

    public static GameResult ParseResult(string token) => token switch
    {
        "1-0" => GameResult.WhiteWin,
        "=" => GameResult.Draw,
        "0-1" => GameResult.BlackWin,
        "-/+" => GameResult.WhiteForfeit,
        "+/-" => GameResult.BlackForfeit,
        "-/-" => GameResult.DoubleForfeit,
        "?" => GameResult.None,
        _ => throw new FormatException($"未知的結果代碼：{token}"),
    };
}

internal sealed class ScenarioPlayer
{
    public required int RegNo { get; init; }
    public required string Name { get; init; }
    public required int Rating { get; init; }
    public int? JoinRound { get; init; }
    public int? WithdrawRound { get; init; }
}

internal sealed class ScenarioRound
{
    public List<string> Games { get; init; } = [];
    public int? Bye { get; init; }
    public bool Draft { get; init; }
}
