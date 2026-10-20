namespace TourneyDesk.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 最後防線：任何未處理的例外都轉成可讀訊息，不讓程式直接崩潰
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        if (!WebView2RuntimeCheck.IsInstalled())
        {
            WebView2RuntimeCheck.ShowMissingRuntimeDialog();
            return;
        }

        Application.Run(new MainForm(args.FirstOrDefault()));
    }

    private static void ShowFatal(Exception? ex)
    {
        MessageBox.Show(
            "發生未預期的錯誤：\n\n" + (ex?.Message ?? "未知錯誤"),
            "TourneyDesk",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
