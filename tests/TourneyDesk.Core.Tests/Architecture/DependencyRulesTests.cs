using System.Reflection;

namespace TourneyDesk.Core.Tests.Architecture;

public class DependencyRulesTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "System.Windows.Forms",
        "Microsoft.Web.WebView2",
        "Microsoft.Data.Sqlite",
        "TourneyDesk.Data",
        "TourneyDesk.UI",
        "TourneyDesk.App",
    ];

    [Fact]
    public void Core_does_not_reference_ui_winforms_or_data_assemblies()
    {
        var core = Assembly.Load("TourneyDesk.Core");

        var offending = core.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offending);
    }
}
