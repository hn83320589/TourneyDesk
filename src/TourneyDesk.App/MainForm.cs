using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using TourneyDesk.UI;
using TourneyDesk.UI.Services;

namespace TourneyDesk.App;

internal sealed class MainForm : Form
{
    private readonly ServiceProvider _services;

    public MainForm(string? startupPath = null)
    {
        Text = "賽事裁判台 TourneyDesk";
        Width = 1360;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;

        var webView = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html" };

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppFolders>();
        services.AddSingleton<ITournamentGateway, FileGateway>();
        services.AddSingleton<Workspace>();
        services.AddSingleton(new StartupOptions { OpenPath = startupPath });
        services.AddSingleton<IFileDialogService>(sp => new WinFormsFileDialogs(this, sp.GetRequiredService<AppFolders>()));
        services.AddSingleton<IPrintService>(_ => new WebViewPrintService(webView));
        _services = services.BuildServiceProvider();

        webView.Services = _services;
        webView.RootComponents.Add<Shell>("#app");
        Controls.Add(webView);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var workspace = _services.GetRequiredService<Workspace>();
        if (workspace.BackgroundWork is not null)
            workspace.CancelBackground();
        workspace.Close(); // 關檔並釋放開檔鎖定
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _services.Dispose();
        base.Dispose(disposing);
    }
}
