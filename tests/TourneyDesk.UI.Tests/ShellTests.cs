using Bunit;

namespace TourneyDesk.UI.Tests;

public class ShellTests : BunitContext
{
    [Fact]
    public void Shell_shows_application_title()
    {
        var cut = Render<Shell>();

        Assert.Equal("賽事裁判台 TourneyDesk", cut.Find("h1").TextContent);
    }
}
