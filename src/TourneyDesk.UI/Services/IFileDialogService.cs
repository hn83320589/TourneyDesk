namespace TourneyDesk.UI.Services;

/// <summary>檔案選擇對話框（App 以 WinForms 實作）。使用者取消時回傳 null。</summary>
public interface IFileDialogService
{
    string? PickTournamentToOpen();

    string? PickTournamentToCreate(string suggestedName);

    string? PickCsvToImport();

    /// <param name="filter">WinForms 篩選字串，例如「CSV 檔 (*.csv)|*.csv」。</param>
    string? PickSaveLocation(string suggestedName, string filter);
}

/// <summary>列印目前頁面（App 以 WebView2 的列印介面實作）。</summary>
public interface IPrintService
{
    Task PrintAsync();
}
