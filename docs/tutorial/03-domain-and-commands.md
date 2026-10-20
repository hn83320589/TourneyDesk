# 第 3 章　領域模型與命令管線

> 本章目標：學會用「不可變狀態 + 純函式命令 + 單一管線」組織業務邏輯，並看懂它如何讓復原、寫檔、錯誤處理、測試都變得簡單。

---

## 3.1 不可變狀態（Immutable State）

整場賽事的狀態是一個不可變的 `record`：

```csharp
// src/TourneyDesk.Core/Domain/TournamentState.cs
public sealed record TournamentState(
    TournamentSettings Settings,
    ImmutableSortedDictionary<int, Player> Players,
    ImmutableArray<Round> Rounds,
    int NextPlayerId,
    int NextRegNo,
    Bracket? Bracket = null)
```

底下的 `Player`、`Round`、`Game`、`Bracket` 也全都是不可變 record，集合用 `System.Collections.Immutable`。

要「修改」時，產生一個新物件：

```csharp
public TournamentState WithPlayer(Player player) => this with { Players = Players.SetItem(player.Id, player) };
```

### 為什麼要不可變？

| 好處 | 說明 |
|---|---|
| 復原變得極簡單 | 歷史只要保存「之前的狀態物件」，復原就是把它放回去。不需要寫「反向操作」 |
| 結構共享，成本低 | `Players.SetItem` 只複製樹狀結構中被改到的那條路徑，其餘節點與舊狀態共用 |
| 執行緒安全 | 背景配對計算讀的是快照，UI 執行緒同時產生新狀態也不會互相干擾 |
| 差異寫入很快 | Data 層用 `ReferenceEquals` 判斷子樹有沒有變（沒變的子樹是同一個物件），第 6 章會看到 |
| 不會有「改到一半」 | 命令中途丟出例外時，舊狀態原封不動 |

### 衍生資料一律不儲存

`TournamentState` 的註解寫著：**排名、同分值、過期標記等衍生資料一律即時計算，不存在這裡。**

如果把「名次」也存進狀態，每次更正成績都要記得重算並更新——忘了一次，資料就不一致。即時計算則永遠正確。代價是效能，所以需要確認算得夠快：400 人的積分榜重算只要約 1.5 ms。

> **教學重點**：資料庫設計的「正規化」精神同樣適用於記憶體中的模型。能算出來的就不要存。

---

## 3.2 用整數表示分數

```csharp
// src/TourneyDesk.Core/Domain/HalfPoints.cs
/// <summary>所有分數一律以整數「半分」表示：勝 2、和 1、負 0。</summary>
public static class HalfPoints
{
    public const int Win = 2;
    public const int Draw = 1;
    public const int Loss = 0;
}
```

為什麼不用 `double`？

- `0.1 + 0.2 != 0.3`。同分判定要比較「兩人分數是否相同」，浮點誤差可能讓兩個理論上相等的值被判為不同。
- SB（Sonneborn-Berger）是「對手分數 × 自己對該對手的得分」的總和，用半分計算時就是「半分 × 半分」，仍然是整數。
- 顯示時才轉換：`HalfPoints.Format(5)` → `"2.5"`。

這條規則用一個反射測試強制（`NoFloatingPointTests`，見第 8 章）。

---

## 3.3 規則違反：DomainException

```csharp
/// <summary>違反賽事規則；訊息直接呈現給使用者，因此一律為可讀的中文。</summary>
public sealed class DomainException(string message) : Exception(message);
```

設計重點：

- **只有一種業務例外**，訊息就是要給使用者看的句子，例如「第 3 輪尚未發布，不能登錄成績。」
- UI 不需要對不同例外寫不同的處理，統一顯示訊息即可。
- 程式錯誤（bug）則用 `InvalidOperationException`、`ArgumentException`，與業務規則區分開。

---

## 3.4 命令模式（Command Pattern）

所有會改變狀態的操作，都實作同一個介面：

```csharp
// src/TourneyDesk.Core/Session/ITournamentCommand.cs
public sealed record CommandContext(DateTimeOffset Now) { /* Audit(...) 輔助方法 */ }

public sealed record Transition(TournamentState NewState, string Description, IReadOnlyList<AuditEntry> Audit);

public interface ITournamentCommand
{
    Transition Apply(TournamentState state, CommandContext context);
}
```

一個命令就是一個 `record`，欄位是操作參數，`Apply` 是**純函式**：

- 只依 `state` 與命令內容決定結果。
- 不讀時鐘（時間由 `context.Now` 提供）、不碰檔案、不改任何外部狀態。
- 違規時丟 `DomainException`。
- 回傳 `Transition`：新狀態、顯示在復原按鈕上的描述、要寫入異動紀錄的項目。

### 範例：登錄成績

```csharp
// src/TourneyDesk.Core/Commands/ResultCommands.cs
public sealed record SetResult(int RoundNumber, int Board, GameResult Result) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        // 1. 驗證：每個檢查都是一句使用者看得懂的話
        if (state.Settings.Format == TournamentFormat.SingleElimination)
            throw new DomainException("淘汰賽請在籤表中登錄勝者。");
        var round = state.GetRound(RoundNumber);
        if (!round.IsPublished)
            throw new DomainException($"第 {RoundNumber} 輪尚未發布，不能登錄成績。");
        if (Board < 1 || Board > round.Games.Length)
            throw new DomainException($"第 {RoundNumber} 輪沒有第 {Board} 桌。");
        var game = round.Games[Board - 1];
        if (game.Result == Result)
            throw new DomainException("成績沒有變更。");

        // 2. 計算新狀態：with 運算式產生新物件
        var updated = round with { Games = round.Games.SetItem(Board - 1, game with { Result = Result }) };

        // 3. 描述與異動紀錄
        var kind = game.Result.HasResult() ? "更正成績" : "登錄成績";
        var target = $"{Describe.Board(state, RoundNumber, Board)} {Describe.Game(state, game)}";
        return new Transition(state.WithRound(updated), $"{kind}：{target} {Result.DisplayName()}",
            [context.Audit(kind, target, game.Result.DisplayName(), Result.DisplayName())]);
    }
}
```

每個命令都是這個三段式：**驗證 → 計算新狀態 → 描述與異動紀錄**。專案共有 17 種命令，都長得很像，讀懂一個就讀懂全部。

### 為什麼「成績沒有變更」也要擋？

如果允許，使用者會在復原清單裡看到一個什麼都沒做的步驟，異動紀錄也會多一筆雜訊。讓命令在「沒有變化」時失敗，可以保證**每個成功的命令都真的改變了狀態**。

### 耗時計算不放在命令裡

瑞士制的配對可能要算半秒以上。如果放在 `Apply` 裡，就會卡住 UI 執行緒。因此拆成兩步：

```csharp
// 第一步：背景計算（不改變狀態，可取消）
var result = SwissRoundPlanner.Propose(snapshot, round, progress, cancellationToken);
// 第二步：把結果交給命令（很快）
workspace.Execute(new GenerateSwissRound(round, result.Pairing));
```

`GenerateSwissRound.Apply` 仍然會驗證傳入的建議「涵蓋所有可配對選手各一次」，所以就算傳入的資料有誤也不會破壞狀態。

---

## 3.5 Session：唯一的變更管線

```csharp
// src/TourneyDesk.Core/Session/TournamentSession.cs（節錄）
public CommandResult Execute(ITournamentCommand command)
{
    if (BusyError() is { } busy)            // 背景工作中 → 拒絕
        return busy;

    Transition transition;
    try
    {
        transition = command.Apply(State, new CommandContext(time.GetUtcNow()));
    }
    catch (DomainException ex)
    {
        return CommandResult.Fail(ex.Message);  // 規則違反 → 可讀訊息，狀態不變
    }

    var before = State;
    var result = CommitTo(transition.NewState, transition.Audit);   // 先寫檔
    if (result.Succeeded)
        _history.Record(new HistoryEntry(before, transition.NewState, transition.Description));
    return Publish(result);                                         // 通知畫面
}

private CommandResult CommitTo(TournamentState target, IReadOnlyList<AuditEntry> audit)
{
    try
    {
        store.Commit(State, target, audit);     // 單一交易寫入
    }
    catch (StorageException ex)
    {
        return CommandResult.Fail($"無法寫入賽事檔：{ex.Message}");
    }

    State = target;                              // 寫入成功後才更新記憶體
    return CommandResult.Ok;
}
```

**復原與重做也走同一條管線**：

```csharp
public CommandResult Undo()
{
    // ...
    var result = CommitTo(entry.Before, [Audit("復原", entry.Description)]);
    if (result.Succeeded)
        _history.CommitUndo();
    return Publish(result);
}
```

這帶來幾個重要保證：

| 保證 | 怎麼做到的 |
|---|---|
| 畫面上看到的狀態一定已經存進檔案 | 先 `store.Commit` 再 `State = target` |
| 寫檔失敗時什麼都不變 | 例外被轉成 `CommandResult.Fail`，`State` 與歷史都沒動 |
| 復原也會寫檔、也有異動紀錄 | 復原只是「把狀態改回 Before」的另一次轉移 |
| 異動紀錄永遠完整 | 異動紀錄與狀態在同一個交易裡寫入 |

> **教學重點**：「所有變更只有一個入口」是讓系統可靠的最有效手段。一旦有第二條路（例如某個畫面直接改資料），所有保證都會失效。

---

## 3.6 快照式復原

```csharp
// src/TourneyDesk.Core/Session/UndoHistory.cs
public sealed record HistoryEntry(TournamentState Before, TournamentState After, string Description);

public sealed class UndoHistory(int capacity)
{
    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    public void Record(HistoryEntry entry)
    {
        _undo.AddLast(entry);
        if (_undo.Count > Capacity)
            _undo.RemoveFirst();     // 超過上限（100 層）丟掉最舊的
        _redo.Clear();               // 做了新動作，重做堆疊作廢
    }
    // CommitUndo / CommitRedo：在兩個堆疊之間搬移
}
```

傳統的「命令式復原」要為每個命令寫一個反向操作（例如「新增選手」的反向是「刪除選手」），很容易出錯：刪除選手時要不要還原註冊編號計數器？要不要還原他原本的位置？

快照式復原完全不需要這些：**復原 = 把 `Before` 放回去**。正確性由構造保證。性質測試再驗證一次：隨機執行至少 50 個命令，逐步復原，每一步的狀態雜湊都必須與該步之前完全相同（見第 8 章）。

為什麼 undo 用 `LinkedList` 而不是 `Stack`？因為超過上限時要從**底部**移除最舊的一筆，`Stack` 做不到。

### 狀態雜湊

為了驗證「完全相同」，`StateHasher` 把整個狀態依固定順序寫成位元組，計算 SHA-256：

```csharp
// src/TourneyDesk.Core/Hashing/StateHasher.cs（節錄）
w.Byte(FormatVersion);
WriteSettings(w, state.Settings);
w.Int(state.NextPlayerId);
w.Int(state.NextRegNo);
foreach (var p in state.Players.Values) // 依內部 Id 排序
    WritePlayer(w, p);
// ... rounds、bracket
```

新增狀態欄位時，如果忘了加進雜湊，驗證就會漏掉這個欄位。所以 `StateHasherTests` 有一個**欄位數量守衛**：用反射數每個狀態 record 主建構子的參數數量，數量變了測試就失敗，提醒你同步更新雜湊。

---

## 3.7 破壞性操作：影響清單與指紋

「從第 k 輪起重新配對」會清除後面所有輪次的配對與成績。UI 要先列出影響範圍給使用者確認。但有個陷阱：

> 使用者看到清單 → 去倒杯水 → 另一個操作改變了狀態 → 回來按「確認」，結果清除的範圍和他看到的不一樣。

解法是**確認指紋**：

```csharp
// src/TourneyDesk.Core/Commands/ClearImpact.cs
public sealed record ClearImpact(ImmutableArray<string> Items)
{
    public string Fingerprint =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Items))));
}

// src/TourneyDesk.Core/Commands/SwissCommands.cs
public sealed record RepairFromRound(int RoundNumber, Pairing NewProposal, string ConfirmedImpactFingerprint) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        var impact = Preview(state, RoundNumber);                  // 執行時重新計算影響
        if (impact.Fingerprint != ConfirmedImpactFingerprint)      // 與使用者確認的那份比對
            throw new DomainException("影響範圍已變更，請重新檢視並確認。");
        // ...
    }
}
```

命令帶著「使用者確認的是哪一份清單」的指紋，執行時重算並比對。淘汰賽的「更正勝者（連鎖清除）」也用同樣的機制。

---

## 3.8 手動交換與違規的處理

草稿可以手動交換兩位選手（`SwapInDraft`）。交換可能造成「重複對戰」或「重複輪空」，但現實中裁判有時確實需要這樣做（例如選手要求）。系統的立場是：

- **不禁止，但要明確**：發布時若有違規，必須帶 `Force: true`（UI 上是「強制採用」勾選框），否則命令失敗並列出違規。
- **留下紀錄**：與引擎建議不同的方案會寫入異動紀錄「採用手動調整」或「強制採用」，並記錄前後方案。
- **即時比較**：草稿畫面同時顯示「最佳解」與「目前方案」的 ①～④ 指標，讓裁判知道偏離了多少。

---

## 3.9 動手做：新增一個命令

假設要新增「修改賽事名稱」：

1. **先寫測試**（TDD，紅燈）：

   ```csharp
   [Fact]
   public void Rename_changes_name_and_is_audited()
   {
       var s = TournamentState.Create(TournamentSettings.Create("舊名", TournamentFormat.Swiss));
       var t = new RenameTournament("新名").Apply(s, new CommandContext(DateTimeOffset.UnixEpoch));
       Assert.Equal("新名", t.NewState.Settings.Name);
       Assert.Equal("修改賽事名稱", Assert.Single(t.Audit).Kind);
   }
   ```

2. **實作**（綠燈），照三段式寫：

   ```csharp
   public sealed record RenameTournament(string Name) : ITournamentCommand
   {
       public Transition Apply(TournamentState state, CommandContext context)
       {
           var settings = state.Settings with { Name = Name.Trim() };
           settings.Validate();                          // 重用既有驗證
           if (settings == state.Settings)
               throw new DomainException("名稱沒有變更。");
           return new Transition(state with { Settings = settings }, $"修改賽事名稱為「{settings.Name}」",
               [context.Audit("修改賽事名稱", "賽事", state.Settings.Name, settings.Name)]);
       }
   }
   ```

3. **不用改的地方**：Session、復原、寫檔、異動紀錄全部自動支援。Data 層的差異寫入發現 `Settings` 變了，會自動更新 `settings` 表。

4. **記得更新**：`RandomOperations`（復原性質測試的隨機操作產生器），讓新命令也被涵蓋。

這就是好架構的價值：**新增功能時，只需要寫「這個功能特有的部分」**。

---

## 3.10 本章小結

1. 狀態不可變；衍生資料即時計算、不儲存。
2. 分數用整數半分，避免浮點誤差。
3. 每個命令是純函式：驗證 → 計算 → 描述與紀錄。
4. 所有變更（含復原、重做）只走 Session 一條管線：先寫檔，成功才更新記憶體。
5. 快照式復原由構造保證正確；狀態雜湊用來驗證。
6. 破壞性操作用「影響清單指紋」防止確認後狀態已變。

下一章：整個系統最難的部分——瑞士制配對引擎。→ [第 4 章　瑞士制配對演算法](04-swiss-pairing.md)
