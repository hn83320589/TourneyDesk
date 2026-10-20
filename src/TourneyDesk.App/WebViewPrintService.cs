using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using TourneyDesk.UI.Services;

namespace TourneyDesk.App;

/// <summary>以 WebView2 的列印介面輸出目前畫面（列印版面由 app.css 的 @media print 控制）。</summary>
internal sealed class WebViewPrintService(BlazorWebView view) : IPrintService
{
    public Task PrintAsync()
    {
        view.WebView.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        return Task.CompletedTask;
    }
}
