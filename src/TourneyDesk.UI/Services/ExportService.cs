using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Import;
using TourneyDesk.Core.Knockout;
using TourneyDesk.Core.Standings;

namespace TourneyDesk.UI.Services;

/// <summary>匯出 CSV（UTF-8 with BOM）與單檔 HTML（以同一組 Razor 元件渲染，樣式內嵌，不引用任何外部資源）。</summary>
public static class ExportService
{
    public static byte[] PlayersCsv(TournamentState s) =>
        Csv.ToUtf8WithBom(
            new[] { new[] { "註冊編號", "姓名", "積分", "狀態" } }.Concat(
                s.Players.Values.OrderBy(p => p.RegNo).Select(p => new[] { Display.Int(p.RegNo), p.Name, Display.Int(p.Rating), Display.Status(p) })));

    public static byte[] PairingsCsv(TournamentState s, Round round)
    {
        var rows = new List<string[]> { new[] { "桌號", "先手編號", "先手", "後手編號", "後手", "結果" } };
        for (var i = 0; i < round.Games.Length; i++)
        {
            var g = round.Games[i];
            rows.Add([Display.Int(i + 1), Display.Int(s.Players[g.White].RegNo), s.Players[g.White].Name,
                Display.Int(s.Players[g.Black].RegNo), s.Players[g.Black].Name, g.Result.HasResult() ? g.Result.DisplayName() : ""]);
        }

        if (round.Bye is { } bye)
            rows.Add(["輪空", Display.Int(s.Players[bye].RegNo), s.Players[bye].Name, "", "", ""]);
        return Csv.ToUtf8WithBom(rows);
    }

    public static byte[] StandingsCsv(TournamentState s)
    {
        if (s.Settings.Format == TournamentFormat.SingleElimination && s.Bracket is not null)
        {
            return Csv.ToUtf8WithBom(new[] { new[] { "名次", "註冊編號", "姓名", "成績" } }.Concat(
                KnockoutStandings.Compute(s).Select(p => new[] { Display.Int(p.Rank), Display.Int(p.RegNo), p.Name, p.Placement })));
        }

        return Csv.ToUtf8WithBom(new[] { new[] { "名次", "註冊編號", "姓名", "總分", "直接對戰", "Cut-1", "Buchholz", "SB", "勝場", "狀態" } }.Concat(
            StandingsCalculator.Compute(s).Select(r => new[]
            {
                Display.Int(r.Rank), Display.Int(r.RegNo), r.Name, Display.Score(r.Score),
                r.DirectEncounter is { } de ? Display.Score(de) : "", Display.Score(r.BuchholzCut1), Display.Score(r.Buchholz),
                Display.Quarter(r.SonnebornBerger), Display.Int(r.Wins), r.Withdrawn ? "退賽" : "",
            })));
    }

    /// <summary>把元件渲染成獨立的 HTML 檔（UTF-8，含內嵌樣式）。</summary>
    public static async Task<byte[]> HtmlAsync<TComponent>(string title, IDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(new EmptyServices(), NullLoggerFactory.Instance);
        var body = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());

        var html = $"""
            <!DOCTYPE html>
            <html lang="zh-Hant">
            <head>
            <meta charset="utf-8" />
            <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
            <style>
            {ExportStyles.Value}
            </style>
            </head>
            <body class="export">
            <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
            {body}
            </body>
            </html>
            """;
        return Encoding.UTF8.GetBytes(html);
    }

    private static readonly Lazy<string> ExportStyles = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TourneyDesk.UI.export.css")
            ?? throw new InvalidOperationException("找不到內嵌的匯出樣式。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
