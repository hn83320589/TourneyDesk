# 第 8 章　測試策略

> 本章目標：學會為一個演算法密集的系統設計測試——不只是「跑幾個例子」，而是用性質測試、獨立參考解、架構測試、效能測試，從不同角度「證明」程式是對的；並了解 TDD 在這個專案中如何實際運作。

專案共有 319 個測試（Core 259、Data 24、UI 36），全部在 CI 上執行。

---

## 8.1 測試的分層

```mermaid
graph TB
    subgraph 慢、少
        E[實機驗證（手動）<br/>列印預覽、缺少 WebView2、強制結束後接手鎖定]
    end
    subgraph 中
        U[UI 元件測試（bUnit）<br/>36 個]
        D[Data 整合測試（真實 SQLite 檔）<br/>24 個]
    end
    subgraph 快、多
        C[Core 單元測試 + 性質測試<br/>259 個]
    end
    E --> U
    E --> D
    U --> C
    D --> C
```

因為架構把規則與演算法集中在 Core，**絕大多數測試都是快速、無 I/O 的 Core 測試**。這是第 2 章「Core 不依賴任何人」帶來的直接紅利。

| 測試種類 | 目的 | 例子 |
|---|---|---|
| 單元測試 | 一條規則一個測試 | `Colors_fewer_whites_gets_white` |
| 手算範例 | 對照人工計算的預期檔 | `samples/tiebreak/`、`samples/swiss-round1/` |
| 性質測試 | 對大量隨機輸入檢查「永遠成立的性質」 | 復原後雜湊相同、半分總和守恆 |
| 參考解比對 | 用另一份獨立實作驗證最佳性 | 窮舉、位元 DP |
| 架構測試 | 守住相依規則與編碼規範 | Core 不引用 SQLite；不得用浮點數 |
| 效能測試 | 確認符合時間上限 | 400 人每輪 < 3 秒 |
| 整合測試 | 真實檔案、真實 SQLite | 往返無損、熱日誌回復、鎖定 |
| UI 元件測試 | 點按鈕、檢查 DOM | 建立賽事後出現正確的分頁 |

---

## 8.2 TDD 在這個專案中的實際做法

每個功能都走「紅 → 綠 → 重構 → commit」：

1. **紅**：先寫一個會失敗的測試，描述想要的行為。
2. **綠**：寫最少的程式讓它通過。
3. **重構**：測試保護下整理程式。
4. **commit**：commit message 說明「為什麼」。

修 bug 時也一樣：**先寫一個重現 bug 的失敗測試，確認它真的失敗，再修**。外部審查回報的三個問題都是這樣處理的（第 10 章）。

> 「確認它真的失敗」這一步不能省。熱日誌那個 bug 的第一版重現測試其實沒有重現問題（資料量太小），如果沒有先看到紅燈就去修程式，會誤以為修好了。

### 測試命名

測試名稱用完整的句子描述行為，讀測試清單就像讀規格書：

```
Correction_that_changes_next_proposal_marks_later_round_stale_without_touching_it
Withdrawal_in_published_round_does_not_make_it_stale
Hot_journal_left_by_a_crash_is_rolled_back_on_open
Opening_a_file_this_program_already_holds_says_so
```

### 測試輔助：讓測試讀起來像情境

```csharp
// tests/TourneyDesk.Core.Tests/Swiss/SwissProposalCacheTests.cs
private static TournamentState FourRounds() =>
    SwissFlow.NewSwiss(6)
        .NextRound().Results(W, W, W)
        .NextRound().Results(W, W, W)
        .NextRound().Results(W, W, W)
        .NextRound();
```

`SwissFlow`、`Then(command)` 等輔助方法（`tests/TourneyDesk.Core.Tests/TestSupport/`）把「建立一個打到第 4 輪的賽事」濃縮成一行，讓測試聚焦在要驗證的事。

---

## 8.3 性質測試（Property-Based Testing）

### 概念

單元測試檢查「這個輸入 → 這個輸出」。性質測試檢查「**對所有輸入，某個性質都成立**」，然後用大量隨機輸入去找反例。

例如「復原」的性質：

> 對任意操作序列，每一步復原後，狀態雜湊等於該步之前的雜湊。

這比寫 50 個「新增選手後復原」「登錄成績後復原」的單元測試強得多：隨機序列會組合出你想不到的操作順序。

### 自寫的輔助工具

專案沒有引入 FsCheck，而是 30 行的自寫輔助：

```csharp
// tests/TourneyDesk.Core.Tests/TestSupport/Property.cs
public static void ForAll(int seed, int cases, Action<Random, int> body)
{
    var master = new Random(seed);
    for (var i = 0; i < cases; i++)
    {
        var caseSeed = master.Next();
        try
        {
            body(new Random(caseSeed), caseSeed);
        }
        catch (Exception ex) when (ex is XunitException or InvalidOperationException or ...)
        {
            throw new XunitException(
                $"性質測試失敗：主種子={seed}，第 {i} 個案例，案例種子={caseSeed}（以 Property.Replay({caseSeed}, …) 重現）\n{ex}");
        }
    }
}
```

兩個關鍵設計：

- **固定種子**：每次執行的隨機輸入都一樣，CI 結果可重現，不會「今天綠、明天紅」。
- **失敗時印出案例種子**：用 `Property.Replay(caseSeed, ...)` 單獨重跑那一個案例來除錯。

### 專案中的性質測試

| # | 性質 | 種子 | 案例數 |
|---|---|---|---|
| 1 | 瑞士制配對 = 窮舉參考解（N ≤ 12，含先後手） | 3 | 600 |
| 2 | 瑞士制 ①～④ = 位元 DP 參考解（N ≤ 18） | 4 | 220 |
| 3 | 決定性：打亂順序、換 Id 後結果相同 | 5 | 220 |
| 4 | 引擎結果不劣於任何隨機合法方案（N 20～60） | 6 | 60 |
| 5 | 同分判定與成績登錄順序無關 | 17 | 200 |
| 6 | 半分總和守恆 | 1717 | 300 |
| 7 | 復原／重做後雜湊相同（每組 ≥ 50 步，涵蓋 17 種命令） | 20 | 45 |
| 8 | Blossom = 暴力搜尋（完全圖，含緊邊性質） | 20261020 | 400 |
| 9 | Blossom = 暴力搜尋（稀疏圖） | 7 | 300 |
| 10 | 賽事檔往返無損 | 18 | 24 |
| 11 | 過期判定：有快取 = 無快取 | 33 | 40 |
| 12 | 循環賽 N = 2～64、淘汰賽 N = 2～128 全部窮舉 | — | 126／127 |

### 復原性質測試

```csharp
// tests/TourneyDesk.Core.Tests/Session/UndoPropertyTests.cs（節錄）
Property.ForAll(seed: 20, cases: 45, (rng, _) =>
{
    var session = new TournamentSession(initial, new InMemoryStore(), new ManualTimeProvider());
    while (before.Count < 55 && attempts++ < 2000)
    {
        var command = RandomOperations.Next(session.State, rng);   // 隨機產生一個命令
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
    // 逐步復原，比對 before；再逐步重做，比對 after
});
// 最後斷言 17 種命令全部出現過
```

注意最後那個斷言：**確認隨機產生器真的涵蓋了所有命令種類**。否則可能某種命令從來沒被產生，性質測試對它形同虛設。

### 如何想出性質？

| 類型 | 例子 |
|---|---|
| 往返（round-trip） | 寫入後讀回 = 原狀態；復原後 = 原狀態 |
| 不變量（invariant） | 半分總和守恆；每輪每人恰好一次 |
| 與參考實作比較 | 引擎 = 窮舉；blossom = 暴力 |
| 對稱／無關性 | 打亂輸入順序結果不變；登錄順序不影響排名 |
| 不劣於 | 引擎結果 ≤ 隨機方案 |
| 有快取 = 無快取 | 快取正確性 |

---

## 8.4 獨立參考解

最佳化演算法最難測：你怎麼知道它找到的是「最佳」？答案是**另寫一份簡單但慢的實作**來比對。

| 參考解 | 方法 | 規模 | 比對 |
|---|---|---|---|
| `BruteForceSwissPairing` | 列舉所有「輪空者 × 完美匹配」，逐一算 ①～⑤ 取最小 | N ≤ 12 | 完整配對與先後手 |
| `BitmaskDpSwissPairing` | dp[未配對集合] = 剩餘部分的最小 (①,②,③,④) | N ≤ 18 | ①～④ 最佳值 |

窮舉法程式只有幾十行，簡單到「一看就知道對」；DP 能處理更大的 N，但不負責 ⑤。兩者互補。

**獨立性很重要**：參考解只引用輸入資料型別（`ReferenceRoundData`），**不呼叫任何 Core 演算法**。如果參考解也用 Core 的 `SwissObjective` 計算目標值，而 `SwissObjective` 本身有 bug，兩邊會一起錯、測試照樣通過。

### 變異測試：測試本身有效嗎？

故意把程式改錯，看測試會不會失敗：

- 把 ⑤ 的方向寫反 → 窮舉比對失敗 ✓
- 在 Core 公開 API 加一個 `double` 屬性 → `NoFloatingPointTests` 失敗 ✓

如果改錯了測試還是綠的，代表測試沒有能力抓到這類錯誤。

---

## 8.5 架構測試與規範測試

### 相依規則

見第 2 章：用反射檢查 Core 與 UI 引用的組件。

### 禁止浮點數

```csharp
// tests/TourneyDesk.Core.Tests/Architecture/NoFloatingPointTests.cs（節錄）
private static readonly Type[] Forbidden = [typeof(float), typeof(double), typeof(decimal)];

private static bool IsForbidden(Type t) =>
    Forbidden.Contains(t)
    || (Nullable.GetUnderlyingType(t) is { } u && Forbidden.Contains(u))          // double?
    || (t.IsGenericType && t.GetGenericArguments().Any(IsForbidden))              // List<double>
    || (t.HasElementType && IsForbidden(t.GetElementType()!));                    // double[]

[Fact]
public void Core_public_api_has_no_floating_point_members()
{
    foreach (var type in Assembly.Load("TourneyDesk.Core").GetExportedTypes())
        // 掃描欄位、屬性、方法回傳值與參數、建構子參數
    Assert.Empty(offending);
}
```

把「分數不得用浮點數」這條規則從「希望大家記得」變成「違反就紅燈」。

### 狀態雜湊的欄位守衛

`StateHasherTests` 用反射數每個狀態 record 的建構子參數數量；新增欄位時測試失敗，提醒同步更新雜湊（第 3 章）。

---

## 8.6 效能測試

```csharp
// tests/TourneyDesk.Core.Tests/Performance/ReleaseOnlyFactAttribute.cs
public sealed class ReleaseOnlyFactAttribute : FactAttribute
{
    public ReleaseOnlyFactAttribute()
    {
#if DEBUG
        Skip = "效能測試只在 Release 組態下執行（dotnet test -c Release）。";
#endif
    }
}
```

```csharp
// tests/TourneyDesk.Core.Tests/Performance/PerformanceLimits.cs
public static double Factor => /* 讀取環境變數 TOURNEYDESK_PERF_FACTOR，預設 1 */;
public static TimeSpan SwissRound400 => TimeSpan.FromMilliseconds(3000 * Factor);
public static TimeSpan Standings400 => TimeSpan.FromMilliseconds(200 * Factor);
```

| 設計 | 理由 |
|---|---|
| 只在 Release 執行 | Debug 關閉最佳化，耗時約 5 倍，不具代表性 |
| CI 放寬 2 倍 | 雲端 runner 比開發機慢；README 的數據以本機實測為準 |
| 同時檢查正確性 | 400 人 9 輪的效能測試同時驗證「每輪每人恰好一次、無重複輪空、無重複對戰」 |

---

## 8.7 Data 整合測試：用真實檔案

Data 層的測試用真實的 SQLite 檔（放在暫存目錄，`TempDirectory` 測試後刪除），因為很多問題只在真實檔案上才會出現：

| 測試 | 驗證 |
|---|---|
| `Committed_transitions_round_trip_losslessly` | 隨機操作序列，每段後重新開檔，雜湊與記憶體中的狀態相同 |
| `Too_new_version_…`、`Corrupted_file_…` 等 | 錯誤訊息明確，且**檔案的 SHA-256 不變** |
| `Old_version_is_backed_up_then_upgraded` | 備份檔與原檔逐位元組相同 |
| `Cancelled_pairing_leaves_file_byte_identical` | 在每個進度點取消配對，檔案 SHA-256 不變 |
| `Hot_journal_left_by_a_crash_is_rolled_back_on_open` | 製造熱日誌，開檔後自動回滾 |

### 如何製造「當機留下的熱日誌」

```csharp
// tests/TourneyDesk.Data.Tests/TournamentFileTests.cs（節錄；檔案中已有 300 位選手）
cmd.CommandText = "PRAGMA cache_size = 1; BEGIN IMMEDIATE; UPDATE players SET name = name || hex(randomblob(1000)), rating = 0;";
cmd.ExecuteNonQuery();                         // 交易進行中，尚未 COMMIT
Assert.True(File.Exists(source + "-journal"), "測試前提：交易途中應有日誌檔");
Assert.NotEqual(beforeTransaction, FileHash(source)); // 測試前提：主檔已被改動 → 日誌是「熱」的
File.Copy(source, crashed);                    // 把「交易進行到一半」的主檔與日誌複製一份
File.Copy(source + "-journal", crashed + "-journal");   // = 當機當下留在磁碟上的樣子
cmd.CommandText = "ROLLBACK";
```

- `cache_size = 1` 讓快取極小，每列又擴大約 2 KB，迫使 SQLite 在交易途中就把資料頁寫回主檔。
- 不能直接關閉連線來「模擬當機」：正常關閉時 SQLite 會自己回滾並刪除日誌。所以改為在交易進行中**複製檔案**，得到當機瞬間的磁碟狀態。
- 兩個「測試前提」斷言確保真的產生了熱日誌；接著開啟複製出來的檔，驗證資料回到交易前、日誌已刪除。

---

## 8.8 UI 元件測試（bUnit）

```csharp
// tests/TourneyDesk.UI.Tests/ShellTests.cs
[Fact]
public void Creating_a_tournament_opens_it_with_format_specific_tabs()
{
    var cut = Render<Shell>();

    cut.Find("button.new-tournament").Click();
    cut.Find("#nt-name").Change("春季瑞士制");
    cut.Find("button.create").Click();

    Assert.Equal("春季瑞士制", cut.Find(".tournament-name").TextContent);
    Assert.Equal(["選手", "輪次與配對", "成績登錄", "積分榜", "交叉表", "異動紀錄"],
        cut.FindAll(".tab").Select(b => b.TextContent));
}
```

測試基底 `UiTestBase` 註冊假的 Gateway、對話框、印表機（第 7 章），所以 UI 測試不碰檔案系統、不開視窗。

### 一個踩過的坑：不穩定的測試

有一個 UI 測試約每 8 次失敗 1 次。第一次的「修法」是把等待時間拉長——無效。仔細看失敗訊息「Check count: 1」才發現根因：

- bUnit 的 `WaitForAssertion` **只在元件重繪時才重新檢查**。
- 被測的 `RoundsView` 沒有訂閱 `Workspace.Changed`（實際 App 中是外層 `Shell` 負責重繪）。
- 背景的過期判定如果在第一次渲染**之後**才完成，元件不會重繪，斷言就永遠不會再檢查，直到逾時。

正確的修法：測試直接 `await Workspace.RefreshStalenessAsync()` 等背景判定完成，再渲染元件。

> **教學重點**：不穩定的測試（flaky test）一定有根因。「拉長等待時間」「重跑一次」只是把問題藏起來。修好後連續執行整套測試 15 次確認穩定。

---

## 8.9 本章小結

1. 架構讓大多數測試成為快速、無 I/O 的 Core 測試。
2. TDD：先看到紅燈（確認測試真的能抓到問題），再寫程式。
3. 性質測試用固定種子與可重現的案例種子，涵蓋大量隨機輸入；記得斷言產生器的涵蓋範圍。
4. 最佳化演算法用獨立的參考解驗證；參考解不得呼叫被測程式。
5. 變異測試檢查「測試本身有效」。
6. 架構與編碼規範寫成測試。
7. 效能測試只在 Release 執行，CI 放寬倍數。
8. 不穩定的測試要找根因，不能只拉長等待時間。

下一章：如何讓每次 commit 都自動建置、測試、打包？→ [第 9 章　建置、CI 與發佈](09-build-ci-release.md)
