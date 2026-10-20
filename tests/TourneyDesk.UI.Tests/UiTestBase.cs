using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TourneyDesk.Core.Domain;
using TourneyDesk.UI.Services;

namespace TourneyDesk.UI.Tests;

/// <summary>註冊假服務的 bUnit 測試基底。</summary>
public abstract class UiTestBase : BunitContext
{
    internal FakeGateway Gateway { get; } = new();

    internal FakeDialogs Dialogs { get; } = new();

    internal FakePrinter Printer { get; } = new();

    internal Workspace Workspace { get; }

    protected UiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Workspace = new Workspace(Gateway, TimeProvider.System);
        Services.AddSingleton(Workspace);
        Services.AddSingleton<IFileDialogService>(Dialogs);
        Services.AddSingleton<IPrintService>(Printer);
    }

    /// <summary>以指定狀態「開啟」一個賽事。</summary>
    internal void OpenWith(TournamentState state, string path = @"C:\賽事\已存在.tourney")
    {
        Gateway.Files[path] = state;
        Assert.True(Workspace.Open(path));
    }
}
