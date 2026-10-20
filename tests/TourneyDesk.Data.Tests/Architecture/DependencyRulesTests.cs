using System.Reflection;

namespace TourneyDesk.Data.Tests.Architecture;

public class DependencyRulesTests
{
    [Fact]
    public void Data_does_not_reference_ui_or_winforms()
    {
        var data = Assembly.Load("TourneyDesk.Data");

        var offending = data.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                        || name.StartsWith("System.Windows.Forms", StringComparison.Ordinal)
                        || name.StartsWith("TourneyDesk.UI", StringComparison.Ordinal)
                        || name.StartsWith("TourneyDesk.App", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }
}
