namespace TourneyDesk.UI.Services;

/// <summary>詢問存放位置後寫出檔案；所有失敗都轉成可讀訊息。</summary>
public static class FileExport
{
    public static void Save(Workspace workspace, IFileDialogService dialogs, string suggestedName, string filter, Func<byte[]> content)
    {
        if (dialogs.PickSaveLocation(suggestedName, filter) is not { } path)
            return;
        try
        {
            File.WriteAllBytes(path, content());
            workspace.ShowInfo($"已匯出：{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            workspace.ShowError($"匯出失敗：{ex.Message}");
        }
    }

    public static async Task SaveAsync(Workspace workspace, IFileDialogService dialogs, string suggestedName, string filter, Func<Task<byte[]>> content)
    {
        if (dialogs.PickSaveLocation(suggestedName, filter) is not { } path)
            return;
        try
        {
            await File.WriteAllBytesAsync(path, await content());
            workspace.ShowInfo($"已匯出：{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            workspace.ShowError($"匯出失敗：{ex.Message}");
        }
    }
}
