# TourneyDesk 系統分析與架構設計（草案 v0.1）

> 狀態：**已確認（2026-10-20）**。第 14 節 Q1～Q8 全部採用建議方案。

---

## 1. 需求拆解與範圍

| 模組 | 重點 | 難度 | 對應驗收 |
|---|---|---|---|
| 賽事／選手管理、CSV 匯入 | 編碼偵測（UTF-8±BOM / Big5）、逐列錯誤回報 | 低 | 1 |
| **瑞士制配對引擎** | 字典序 ①～⑤ 最佳化、400 人 < 3s、可取消、窮舉／DP 驗證 | **極高** | 2–9 |
| 循環賽排程 | 單／雙循環、輪空平均、先後手平衡 | 中 | 10–11 |
| 單淘汰籤表 | 標準種子、輪空、季軍戰、連鎖清除＋復原 | 中 | 12–13 |
| 成績更正與過期判定 | 不得悄悄改動已發布配對、「從第 k 輪重新配對」 | 高 | 14 |
| 排名／同分判定 | 遞迴分群、虛擬對手、Cut-1、SB | 高 | 15–17 |
| 檔案／鎖定／復原 | SQLite 單檔、schema 版本、網路磁碟鎖、50 層 undo | 高 | 18–20 |
| 版面計算與輸出 | 籤表／交叉表座標、分頁、SVG、列印、CSV/HTML 匯出 | 中 | 21–22 |
| 工程 | ≥180 測試、≥7 性質測試、CI、README | 中 | 23–25 |

---

## 2. 解決方案結構

```
TourneyDesk.sln
├─ src/
│  ├─ TourneyDesk.Core      net9.0        純邏輯，零 UI 相依
│  ├─ TourneyDesk.Data      net9.0        Microsoft.Data.Sqlite 9.0.x、鎖定檔實作
│  ├─ TourneyDesk.UI        net9.0 RCL    Razor 元件（不引用 WinForms）
│  └─ TourneyDesk.App       net9.0-windows WinForms + BlazorWebView 外殼、DI 組裝
├─ tests/
│  ├─ TourneyDesk.Core.Tests   xUnit；含 Reference/（窮舉、位元 DP）與 Properties/
│  ├─ TourneyDesk.Data.Tests   xUnit；檔案往返、遷移、鎖定（暫存目錄）
│  └─ TourneyDesk.UI.Tests     xUnit + bUnit
├─ tools/
│  └─ TourneyDesk.SampleGen    產生 400 人測試賽事（console，產出不入 repo）
├─ samples/                    範例資料與預期結果（JSON/CSV + 手算說明 .md）
└─ .github/workflows/ci.yml
```

相依方向（單向）：

```
App ──► UI ──► Core ◄── Data
 └──────────────────────► Data
Tests ──► (對應專案)；Reference 只引用 Core 的「輸入資料型別」，不呼叫任何 Core 演算法
```

### 引入的套件（全部免費授權）
| 套件 | 用途 |
|---|---|
| Microsoft.Data.Sqlite 9.0.x | SQLite 存取（不用 EF Core：要精準控制交易、PRAGMA、user_version） |
| Microsoft.AspNetCore.Components.WebView.WindowsForms 9.0.x | BlazorWebView |
| xunit、xunit.runner.visualstudio、Microsoft.NET.Test.Sdk | 測試 |
| bunit | 元件測試 |

**不引入** FsCheck：性質測試用自寫產生器（固定種子、失敗時印出 seed），減少相依、控制力較好。CSV parser 自寫（RFC 4180 子集）。Big5 用 .NET 內建 `CodePagesEncodingProvider`。

---

## 3. 領域模型（Core）

### 3.1 核心原則
- **不可變狀態**：`TournamentState` 為 immutable record（`ImmutableArray`/`ImmutableDictionary`）。
- **每個操作 = 純函式**：`Apply(state, command) → Result<Transition, DomainError>`，`Transition = (NewState, AuditEntries, Description)`。
- **衍生資料不儲存**：排名、同分值、「過期」狀態、交叉表都由狀態即時計算 → 不會與資料不一致，undo 也自然正確。
- 分數全為 `int` 半分；同分值（Buchholz、SB 等）也為 `int`/`long`。全專案禁止 `double/float/decimal` 表示分數（加 Roslyn analyzer 規則或測試掃描 `BannedApiAnalyzers`？→ 見 ❓Q8）。
- 排序一律 `StringComparer.Ordinal`；時間一律 `TimeProvider`。

### 3.2 實體

```
TournamentState
  Settings   : Name, Format(Swiss|RoundRobin|DoubleRoundRobin|SingleElimination),
               SwissRounds(1..15), ByePoints(0|1|2 半分), ThirdPlaceMatch(bool)
  Players    : PlayerId(內部 int) → Player{ RegNo, Name, Rating(0..3000),
               JoinRound?(遲到加入起始輪), WithdrawRound?(退賽生效輪) }
  NextRegNo
  Rounds     : RoundNo → Round{ Status(Draft|Published), Games[], Bye?,
               EngineProposalHash(發布時引擎建議的指紋), ForcedOverride(bool) }
  Game       : Board(桌號), White(先手), Black(後手), Result
  Result     : None | WhiteWin | Draw | BlackWin | WhiteForfeit | BlackForfeit | DoubleForfeit
  Bracket    : (淘汰賽) Matches[]{ Id, Round, Slot, SourceA/SourceB(Seed n | WinnerOf m | LoserOf m),
               Winner?, IsWalkover }
```

- 「輪次完成」＝已發布且所有對局皆有結果（輪空自動登錄）。
- 內部 `PlayerId` 與 `RegNo` 分離（驗收 5：重新指派內部 id 結果不變）。

### 3.3 Session（應用服務，放 Core，只依賴介面）

```
TournamentSession
  State (目前狀態)
  Execute(command)          → 驗證 → 計算 Transition → repository.Commit(old,new,audit) [單一交易] → push undo
  Undo() / Redo()           → 以「還原到前一個狀態」作為一個 Transition 走同一條 Commit 管線，並寫入稽核紀錄
  依賴：ITournamentRepository, TimeProvider, IAuditContext(電腦名稱/使用者)
```

### 3.4 復原／重做
- 快照式：undo 堆疊存的是前一個 `TournamentState`（不可變 + 結構共享，50 層成本極低；預設上限 100）。
- 好處：驗收 20「復原後雜湊與之前完全相同」幾乎由構造保證；`StateHasher` = 正規化序列化 → SHA-256。
- 「復原不得繞過規則」的實作：
  1. Undo/Redo 也是一次 Commit（單一交易）並寫稽核紀錄（稽核紀錄本身**不會**被復原）。
  2. 背景配對執行中禁止 undo/redo。
  3. 過期狀態為衍生值，復原後自動重新評估，不會殘留錯誤標記。
- 堆疊只存在記憶體（關檔即清空）→ ❓Q1。

---

## 4. 瑞士制配對引擎（最關鍵）

### 4.1 問題轉換
本輪可配對選手依「賽前排名」編號 1..n。n 為奇數時加入虛擬點 **D**（配到 D = 輪空）。
所求 = 完全圖上的**最小成本完美匹配**，比較依字典序 ①②③④⑤。

- ①②③④ 都可以**逐邊相加**：
  - 一般邊 (i,j)：`① = 曾交手?1:0`，`④ = (score_i − score_j)²`
  - 輪空邊 (i,D)：`② = 曾輪空?1:0`，`③' = n − rank_i`（越後面越小）
  - 合成整數成本 `P = ((①·2 + ②)·(n+1) + ③')·(④max+1) + ④`，上界約 2⁴⁰，`long` 足夠。
- ⑤（排序後的 (hi,lo) 字典序）**可證明等價於**：反覆取「目前最小未配對排名 h」，讓 h 的對手排名**越大越好**（推導：已固定的前綴相同時，下一個 hi 必然是最小未配對者，因此 hi 永遠不會先分出勝負，只比 lo）。這不是可加的，但可用「分層權重」精確編碼前 k 個最小未配對者：
  - 第 t 層（第 t 小的未配對者 u_t 作為 hi 時）權重 = `B^(k−t) · lo`，B = n+1，保證上一層嚴格支配下一層。

### 4.2 演算法：分段固定（iterative fixing）
```
remaining = 所有點（含 D）
while remaining 非空:
    k = 在 Int128 範圍內能容納的最大層數（P 佔 ~40 bits，B^k 佔其餘；n=400 時 k≈9）
    權重 w(e) = (Pmax − P(e))·Scale + Lex_k(e)       // 最大化 w
    M = MaxWeightPerfectMatching(remaining, w)       // Edmonds blossom O(n³)，整數權重
    固定 M 中 hi ∈ {u_1..u_k} 的所有配對；從 remaining 移除
    回報進度 = 已固定人數 / 總人數（單調遞增）
```
- 正確性：每一段都同時以 P 為最高優先，所以固定的配對都屬於某個全域最佳解；分層權重保證前 k 個 hi 的對手是字典序最大。最終解在 ①～⑤ 下**唯一**（⑤ 完全決定配對，③ 決定輪空者）→ 決定性（驗收 5）由數學保證，不依賴輸入順序。
- 效能估算：n=400 約 45 段、規模遞減，總量約 12×(400³) 基本運算；blossom 在 C# 中 400 點稠密圖預估 50–150ms/次 → 合計約 1–2 秒。若實測超標，備案：改用 256-bit 自訂整數讓 k≈25（段數減為 1/3）。
- Blossom 實作：移植 Van Rantwijk `mwmatching`（公開演算法，O(n³)，以泛型整數權重；權重乘 2 保證對偶變數為整數）。
- 取消：`CancellationToken` 在 blossom 每個 stage 檢查；引擎是純函式、不碰資料庫 → 取消後檔案位元組不變（驗收 9）。成功後才在單一交易寫入草稿。

### 4.3 先後手（配對確定後）
依序比較：先手次數少者先手 → 上一場為後手者先手 → 賽前排名高者先手。輪空、棄權場次是否計入「先手次數」與「上一場」→ ❓Q4（預設：只計有實際對局或單方棄權的已排定場次，輪空不計）。

### 4.4 手動交換與評估
- `PairingEvaluator.Evaluate(pairing) → ObjectiveVector(①,②,③,④,⑤序列) + Violations[]`（重複對戰、重複輪空）。
- UI 交換兩人後即時呼叫（純計算，O(n)），顯示與最佳解的差異；違規時需勾選「強制採用」，寫稽核紀錄，Round.ForcedOverride = true。

### 4.5 過期判定（驗收 14）
- 發布時存 `EngineProposalHash`（當時引擎建議配對的指紋，含先後手）。
- 成績更正後，對每個較後的已發布輪次 k：以「第 k 輪之前的現有成績＋第 k 輪的參賽者集合」重算引擎建議，若指紋 ≠ 儲存值 → 標示「過期」。
- 用「引擎建議 vs 引擎建議」比較，而不是「引擎建議 vs 已發布」，避免曾手動交換的輪次永遠被誤判過期 → ❓Q3。
- 系統絕不自動改動已發布配對；「從第 k 輪起重新配對」= 清除第 k 輪起的配對與成績（先列出影響、確認）→ 產生第 k 輪新草稿，可一步復原。
- 重算可能需多次 400 人配對，於背景執行並顯示進度。

### 4.6 驗證用參考解（測試專案內，與正式碼獨立）
| 參考解 | 規模 | 比較內容 |
|---|---|---|
| `BruteForcePairing`：遞迴列舉所有匹配（含輪空選擇） | N ≤ 12（11 人約 1 萬種） | 完整配對逐案一致（≥500 組隨機歷史） |
| `BitmaskDpPairing`：`dp[mask]` 以最低位元配對 | N ≤ 18（2¹⁸×18） | ①～④ 最佳值一致（≥200 組） |
- 參考解自行從原始歷史（對局清單）計算排名、曾交手、分差，不呼叫 Core 的任何計算；只共用輸入 DTO。

---

## 5. 循環賽

- **輪次**：圓桌法（circle method）。奇數人數加入虛擬點 → 每輪一人輪空，單循環每人恰好輪空一次，雙循環兩次。
- **先後手**：與輪次分離，採奇偶規則——對 i<j（i、j 為種子序），`i+j 為奇數 → i 先手，否則 j 先手`。此規則讓每人先後手差 ≤ 1（偶數場時恰好相等）；以 N=2..64 性質測試驗證。
- **雙循環**：第二循環複製第一循環輪次並互換先後手。
- 種子序依（積分高→低、註冊編號小→大）。
- 退賽：生效輪起之對局判退賽者棄權，對手得 2 半分；輪空依設定計分。

## 6. 單淘汰

- 籤表大小 S = 2^⌈log₂N⌉；標準種子位置以遞迴產生（1v8、4v5、2v7、3v6…）；種子 > N 視為不存在 → 其對手（必為前 S−N 名種子）第 1 輪輪空。
- 每場由 `Source` 描述來源（種子／某場勝者／某場敗者[季軍戰]），形成 DAG。
- **更正勝者**：沿 DAG 找出所有依賴舊勝者的後續場次（勝者、結果、季軍戰）→ 列出清單 → 確認 → 一次 Transition 清除（可一步復原）。
- 退賽者的場次：對手以 walkover 晉級。
- 名次：冠軍、亞軍、季軍戰勝負、其餘同輪淘汰者並列。

## 7. 排名與同分判定

- `StandingsCalculator.Compute(state) → Standing[]`，每列含：名次、總分、直接對戰值（適用時）、Buchholz Cut-1、Buchholz、SB、實際勝場。
- 分群演算法：
  ```
  Rank(group, startCriterion=2):
     for c in startCriterion..6:
        values = criterion_c(group)          // 直接對戰只在群組內兩兩都有實際對局時適用
        若 values 把 group 分成多個子群 → 依值排序，對每個 size>1 的子群 Rank(sub, 2)；return
     全部相同 → 並列（同名次，畫面依註冊編號排列）
  ```
- 虛擬對手：選手自己的每個輪空／棄權（含棄權勝、棄權負、雙方棄權）場次，分數 = 該輪前累計 + 1 半分 ×（已完成輪數 − 該輪 + 1）。遲到加入前的輪次不產生虛擬對手；退賽後的未排定輪次不產生虛擬對手 → ❓Q5。
- 名次：競賽排名（1,2,2,4）。400 人重算目標 < 200ms（預估 < 20ms）。

## 8. 檔案、鎖定、備份（Data）

### 8.1 .tourney 檔
- SQLite，`PRAGMA application_id = 0x54445348`（'TDSH'）辨識本程式檔；`PRAGMA user_version` = schema 版本。
- `journal_mode=DELETE`（**不用 WAL**：WAL 需共享記憶體，在網路磁碟上不可靠）、`synchronous=FULL`。
- 開檔流程：先以 **唯讀** 驗證 header／application_id／user_version／`integrity_check` → 失敗則給明確訊息且不修改檔案；版本過新 → 拒絕；版本較舊 → 先複製 `.v{舊版}.bak` 再於交易內遷移。
- 寫入：`Commit(old, new, audit)` 以差異比對（不可變物件參考相等快速跳過）在單一交易中 upsert/delete 變動列 + append 稽核紀錄。
- 稽核表 `audit_log` 以 trigger 禁止 UPDATE/DELETE（只能新增）。
- 輪次完成時以 SQLite Online Backup API 產生 `{檔名}.r{輪}.bak`。

### 8.2 開檔鎖定（邏輯在 Core，I/O 在 Data）
- 鎖檔：`{檔名}.tourney.lock`，開啟後**整個 session 持有 handle**（`FileShare.Read`，拒絕他人寫入／刪除；SMB 伺服器會強制執行），內容記錄電腦名稱、PID、程序啟動時間、開啟時間、session GUID。
- Core 中的 `LockArbiter`（純邏輯，注入 `IProcessInspector`、`TimeProvider`）：

| 情況 | 判定 |
|---|---|
| 無鎖檔 | 直接取得 |
| 鎖檔 handle 被持有中 | 拒絕，顯示持有者（無法強制：對方程序確實在使用） |
| handle 未被持有、記錄為本機、程序已不存在（PID＋啟動時間比對，防 PID 重用） | 殘留鎖 → 確認後接手 |
| handle 未被持有、記錄為本機、程序仍存在 | 視為持有中，拒絕 |
| handle 未被持有、記錄為他機 | 不可自動接手；輸入固定確認字串（例如 `我確定要強制接手`）後接手 |
- 正常關閉：釋放 handle 並刪除鎖檔；當掉：handle 由 OS 釋放、鎖檔殘留 → 依上表處理。

## 9. UI（Blazor Hybrid）

- 頁面：賽事設定、選手（`<Virtualize>`）、輪次與配對（草稿交換、違規提示、強制採用）、成績登錄、積分榜（含同分值欄）、交叉表、淘汰籤表（SVG）、異動紀錄。
- 背景工作：配對／過期重算以 `Task.Run` + `IProgress<int>` + 取消按鈕。
- 錯誤：所有 Session 呼叫包在統一的錯誤邊界，轉成可讀訊息（不崩潰）；WinForms 端掛 `Application.ThreadException` / `AppDomain.UnhandledException` 最後防線。
- 介面抽象（UI 定義、App 實作）：`IPrintService`（WebView2 `ShowPrintUI`）、`IFileDialogService`、`IExportService`。
- 列印／匯出：列印專用 CSS（`@media print`、分頁）；HTML 匯出用 .NET 8+ 的 `HtmlRenderer` 把同一組 Razor 元件渲染成單一 HTML 檔（樣式內嵌）；CSV 為 UTF-8 with BOM。
- 全部資源本機（CSS 自寫，不用 CDN，字型用系統字型 Microsoft JhengHei）。
- WebView2 偵測：啟動時 `CoreWebView2Environment.GetAvailableBrowserVersionString()`，失敗 → 顯示 WinForms 說明視窗與下載網址文字。

### 版面計算（Core.Layout，不引用 UI）
- `BracketLayout`：每場的格子矩形、連線折線端點、依頁面高度切分子樹分頁。
- `CrosstableLayout`：格子座標、行列雙向分頁（400×400 會切成多頁），保證每格恰出現一次。
- UI 只把 layout model 轉成 `<svg>` 元素；數字格式化一律 InvariantCulture → SVG 決定性。

## 10. CSV 匯入
- 編碼偵測：有 UTF-8 BOM → UTF-8；否則以 strict UTF-8 解碼，失敗 → Big5。
- 欄位：`註冊編號,姓名,積分`（標題列可為中文或英文 `RegNo,Name,Rating`）→ ❓Q2。
- 逐列驗證，錯誤列記錄（行號、原因），其餘照常匯入；整批匯入是一個 Transition（可一步復原）。

## 11. 測試策略

| 類別 | 內容 | 估計數量 |
|---|---|---|
| Core 單元 | 配對範例、先後手、循環、淘汰、同分手算、鎖定、undo、版面、CSV | ~130 |
| 性質測試（≥7） | ①窮舉一致 ②DP 一致 ③決定性 ④循環賽性質 ⑤淘汰籤表性質 ⑥同分：登錄順序無關 ⑦分數守恆 ⑧undo 雜湊 ⑨版面無重疊／分頁不漏不重 | 9 |
| Data | 往返無損、損毀／過新／非本程式、遷移備份、交易、稽核唯新增 | ~25 |
| UI（bUnit） | 虛擬化列數、SVG 決定性、交換違規提示、確認對話 | ~25 |
| 效能 | 400 人 9 輪、積分榜 200ms（`[Trait("Category","Performance")]`） | 2 |
- 每個性質測試：固定 seed，失敗訊息印出 `seed=…` 可單獨重跑。

## 12. CI（GitHub Actions）
`windows-latest` → setup-dotnet 9.0.x → restore → build（`-warnaserror`）→ test → `dotnet publish src/TourneyDesk.App -c Release -r win-x64 --self-contained false` → upload-artifact。README 放徽章。→ ❓Q6

---

## 13. 開發里程碑（每階段可編譯、可測試、可 commit）

| # | 內容 | 預估 |
|---|---|---|
| M0 | Solution 骨架、CI、Directory.Build.props（nullable、warnings as errors、analyzers） | 3h |
| M1 | Core 領域模型、Session、undo、雜湊 | 6h |
| M2 | 排名與同分判定＋手算範例 | 8h |
| M3 | Blossom 移植＋瑞士制引擎＋參考解＋性質測試＋效能調校 | 16h |
| M4 | 先後手、手動交換評估、過期判定、從第 k 輪重新配對、退賽／遲到 | 8h |
| M5 | 循環賽 | 4h |
| M6 | 淘汰籤表、連鎖清除 | 6h |
| M7 | Data：schema、遷移、差異寫入、稽核、備份；鎖定 | 8h |
| M8 | CSV 匯入 | 3h |
| M9 | 版面計算（籤表、交叉表、分頁） | 6h |
| M10 | UI 各頁面、背景工作、列印、匯出、WebView2 偵測 | 14h |
| M11 | 範例資料與手算文件、400 人產生器、README、截圖 | 8h |
| | **合計** | **約 90h** |

---

## 14. 已確認決策（皆採建議）

| # | 問題 | 我的建議 |
|---|---|---|
| Q1 | 復原堆疊是否需跨關檔保存？ | 只在記憶體（關檔清空）；稽核紀錄永久保存 |
| Q2 | CSV 欄位與註冊編號：CSV 可否指定註冊編號？ | 可選；空白則自動配發；與既有或檔內重複 → 錯誤列 |
| Q3 | 過期判定基準 | 比較「現在的引擎建議」與「發布當時的引擎建議」（含先後手） |
| Q4 | 先後手計數是否含輪空與棄權場次 | 輪空不計；單方／雙方棄權仍依排定的先後手計入 |
| Q5 | 退賽後輪次、遲到前輪次是否產生虛擬對手 | 皆不產生（只對「已排定」的輪空／棄權產生） |
| Q6 | GitHub repo：是否已有遠端？發佈檔要 framework-dependent 或 self-contained？ | 先本地 git；發佈 self-contained 單一資料夾（使用者免裝 .NET） |
| Q7 | 效能測試在 CI 是否也要強制門檻？（雲端 runner 較慢） | CI 執行但門檻×2；README 數據以本機 i7-13700/32GB 實測 |
| Q8 | 「禁止 double/float 表示分數」是否要以工具強制？ | 加一個測試以反射掃描 Core 公開型別中名稱含 Score/Points 的成員不得為浮點數 |
