using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Import;
using TourneyDesk.Core.Session;

namespace TourneyDesk.Core.Commands;

/// <summary>
/// 匯入已通過驗證的選手（整批為一個可復原的步驟）。執行時再驗證一次註冊編號，
/// 未指定註冊編號者自動配發，並跳過檔內與既有已使用的號碼。
/// </summary>
public sealed record ImportPlayers(IReadOnlyList<ImportedPlayer> Players) : ITournamentCommand
{
    public Transition Apply(TournamentState state, CommandContext context)
    {
        if (state.IsStarted)
            throw new DomainException("賽事開始後不能匯入選手。");
        if (Players.Count == 0)
            throw new DomainException("沒有可匯入的選手。");
        if (state.Players.Count + Players.Count > TournamentState.MaxPlayers)
            throw new DomainException($"匯入後將超過選手上限 {TournamentState.MaxPlayers} 人。");

        var used = state.Players.Values.Select(p => p.RegNo).ToHashSet();
        foreach (var p in Players.Where(p => p.RegNo is not null))
        {
            if (!used.Add(p.RegNo!.Value))
                throw new DomainException($"第 {p.LineNumber} 行：註冊編號 {p.RegNo} 重複，請重新檢查匯入檔。");
        }

        var next = state;
        var nextRegNo = state.NextRegNo;
        foreach (var p in Players)
        {
            Player.ValidateFields(p.Name, p.Rating);
            var regNo = p.RegNo;
            if (regNo is null)
            {
                while (used.Contains(nextRegNo))
                    nextRegNo++;
                regNo = nextRegNo;
                used.Add(nextRegNo);
            }

            next = next.WithPlayer(new Player(next.NextPlayerId, regNo.Value, p.Name.Trim(), p.Rating)) with
            {
                NextPlayerId = next.NextPlayerId + 1,
            };
        }

        next = next with { NextRegNo = Math.Max(state.NextRegNo, next.Players.Values.Max(p => p.RegNo) + 1) };
        var names = string.Join("、", Players.Take(10).Select(p => p.Name)) + (Players.Count > 10 ? "…" : "");
        return new Transition(next, $"匯入 {Players.Count} 位選手",
            [context.Audit("匯入選手", $"{Players.Count} 位", null, names)]);
    }
}
