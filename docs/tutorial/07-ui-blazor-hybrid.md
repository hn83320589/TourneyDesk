# 第 7 章　UI：Blazor Hybrid

> 本章目標：了解如何用 WinForms + BlazorWebView 以網頁技術做桌面 UI；如何讓 UI 只負責「呈現與轉送」，把狀態與規則留在 Core；以及如何讓耗時計算在背景執行、可取消、不卡畫面。

主要程式：`src/TourneyDesk.App/`、`src/TourneyDesk.UI/`

---

## 7.1 Blazor Hybrid 是什麼？

```
┌─────────────── WinForms 視窗（MainForm） ───────────────┐
│  ┌──────────── BlazorWebView（WebView2） ─────────────┐ │
│  │  HTML / CSS / SVG 畫面                             │ │
│  │  ↑ 由 Razor 元件產生（Shell、*View、*Svg）         │ │
│  └────────────────────────────────────────────────────┘ │
│  Razor 元件的 C# 程式碼直接在 .NET 程序內執行            │
│  （沒有 HTTP、沒有伺服器、沒有 WebAssembly）             │
└──────────────────────────────────────────────────────────┘
```

- 畫面用 HTML/CSS 繪製，顯示於 WebView2（Edge 的瀏覽器引擎）。
- 元件的 C# 程式碼在**同一個 .NET 程序**裡執行，可以直接呼叫 Core、存取檔案。
- 好處：表格、SVG 籤表、列印版面都用網頁技術處理，比 WinForms 原生控制項容易得多；仍然是可以離線執行的桌面程式。

### 啟動流程

```csharp
// src/TourneyDesk.App/Program.cs
[STAThread]
private static void Main(string[] args)
{
    ApplicationConfiguration.Initialize();
    // 最後防線：任何未處理的例外都轉成可讀訊息
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    Application.ThreadException += (_, e) => ShowFatal(e.Exception);
    AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

    if (!WebView2RuntimeCheck.IsInstalled())        // 沒裝 WebView2 → 說明如何安裝，而不是當掉
    {
        WebView2RuntimeCheck.ShowMissingRuntimeDialog();
        return;
    }
    Application.Run(new MainForm(args.FirstOrDefault()));   // 可用命令列參數直接開檔
}
```

`MainForm` 建立 `BlazorWebView`、組裝 DI（第 2 章），把根元件 `Shell` 掛到 `#app`。

---

## 7.2 為什麼 UI 獨立成 Razor Class Library？

`TourneyDesk.UI` 是 `net9.0` 的 Razor Class Library，**不引用 WinForms 或 WebView2**（有架構測試保護）。平台相關的功能都透過介面注入：

| 介面 | UI 需要的能力 | App 的實作 | 測試的實作 |
|---|---|---|---|
| `ITournamentGateway` | 開檔、建檔、鎖定 | `FileGateway`（Data 層） | `FakeGateway`（記憶體） |
| `IFileDialogService` | 選擇開啟／儲存位置 | WinForms 對話框 | `FakeDialogs` |
| `IPrintService` | 列印 | WebView2 `ShowPrintUI` | `FakePrinter` |

結果是：所有頁面都能用 bUnit 在測試中渲染、點按鈕、檢查 DOM，完全不用開視窗。

---

## 7.3 Workspace：UI 的應用程式狀態

`Workspace`（`src/TourneyDesk.UI/Services/Workspace.cs`）是 UI 唯一的狀態中心：

```csharp
public sealed class Workspace(ITournamentGateway gateway, TimeProvider time) : IDisposable
{
    public TournamentSession? Session { get; private set; }
    public TournamentState? State => Session?.State;
    public Notice? Notice { get; private set; }                 // 頂部通知列
    public string? BackgroundWork { get; private set; }         // 背景工作描述
    public int BackgroundProgress { get; private set; }
    public ImmutableArray<int> StaleRounds { get; private set; } // 過期輪次
    public event Action? Changed;                                // 任何會影響畫面的變化

    public CommandResult Execute(ITournamentCommand command, string? successMessage = null) { ... }
    public CommandResult Undo() { ... }
    public CommandResult Redo() { ... }
}
```

它的責任：

1. **包裝 Session**：頁面呼叫 `Workspace.Execute(command)`，結果自動轉成通知（成功或錯誤訊息）。
2. **不讓例外跑到畫面上**：所有已知的失敗（規則違反、寫檔失敗、鎖定失敗）都轉成可讀訊息。
3. **管理背景工作與過期判定**（7.5、7.6）。

### 頁面怎麼寫

頁面只做兩件事：**把狀態畫出來**、**把使用者的動作轉成命令**。

```razor
@* src/TourneyDesk.UI/Pages/ResultsView.razor（概念） *@
<select @onchange="e => Set(round, board, (GameResult)Enum.Parse(...))">...</select>

@code {
    private void Set(int round, int board, GameResult result) =>
        Workspace.Execute(new SetResult(round, board, result));
}
```

頁面裡沒有任何規則判斷（例如「草稿不能登錄成績」）。規則在命令裡；頁面只負責顯示命令回傳的錯誤訊息。這樣規則只有一份，測試 Core 就等於測試規則。

### 畫面更新：Changed 事件

```razor
@* src/TourneyDesk.UI/Shell.razor *@
@code {
    protected override void OnInitialized() => Workspace.Changed += Refresh;
    private void Refresh() => InvokeAsync(StateHasChanged);    // 可能從背景執行緒觸發，要切回 UI 同步內容
    public void Dispose() => Workspace.Changed -= Refresh;     // 一定要取消訂閱，避免記憶體洩漏
}
```

`InvokeAsync` 很重要：過期判定在背景執行緒完成後會觸發 `Changed`，而 Blazor 的渲染必須在它的同步內容中進行。

---

## 7.4 錯誤處理的層次

| 層級 | 處理方式 |
|---|---|
| 規則違反（`DomainException`） | Session 轉成 `CommandResult.Fail` → Workspace 顯示錯誤通知 |
| 寫檔失敗（`StorageException`） | 同上，狀態不變 |
| 元件渲染時的未預期例外 | Shell 的 `ErrorBoundary` 顯示「畫面發生錯誤」與「返回」按鈕 |
| 其他未處理例外 | `Program` 的全域處理器顯示訊息框，不讓程式直接崩潰 |

```razor
<ErrorBoundary @ref="_errors">
    <ChildContent> @* 目前分頁 *@ </ChildContent>
    <ErrorContent Context="ex">
        <p class="notice error">畫面發生錯誤：@ex.Message</p>
        <button @onclick="Recover">返回</button>
    </ErrorContent>
</ErrorBoundary>
```

原則：**每個錯誤都在最適合的層級處理，且一定有可讀的訊息**；不無聲吞掉例外，也不讓使用者看到堆疊追蹤。

---

## 7.5 背景工作：可取消、不卡畫面

產生瑞士制配對可能要半秒以上，不能在 UI 執行緒上算。`RunInBackgroundAsync` 把這件事包成一個通用機制：

```csharp
// src/TourneyDesk.UI/Services/Workspace.cs（節錄）
public async Task<T?> RunInBackgroundAsync<T>(string description,
    Func<TournamentState, IProgress<int>, CancellationToken, T> work) where T : class
{
    using var cts = new CancellationTokenSource();
    var snapshot = Session.State;                              // 1. 取不可變快照
    var scope = Session.BeginBackgroundWork(description);      // 2. 期間 Session 拒絕任何變更
    BackgroundWork = description;
    var progress = new ThrottledProgress(p => { BackgroundProgress = p; Raise(); });
    try
    {
        return await Task.Run(() => work(snapshot, progress, cts.Token), cts.Token);   // 3. 背景計算
    }
    catch (OperationCanceledException)
    {
        ShowInfo($"已取消「{description}」，賽事內容未變更。");
        return null;
    }
    finally
    {
        scope.Dispose();                                       // 4. 解除鎖定
        BackgroundWork = null;
        Raise();
    }
}
```

使用方式（`RoundsView.razor`）：

```csharp
private async Task GenerateAsync()
{
    var round = State.Rounds.Length + 1;
    var result = await Workspace.RunInBackgroundAsync($"計算第 {round} 輪配對",
        (s, progress, ct) => SwissRoundPlanner.Propose(s, round, progress, ct));      // 背景：純計算
    if (result is not null)
        Workspace.Execute(new GenerateSwissRound(round, result.Pairing));             // 前景：命令寫入
}
```

設計重點：

| 重點 | 理由 |
|---|---|
| 背景只讀不可變快照 | 不需要任何鎖，也不怕 UI 同時產生新狀態 |
| 背景期間 Session 拒絕變更（含復原） | 保證「計算所依據的狀態」就是「寫入時的狀態」 |
| 計算與寫入分開 | 取消時什麼都沒寫，檔案與取消前逐位元組相同 |
| `ThrottledProgress` | 只在百分比改變時通知，避免每秒上千次重繪 |

`BackgroundOverlay` 元件顯示進度條與「取消」按鈕，提醒「計算期間無法修改賽事；取消後不會留下任何變更」。

---

## 7.6 過期判定：自動在背景重算

每次狀態改變，Workspace 會在背景重新判定過期輪次：

```csharp
private void OnStateChanged()
{
    Raise();
    _ = RefreshStalenessAsync();          // 不等待：畫面先更新，判定結果稍後到
}

public async Task RefreshStalenessAsync()
{
    lock (_staleGate)
    {
        _staleCts?.Cancel();              // 1. 狀態又變了 → 取消上一次判定
        snapshot = current;
        cts = new CancellationTokenSource();
        _staleCts = cts;
    }
    var stale = await Task.Run(() => SwissStaleness.FindStaleRounds(snapshot, 2, null, cts.Token, _proposalCache), cts.Token)
        .ConfigureAwait(false);
    lock (_staleGate)
    {
        if (ReferenceEquals(State, snapshot))   // 2. 只有在狀態仍是當初那份時才採用結果
            StaleRounds = stale;
    }
}
```

三個並行陷阱與對策：

1. **連續快速操作**：每次新的判定會取消上一次，只保留最新的。
2. **過時的結果**：判定完成時，狀態可能已經又變了。用 `ReferenceEquals(State, snapshot)` 確認——不可變狀態讓這個檢查又快又可靠。
3. **跨執行緒存取**：`_staleGate` 鎖保護 `_staleCts` 與 `StaleRounds` 的讀寫。

`_proposalCache` 是第 4 章介紹的引擎建議快取，開啟另一個賽事時重建。

---

## 7.7 大量資料：虛擬化

400 位選手的列表，如果一次產生 400 列 DOM，捲動會變慢。用 Blazor 內建的 `Virtualize`：

```razor
@* src/TourneyDesk.UI/Pages/PlayersView.razor *@
<Virtualize Items="Players" ItemSize="32" OverscanCount="5" Context="p">
    <tr> ... </tr>
</Virtualize>
```

只渲染可視範圍（加上少量緩衝）的列。bUnit 測試模擬 520 px 的可視區，確認只渲染 10～40 列。

---

## 7.8 版面計算與 SVG 繪製

籤表與交叉表的版面分成兩層：

1. **Core.Layout**：計算每個方框的整數座標、連線的折點、分頁（`BracketLayout`、`Crosstable`）。
2. **UI.Rendering**：把版面模型轉成 `<svg>`。

```razor
@* src/TourneyDesk.UI/Rendering/BracketSvg.razor：只負責繪製，座標全部來自版面計算 *@
<svg class="bracket" width="@Page.Width" height="@Page.Height" viewBox="0 0 @Page.Width @Page.Height">
    @foreach (var c in Page.Connectors)
    {
        <polyline class="link" points="@Points(c)" />
    }
    @foreach (var box in Page.Boxes)
    {
        <rect x="@box.X" y="@box.Y" width="@box.Width" height="@box.Height" rx="4" />
        ...
    }
</svg>
```

為什麼要分開？

- **版面規則可以在 Core 用單元測試驗證**：方框不重疊、連線端點正確、父場次在兩個子場次中間、分頁時每場恰好出現在一頁。這些都不需要瀏覽器。
- **座標全是整數**，SVG 輸出具決定性（同樣的輸入產生完全相同的字串），可以直接比對。
- 想改成輸出 PDF 或 PNG，只要換掉繪製層。

籤表分頁的想法：依頁面高度把第 1 輪切成大小為 2 的冪次的子樹，逐層往上遞迴，每一頁是一棵完整的子樹。

---

## 7.9 列印與匯出

### 列印

列印就是「印目前的網頁」：WebView2 的 `ShowPrintUI` 開啟列印預覽，版面由 CSS 控制：

```css
/* src/TourneyDesk.UI/wwwroot/app.css */
@media print {
    .topbar, .tabs, .notice, .overlay, .screen-only, .page-header { display: none !important; }
    .print-only { display: block; }
    .print-page { page-break-after: always; break-after: page; }
}
```

只在螢幕上有意義的東西（工具列、分頁籤、「與最佳解比較」面板）標上 `screen-only`，列印時隱藏。這個規則是實機開啟列印預覽時才發現漏掉的——自動測試看不到列印結果，所以補了回歸斷言檢查 CSS class。

### 匯出 CSV

```csharp
// src/TourneyDesk.Core/Import/Csv.cs
public static string FormatRow(IEnumerable<string> values) =>
    string.Join(',', values.Select(raw =>
    {
        var v = raw.Length > 0 && "=+-@\t\r".Contains(raw[0], StringComparison.Ordinal) ? "'" + raw : raw;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : v;
    }));
```

- **加 UTF-8 BOM**：Excel 才會正確顯示中文。
- **防公式注入（CSV injection）**：選手姓名可能來自匯入的 CSV。如果有人把姓名設成 `=HYPERLINK(...)`，匯出後用 Excel 開啟就會被當成公式執行。以 `= + - @ Tab CR` 開頭的欄位前加單引號。

### 匯出 HTML

用 .NET 8 起提供的 `HtmlRenderer`，把同一個 Razor 元件（例如 `StandingsTable`）渲染成 HTML 字串，加上內嵌 CSS，存成獨立的 `.html` 檔。**畫面、列印、匯出共用同一個元件**，不會出現三份不一致的版面。

---

## 7.10 本章小結

1. Blazor Hybrid：網頁技術畫 UI，C# 在本機程序執行，仍是離線桌面程式。
2. UI 是不引用平台的 Razor Class Library；平台功能用介面注入，所以能用 bUnit 測試。
3. 頁面只負責呈現與轉送命令，規則全在 Core。
4. 背景工作：取快照、鎖住 Session、背景計算、前景寫入；取消時什麼都不寫。
5. 背景判定用「取消舊的 + 確認狀態沒變才採用」處理並行。
6. 版面計算在 Core（可測試、整數、決定性），UI 只繪製。
7. 列印靠 `@media print`；匯出 CSV 要加 BOM 並防公式注入。

下一章：怎麼證明這一切都是對的？→ [第 8 章　測試策略](08-testing.md)
