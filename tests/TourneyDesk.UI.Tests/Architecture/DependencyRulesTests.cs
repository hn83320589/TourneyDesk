using System.Reflection;

namespace TourneyDesk.UI.Tests.Architecture;

public class DependencyRulesTests
{
    [Fact]
    public void Ui_does_not_reference_winforms_or_webview2()
    {
        var ui = Assembly.Load("TourneyDesk.UI");

        var offending = ui.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("System.Windows.Forms", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft.AspNetCore.Components.WebView", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }
}
