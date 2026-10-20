using System.Diagnostics;
using System.Globalization;
using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Import;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Data;

// 產生測試與示範用的賽事檔（大檔不提交到 repo，需要時以此工具產生）。
//
//   players400 <輸出.tourney> [--rounds N] [--seed S]   400 人瑞士制；可先以引擎配對並隨機登錄 N 輪
//   players400-csv <輸出.csv> [--seed S]                400 人選手名單 CSV（UTF-8 with BOM），供匯入測試
//   demo <輸出資料夾>                                     示範賽事：40 人瑞士制、7 人單循環、11 人單淘汰

return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2)
        return Usage();

    var seed = IntOption(args, "--seed", 400);
    try
    {
        switch (args[0])
        {
            case "players400":
                Players400(args[1], IntOption(args, "--rounds", 0), seed);
                return 0;
            case "players400-csv":
                File.WriteAllBytes(args[1], Csv.ToUtf8WithBom(
                    new[] { new[] { "註冊編號", "姓名", "積分" } }.Concat(
                        RandomPlayers(400, seed).Select((p, i) => new[] { (i + 1).ToString(CultureInfo.InvariantCulture), p.Name, p.Rating.ToString(CultureInfo.InvariantCulture) }))));
                Console.WriteLine($"已產生 {args[1]}");
                return 0;
            case "demo":
                Demo(args[1]);
                return 0;
            default:
                return Usage();
        }
    }
    catch (Exception ex) when (ex is StorageException or DomainException or IOException)
    {
        Console.Error.WriteLine($"失敗：{ex.Message}");
        return 1;
    }
}

static int Usage()
{
    Console.Error.WriteLine("用法：");
    Console.Error.WriteLine("  TourneyDesk.SampleGen players400 <輸出.tourney> [--rounds N] [--seed S]");
    Console.Error.WriteLine("  TourneyDesk.SampleGen players400-csv <輸出.csv> [--seed S]");
    Console.Error.WriteLine("  TourneyDesk.SampleGen demo <輸出資料夾>");
    return 2;
}

static int IntOption(string[] args, string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

static List<(string Name, int Rating)> RandomPlayers(int count, int seed)
{
    string[] family = ["陳", "林", "黃", "張", "李", "王", "吳", "劉", "蔡", "楊", "許", "鄭", "謝", "郭", "洪"];
    string[] given = ["志明", "淑芬", "建宏", "美玲", "俊傑", "雅婷", "家豪", "怡君", "冠宇", "佳穎", "柏翰", "宜蓁"];
    var rng = new Random(seed);
    return [.. Enumerable.Range(0, count).Select(_ => ($"{family[rng.Next(family.Length)]}{given[rng.Next(given.Length)]}", rng.Next(800, 2400)))];
}

static GameResult RandomResult(Random rng) => rng.Next(100) switch
{
    < 38 => GameResult.WhiteWin,
    < 76 => GameResult.BlackWin,
    < 94 => GameResult.Draw,
    < 97 => GameResult.WhiteForfeit,
    _ => GameResult.BlackForfeit,
};

/// <summary>以真實的命令流程建立賽事並寫入檔案（每一步都是一筆交易，與程式操作相同）。</summary>
static TournamentSession NewFile(string path, TournamentSettings settings)
{
    if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
        Directory.CreateDirectory(dir);
    var initial = TournamentState.Create(settings);
    var file = TournamentFile.Create(path, initial);
    return new TournamentSession(initial, file, TimeProvider.System);
}

static void Must(CommandResult result)
{
    if (!result.Succeeded)
        throw new DomainException(result.Error!);
}

static void PlaySwissRound(TournamentSession session, Random rng, bool enterResults)
{
    var round = session.State.Rounds.Length + 1;
    var sw = Stopwatch.StartNew();
    var proposal = SwissRoundPlanner.Propose(session.State, round).Pairing;
    Console.WriteLine($"  第 {round} 輪配對：{sw.ElapsedMilliseconds} ms");
    Must(session.Execute(new GenerateSwissRound(round, proposal)));
    Must(session.Execute(new PublishRound(round)));
    if (!enterResults)
        return;
    for (var b = 1; b <= proposal.Games.Length; b++)
        Must(session.Execute(new SetResult(round, b, RandomResult(rng))));
}

static void Players400(string path, int rounds, int seed)
{
    var session = NewFile(path, TournamentSettings.Create("400 人測試賽事", TournamentFormat.Swiss, Math.Max(rounds, 9)));
    var players = RandomPlayers(400, seed);
    Must(session.Execute(new ImportPlayers([.. players.Select((p, i) => new ImportedPlayer(i + 2, null, p.Name, p.Rating))])));
    var rng = new Random(seed);
    for (var r = 0; r < rounds; r++)
        PlaySwissRound(session, rng, enterResults: true);
    Console.WriteLine($"已產生 {path}（400 人、{rounds} 輪）");
}

static void Demo(string folder)
{
    var rng = new Random(2026);

    // 40 人瑞士制：3 輪已完成（含 1 人退賽、1 人遲到加入），第 4 輪為草稿
    var swiss = NewFile(Path.Combine(folder, "社區桌球秋季瑞士制.tourney"), TournamentSettings.Create("社區桌球秋季瑞士制", TournamentFormat.Swiss, 6));
    foreach (var (name, rating) in RandomPlayers(39, 40))
        Must(swiss.Execute(new AddPlayer(name, rating)));
    PlaySwissRound(swiss, rng, enterResults: true);
    PlaySwissRound(swiss, rng, enterResults: true);
    Must(swiss.Execute(new WithdrawPlayer(swiss.State.Players.Keys.Last(), 3)));
    Must(swiss.Execute(new LateJoinPlayer("周晚到", 1650, 3)));
    PlaySwissRound(swiss, rng, enterResults: true);
    var r4 = swiss.State.Rounds.Length + 1;
    Must(swiss.Execute(new GenerateSwissRound(r4, SwissRoundPlanner.Propose(swiss.State, r4).Pairing)));

    // 7 人單循環：前 3 輪已登錄
    var rr = NewFile(Path.Combine(folder, "單循環示範.tourney"), TournamentSettings.Create("單循環示範", TournamentFormat.RoundRobin));
    foreach (var (name, rating) in RandomPlayers(7, 7))
        Must(rr.Execute(new AddPlayer(name, rating)));
    Must(rr.Execute(new StartRoundRobin()));
    for (var r = 1; r <= 3; r++)
        for (var b = 1; b <= rr.State.GetRound(r).Games.Length; b++)
            Must(rr.Execute(new SetResult(r, b, RandomResult(rng))));

    // 11 人單淘汰（含季軍戰）：第 1 輪與八強已完成
    var ko = NewFile(Path.Combine(folder, "單淘汰示範.tourney"), TournamentSettings.Create("單淘汰示範", TournamentFormat.SingleElimination, thirdPlaceMatch: true));
    foreach (var (name, rating) in RandomPlayers(11, 11))
        Must(ko.Execute(new AddPlayer(name, rating)));
    Must(ko.Execute(new StartKnockout()));
    foreach (var m in Enumerable.Range(1, 12))
    {
        var bracket = ko.State.Bracket!;
        var match = bracket.GetMatch(m);
        var (a, b) = bracket.Participants(match);
        if (match.Outcome is null && a.IsKnown && b.IsKnown)
            Must(ko.Execute(new RecordKnockoutWinner(m, rng.Next(3) == 0 ? b.PlayerId!.Value : a.PlayerId!.Value)));
    }

    Console.WriteLine($"已產生示範賽事於 {Path.GetFullPath(folder)}");
}
