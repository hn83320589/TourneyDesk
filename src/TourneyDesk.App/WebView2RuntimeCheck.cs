using Microsoft.Web.WebView2.Core;

namespace TourneyDesk.App;

internal static class WebView2RuntimeCheck
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static bool IsInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    public static void ShowMissingRuntimeDialog()
    {
        MessageBox.Show(
            "本程式需要 Microsoft Edge WebView2 Runtime，但在這台電腦上找不到。\n\n" +
            "請以瀏覽器開啟下列網址，下載並安裝「Evergreen Standalone Installer」後再重新啟動本程式：\n\n" +
            DownloadUrl,
            "缺少 WebView2 Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
