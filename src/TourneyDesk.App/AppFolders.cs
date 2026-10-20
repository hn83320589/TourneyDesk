using System.Diagnostics;
using System.Text.Json;

namespace TourneyDesk.App;

/// <summary>
/// 賽事檔預設放在 %UserProfile%\Documents\TourneyDesk\；程式設定放在 %LocalAppData%\TourneyDesk\settings.json。
/// </summary>
internal sealed class AppFolders
{
    private sealed record Settings(string? LastFolder);

    private readonly string _settingsPath;

    public AppFolders()
    {
        DefaultTournamentFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TourneyDesk");
        var settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TourneyDesk");
        _settingsPath = Path.Combine(settingsFolder, "settings.json");
        TryCreate(DefaultTournamentFolder);
        TryCreate(settingsFolder);
        LastFolder = Load()?.LastFolder is { } last && Directory.Exists(last) ? last : DefaultTournamentFolder;
    }

    public string DefaultTournamentFolder { get; }

    public string LastFolder { get; private set; }

    public void Remember(string filePath)
    {
        if (Path.GetDirectoryName(filePath) is not { Length: > 0 } folder)
            return;
        LastFolder = folder;
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(folder)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"無法儲存設定：{ex.Message}"); // 設定只影響預設資料夾，失敗不影響賽事資料
        }
    }

    private Settings? Load()
    {
        try
        {
            return File.Exists(_settingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"無法讀取設定，改用預設值：{ex.Message}");
            return null;
        }
    }

    private static void TryCreate(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"無法建立資料夾 {folder}：{ex.Message}");
        }
    }
}
