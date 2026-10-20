# TourneyDesk 教學：從需求到程式碼

這份教學以 TourneyDesk（賽事裁判台）為教材，帶你走一遍一個「真的會被拿去用」的桌面系統：從讀懂需求、做系統分析，到架構切分、程式寫法、核心演算法、資料保存、UI、測試與發佈。

每一章都以專案裡實際存在的程式碼為例，並附上檔案路徑，你可以邊讀邊打開原始碼對照。

> 適合對象：已經會寫 C#（或其他物件導向語言），想學「中大型程式怎麼組織」、「演算法怎麼落地成可靠的產品程式」的開發者。
> 不需要事先懂任何賽制，第 1 章會從頭介紹。

---

## 章節

| 章 | 主題 | 你會學到 |
|---|---|---|
| [01 系統分析](01-system-analysis.md) | 領域、使用者、需求、驗收標準 | 如何把一份題目拆成模組、找出困難點、把模糊處變成明確決策 |
| [02 系統架構](02-architecture.md) | 分層、專案相依、資料流 | 為什麼 Core 不能引用 UI／SQLite；如何用測試守住架構 |
| [03 領域模型與命令管線](03-domain-and-commands.md) | 不可變狀態、Command、Session、Undo | 「純函式 + 單一管線」如何讓復原、寫檔、錯誤處理都變簡單 |
| [04 瑞士制配對演算法](04-swiss-pairing.md) | 最大權匹配、Blossom、對偶、字典序最佳化 | 把排賽規則轉成圖論問題，並證明結果唯一、可重現 |
| [05 循環賽、淘汰賽與排名](05-round-robin-knockout-standings.md) | 圓桌法、籤表 DAG、同分判定 | 用數論與遞迴把規則寫成可驗證的程式 |
| [06 資料層與檔案鎖定](06-data-and-locking.md) | SQLite、差異寫入、遷移、當機回復、網路磁碟鎖 | 「每個動作都立刻安全寫入」怎麼做；多人共用檔案怎麼防衝突 |
| [07 UI：Blazor Hybrid](07-ui-blazor-hybrid.md) | WinForms + BlazorWebView、Workspace、背景工作 | UI 只負責呈現；耗時計算如何可取消、不卡畫面 |
| [08 測試策略](08-testing.md) | TDD、性質測試、參考解、架構測試、效能測試 | 怎麼「證明」演算法是對的，而不只是「跑過幾個例子」 |
| [09 建置、CI 與發佈](09-build-ci-release.md) | 建置設定、GitHub Actions、self-contained 發佈 | 讓每次 commit 都自動編譯、測試、打包 |
| [10 案例研究與練習](10-case-studies-and-exercises.md) | 外部審查發現的 bug、練習題 | 從真實錯誤學習；動手延伸系統 |

## 建議閱讀順序

- **想了解整體**：01 → 02 → 03 → 07。
- **想學演算法**：01（第 2 節賽制介紹）→ 04 → 05 → 08 的「參考解」一節。
- **想學工程實務**：02 → 03 → 06 → 08 → 09 → 10。

## 開始之前

```powershell
git clone https://github.com/hn83320589/TourneyDesk.git
cd TourneyDesk
dotnet build TourneyDesk.sln
dotnet test TourneyDesk.sln            # Debug：效能測試會略過
dotnet test TourneyDesk.sln -c Release # Release：包含效能測試
dotnet run --project src/TourneyDesk.App
```

需要 .NET 9 SDK 與 Windows（UI 使用 WebView2）。Core、Data 與它們的測試在任何平台都能編譯與執行。

## 相關文件

- [`docs/design.md`](../design.md)：開發前寫的系統分析與架構設計（含待決事項 Q1～Q8 與實作紀錄）。
- [`docs/test-report.md`](../test-report.md)：測試報告，逐項對照 25 條驗收標準。
- [`README.md`](../../README.md)：使用說明、功能清單、效能數據。

本教學與上面三份文件互補：design.md 記錄「當時決定了什麼」，這份教學解釋「為什麼這樣做、怎麼做到的」。
