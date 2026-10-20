using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Swiss;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Swiss;

public class SwissPairingEngineTests
{
    /// <summary>以字母 A、B、C… 建立候選人（Id = 註冊編號 = 1、2、3…），積分遞減使排名 = 字母順序。</summary>
    private static SwissCandidate C(char name, int score = 0, bool hadBye = false, int whites = 0, PieceColor? last = null)
    {
        var id = name - 'A' + 1;
        return new SwissCandidate(id, id, 2000 - id, score, hadBye, whites, last);
    }

    private static (int, int) M(char a, char b) => (a - 'A' + 1, b - 'A' + 1);

    private static string Describe(Pairing p) =>
        string.Join(" ", p.Games.Select(g => $"{(char)('A' + g.White - 1)}-{(char)('A' + g.Black - 1)}")
            .Concat(p.Bye is { } b ? [$"bye:{(char)('A' + b - 1)}"] : []));

    private static List<string> ReadExpected(string file) =>
        [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "swiss-round1", file))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))];

    private static List<string> AsExpectedLines(TournamentState state, Pairing p)
    {
        var lines = p.Games.Select((g, i) => $"{i + 1} {state.Players[g.White].RegNo} {state.Players[g.Black].RegNo}").ToList();
        if (p.Bye is { } bye)
            lines.Add($"bye {state.Players[bye].RegNo}");
        return lines;
    }

    [Theory]
    [InlineData("even.json", "expected-even.txt")]
    [InlineData("odd.json", "expected-odd.txt")]
    public void Round_one_matches_hand_calculated_sample(string scenario, string expected)
    {
        var state = ScenarioFile.Load($"swiss-round1/{scenario}").ToState();

        var result = SwissPairingEngine.Pair(SwissRoundInput.FromState(state, 1));

        Assert.Equal(ReadExpected(expected), AsExpectedLines(state, result.Pairing));
    }

    [Fact]
    public void Unavoidable_repeat_is_kept_to_the_minimum()
    {
        // A 已和所有人交手 → 至少 1 場重複。另有 B–C、D–E 交手過。
        // 最少重複 = 1；⑤：A 的對手越後面越好 → A–F；剩 B、C、D、E：B 的對手越後面越好且不重複 → B–E；C–D。
        var input = new SwissRoundInput(
            [C('A'), C('B'), C('C'), C('D'), C('E'), C('F')],
            [M('A', 'B'), M('A', 'C'), M('A', 'D'), M('A', 'E'), M('A', 'F'), M('B', 'C'), M('D', 'E')]);

        var result = SwissPairingEngine.Pair(input);

        Assert.Equal(1, result.Evaluation.Objective.Repeats);
        Assert.Equal("A-F B-E C-D", Describe(result.Pairing));
        var violation = Assert.Single(result.Evaluation.Violations);
        Assert.Equal(new PairingViolation(ViolationKind.RepeatPairing, 1, 6), violation);
    }

    [Fact]
    public void When_everyone_had_a_bye_the_last_ranked_player_gets_it()
    {
        var input = new SwissRoundInput(
            [C('A', 6, true), C('B', 4, true), C('C', 4, true), C('D', 2, true), C('E', 2, true)], []);

        var result = SwissPairingEngine.Pair(input);

        Assert.Equal(5, result.Pairing.Bye);
        Assert.Equal(1, result.Evaluation.Objective.ByeRepeat);
        Assert.Equal(5, result.Evaluation.Objective.ByeRank);
    }

    [Fact]
    public void Bye_goes_to_lowest_ranked_player_without_previous_bye()
    {
        // E（第 5 名）已輪空過 → ② 優先 → D（第 4 名）輪空
        var input = new SwissRoundInput([C('A', 4), C('B', 4), C('C', 2), C('D', 2), C('E', 0, hadBye: true)], []);

        var result = SwissPairingEngine.Pair(input);

        Assert.Equal(4, result.Pairing.Bye);
        Assert.Equal(0, result.Evaluation.Objective.ByeRepeat);
    }

    [Fact]
    public void Avoiding_repeats_takes_priority_over_avoiding_repeated_bye()
    {
        // A–B、B–C 交手過，B 已輪空過。讓 A 或 C 輪空都會造成重複對戰 → 只能讓 B 再輪空（① 優先於 ②）
        var input = new SwissRoundInput([C('A'), C('B', hadBye: true), C('C')], [M('A', 'B'), M('B', 'C')]);

        var result = SwissPairingEngine.Pair(input);

        Assert.Equal("A-C bye:B", Describe(result.Pairing));
        Assert.Equal((0, 1), (result.Evaluation.Objective.Repeats, result.Evaluation.Objective.ByeRepeat));
    }

    [Fact]
    public void Score_difference_squares_prefer_pairing_within_score_groups()
    {
        // 分數 4、4、2、2 → A–B、C–D（④=0）優於 A–D、B–C（④=8）
        var input = new SwissRoundInput([C('A', 4), C('B', 4), C('C', 2), C('D', 2)], []);

        var result = SwissPairingEngine.Pair(input);

        Assert.Equal("A-B C-D", Describe(result.Pairing));
        Assert.Equal(0, result.Evaluation.Objective.ScoreDiffSquares);
    }

    [Fact]
    public void Single_player_gets_the_bye_and_empty_input_gives_empty_pairing()
    {
        Assert.Equal("bye:A", Describe(SwissPairingEngine.Pair(new SwissRoundInput([C('A')], [])).Pairing));
        Assert.Equal("", Describe(SwissPairingEngine.Pair(new SwissRoundInput([], [])).Pairing));
    }

    [Fact]
    public void Colors_fewer_whites_gets_white()
    {
        // A 排名較高但先手 2 次，B 先手 1 次 → B 先手
        var input = new SwissRoundInput([C('A', whites: 2, last: PieceColor.Black), C('B', whites: 1, last: PieceColor.White)], []);

        Assert.Equal(new PairedGame(2, 1), ColorAllocator.Assign(input, 1, 2));
    }

    [Fact]
    public void Colors_equal_whites_previous_black_gets_white()
    {
        // 先手次數相同；A 上一場先手、B 上一場後手 → B 先手
        var input = new SwissRoundInput([C('A', whites: 1, last: PieceColor.White), C('B', whites: 1, last: PieceColor.Black)], []);

        Assert.Equal(new PairedGame(2, 1), ColorAllocator.Assign(input, 1, 2));
    }

    [Theory]
    [InlineData(PieceColor.Black, PieceColor.Black)]
    [InlineData(PieceColor.White, PieceColor.White)]
    [InlineData(PieceColor.White, null)]
    public void Colors_still_equal_higher_rank_gets_white(PieceColor lastA, PieceColor? lastB)
    {
        var input = new SwissRoundInput([C('A', whites: 1, last: lastA), C('B', whites: 1, last: lastB)], []);

        Assert.Equal(new PairedGame(1, 2), ColorAllocator.Assign(input, 2, 1));
    }

    [Fact]
    public void Colors_previous_black_beats_no_previous_game()
    {
        // B 上一場後手，A 沒有對局過（例如遲到加入）→ B 先手
        var input = new SwissRoundInput([C('A', whites: 0, last: null), C('B', whites: 0, last: PieceColor.Black)], []);

        Assert.Equal(new PairedGame(2, 1), ColorAllocator.Assign(input, 1, 2));
    }

    [Fact]
    public void Progress_is_monotonic_and_ends_at_100()
    {
        var (state, round) = SwissHistoryGenerator.Generate(new Random(9), 60, 3);
        var reports = new List<int>();

        SwissPairingEngine.Pair(SwissRoundInput.FromState(state, round), new SyncProgress(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(0, reports[0]);
        Assert.Equal(100, reports[^1]);
        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i] >= reports[i - 1], $"進度倒退：{reports[i - 1]} → {reports[i]}");
    }

    [Fact]
    public void Cancellation_at_any_progress_point_throws_without_result()
    {
        var (state, round) = SwissHistoryGenerator.Generate(new Random(10), 80, 2);
        var input = SwissRoundInput.FromState(state, round);
        var points = new List<int>();
        SwissPairingEngine.Pair(input, new SyncProgress(points.Add));

        for (var i = 0; i < points.Count - 1; i++)
        {
            using var cts = new CancellationTokenSource();
            var calls = 0;
            var stopAt = i;
            var progress = new SyncProgress(_ =>
            {
                if (calls++ == stopAt)
                    cts.Cancel();
            });

            Assert.ThrowsAny<OperationCanceledException>(() => SwissPairingEngine.Pair(input, progress, cts.Token));
        }
    }

    [Fact]
    public void Evaluate_rejects_incomplete_or_duplicated_pairings()
    {
        var input = new SwissRoundInput([C('A'), C('B'), C('C')], []);

        Assert.Throws<DomainException>(() => SwissObjective.Evaluate(input, new Pairing([new PairedGame(1, 2)], null)));
        Assert.Throws<DomainException>(() => SwissObjective.Evaluate(input, new Pairing([new PairedGame(1, 2)], 2)));
        Assert.Throws<DomainException>(() => SwissObjective.Evaluate(input, new Pairing([new PairedGame(1, 9)], 3)));
    }

    [Fact]
    public void Objective_ordering_follows_lexicographic_rules()
    {
        ObjectiveVector V(int rep, int byeRep, int? byeRank, long sq, params (int, int)[] pairs) => new(rep, byeRep, byeRank, sq, [.. pairs]);

        Assert.True(V(0, 1, 1, 99).CompareTo(V(1, 0, 9, 0)) < 0);
        Assert.True(V(0, 0, 1, 99).CompareTo(V(0, 1, 9, 0)) < 0);
        Assert.True(V(0, 0, 9, 99).CompareTo(V(0, 0, 8, 0)) < 0);
        Assert.True(V(0, 0, null, 4).CompareTo(V(0, 0, null, 6)) < 0);
        Assert.True(V(0, 0, null, 0, (1, 4), (2, 3)).CompareTo(V(0, 0, null, 0, (1, 3), (2, 4))) < 0);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
