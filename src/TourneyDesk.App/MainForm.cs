using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using TourneyDesk.UI;

namespace TourneyDesk.App;

internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "賽事裁判台 TourneyDesk";
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();

        var webView = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot/index.html",
            Services = services.BuildServiceProvider(),
        };
        webView.RootComponents.Add<Shell>("#app");
        Controls.Add(webView);
    }
}
