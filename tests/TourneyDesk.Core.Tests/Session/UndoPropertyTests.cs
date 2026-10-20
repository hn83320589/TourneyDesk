using TourneyDesk.Core.Commands;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Hashing;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Tests.TestSupport;

namespace TourneyDesk.Core.Tests.Session;

public class UndoPropertyTests
{
    /// <summary>
    /// 驗收 20：隨機操作序列（涵蓋所有操作種類），逐步復原後整個賽事的雜湊與該步之前完全相同；
    /// 再逐步重做，雜湊與該步之後完全相同。每個案例至少 50 層。
    /// </summary>
    [Fact]
    public void Every_undo_restores_exact_previous_state_and_redo_reapplies()
    {
        var kinds = new HashSet<string>();
        Property.ForAll(seed: 20, cases: 45, (rng, _) =>
        {
            var format = (TournamentFormat)(rng.Next(4));
            var initial = TournamentState.Create(TournamentSettings.Create("復原測試", format, rng.Next(3, 8), thirdPlaceMatch: rng.Next(2) == 0));
            for (var i = 0; i < rng.Next(3, 9); i++)
                initial = initial.Then(new AddPlayer($"P{i}", rng.Next(0, 3001)));

            var session = new TournamentSession(initial, new InMemoryStore(), new ManualTimeProvider());
            var before = new List<string>();
            var after = new List<string>();
            var attempts = 0;
            while (before.Count < 55 && attempts++ < 2000)
            {
                var command = RandomOperations.Next(session.State, rng);
                var hash = StateHasher.Compute(session.State);
                var result = session.Execute(command);
                if (!result.Succeeded)
                {
                    Assert.Equal(hash, StateHasher.Compute(session.State)); // 失敗不得改變狀態
                    continue;
                }

                kinds.Add(command.GetType().Name);
                before.Add(hash);
                after.Add(StateHasher.Compute(session.State));
            }

            Assert.True(before.Count >= 50, $"只成功執行 {before.Count} 步");

            for (var i = before.Count - 1; i >= 0; i--)
            {
                Assert.True(session.Undo().Succeeded);
                Assert.Equal(before[i], StateHasher.Compute(session.State));
            }

            Assert.False(session.CanUndo);

            for (var i = 0; i < after.Count; i++)
            {
                Assert.True(session.Redo().Succeeded);
                Assert.Equal(after[i], StateHasher.Compute(session.State));
            }
        });

        string[] required =
        [
            nameof(AddPlayer), nameof(EditPlayer), nameof(DeletePlayer), nameof(GenerateSwissRound), nameof(SwapInDraft),
            nameof(PublishRound), nameof(SetResult), nameof(WithdrawPlayer), nameof(LateJoinPlayer), nameof(RepairFromRound),
            nameof(DiscardDraft), nameof(StartRoundRobin), nameof(StartKnockout), nameof(RecordKnockoutWinner),
            nameof(CorrectKnockoutWinner), nameof(WithdrawFromKnockout),
        ];
        Assert.Empty(required.Except(kinds));
    }
}
