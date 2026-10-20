using TourneyDesk.UI.Services;

namespace TourneyDesk.App;

internal sealed class WinFormsFileDialogs(IWin32Window owner, AppFolders folders) : IFileDialogService
{
    private const string TournamentFilter = "TourneyDesk 賽事檔 (*.tourney)|*.tourney";

    public string? PickTournamentToOpen()
    {
        using var dialog = new OpenFileDialog { Filter = TournamentFilter, InitialDirectory = folders.LastFolder, Title = "開啟賽事檔" };
        return Show(dialog);
    }

    public string? PickTournamentToCreate(string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = TournamentFilter,
            InitialDirectory = folders.LastFolder,
            FileName = SafeFileName(suggestedName) + ".tourney",
            Title = "建立賽事檔",
            OverwritePrompt = false, // 不覆寫既有賽事檔：已存在時由建立流程回報錯誤
        };
        return Show(dialog);
    }

    public string? PickCsvToImport()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV 檔 (*.csv)|*.csv|所有檔案 (*.*)|*.*", InitialDirectory = folders.LastFolder, Title = "匯入選手 CSV" };
        return Show(dialog);
    }

    public string? PickSaveLocation(string suggestedName, string filter)
    {
        using var dialog = new SaveFileDialog { Filter = filter, InitialDirectory = folders.LastFolder, FileName = SafeFileName(suggestedName) };
        return Show(dialog);
    }

    private string? Show(FileDialog dialog)
    {
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;
        folders.Remember(dialog.FileName);
        return dialog.FileName;
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
