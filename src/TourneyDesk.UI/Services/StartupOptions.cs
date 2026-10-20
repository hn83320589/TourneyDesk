namespace TourneyDesk.UI.Services;

/// <summary>啟動參數（例如雙擊 .tourney 檔時傳入的路徑）。開啟後即清除，只處理一次。</summary>
public sealed class StartupOptions
{
    public string? OpenPath { get; set; }
}
