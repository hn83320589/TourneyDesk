# 賽事裁判台 TourneyDesk

社區桌球社的桌面賽事工具：瑞士制／單循環／雙循環／單淘汰的自動配對、成績登錄、排名與列印。

> 開發中。系統分析與架構設計見 [docs/design.md](docs/design.md)。

## 建置與執行

需求：Windows 10/11、.NET 9 SDK（9.0.x）、Microsoft Edge WebView2 Runtime。

```powershell
dotnet build TourneyDesk.sln
dotnet test TourneyDesk.sln
dotnet run --project src/TourneyDesk.App
```

發佈 win-x64（self-contained，使用者不需另裝 .NET）：

```powershell
dotnet publish src/TourneyDesk.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## 專案結構

| 專案 | 說明 |
|---|---|
| `src/TourneyDesk.Core` | 純 net9.0：賽制演算法、配對最佳化、同分判定、鎖定邏輯、版面計算、復原堆疊、檔案層介面 |
| `src/TourneyDesk.Data` | SQLite（Microsoft.Data.Sqlite）實作、schema 版本管理、鎖定檔 |
| `src/TourneyDesk.UI` | Razor Class Library（不引用 WinForms） |
| `src/TourneyDesk.App` | WinForms 外殼 + BlazorWebView |
| `tests/*` | xUnit / bUnit 測試；參考解（窮舉、位元 DP）位於 `TourneyDesk.Core.Tests/Reference` |
