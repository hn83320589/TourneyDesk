using System.Collections.Immutable;
using TourneyDesk.Core.Domain;
using TourneyDesk.Core.Locking;
using TourneyDesk.Core.Session;
using TourneyDesk.Core.Swiss;

namespace TourneyDesk.UI.Services;

public enum NoticeKind
{
    Info,
    Success,
    Error,
}

public sealed record Notice(NoticeKind Kind, string Message);

/// <summary>
/// UI 的工作區：目前開啟的賽事、執行命令、復原、背景工作與通知。
/// 所有狀態變更都經由 <see cref="TournamentSession"/>；任何失敗都轉成可讀訊息，不丟出例外給畫面。
/// </summary>
public sealed class Workspace(ITournamentGateway gateway, TimeProvider time) : IDisposable
{
    private OpenedTournament? _opened;
    private CancellationTokenSource? _backgroundCts;
    private CancellationTokenSource? _staleCts;

    public TournamentSession? Session { get; private set; }

    public TournamentState? State => Session?.State;

    public bool IsOpen => Session is not null;

    public string? FilePath => _opened?.Path;

    public Notice? Notice { get; private set; }

    /// <summary>背景工作描述；null 表示沒有背景工作。</summary>
    public string? BackgroundWork { get; private set; }

    public int BackgroundProgress { get; private set; }

    /// <summary>目前判定為「過期」的瑞士制輪次。</summary>
    public ImmutableArray<int> StaleRounds { get; private set; } = [];

    public bool IsCheckingStaleness { get; private set; }

    /// <summary>任何會影響畫面的變化（狀態、通知、背景進度）。</summary>
    public event Action? Changed;

    public LockDecision CheckLock(string path) => gateway.CheckLock(path);

    public bool Open(string path, bool confirmedStaleTakeover = false, string? forceConfirmation = null) =>
        Attach(() => gateway.Open(path, confirmedStaleTakeover, forceConfirmation), $"已開啟 {System.IO.Path.GetFileName(path)}");

    public bool Create(string path, TournamentSettings settings) =>
        Attach(() => gateway.Create(path, TournamentState.Create(settings)), $"已建立 {System.IO.Path.GetFileName(path)}");

    public void Close()
    {
        _staleCts?.Cancel();
        _backgroundCts?.Cancel();
        _opened?.Dispose();
        _opened = null;
        Session = null;
        StaleRounds = [];
        Raise();
    }

    public CommandResult Execute(ITournamentCommand command, string? successMessage = null)
    {
        if (Session is null)
            return Report(CommandResult.Fail("尚未開啟賽事。"), null);
        return Report(Session.Execute(command), successMessage);
    }

    public CommandResult Undo() =>
        Session is null ? CommandResult.Fail("尚未開啟賽事。") : Report(Session.Undo(), "已復原");

    public CommandResult Redo() =>
        Session is null ? CommandResult.Fail("尚未開啟賽事。") : Report(Session.Redo(), "已重做");

    public IReadOnlyList<AuditEntry> ReadAudit()
    {
        try
        {
            return _opened?.ReadAudit() ?? [];
        }
        catch (Exception ex) when (ex is StorageException or InvalidOperationException or IOException)
        {
            ShowError($"無法讀取異動紀錄：{ex.Message}");
            return [];
        }
    }

    public void ShowInfo(string message) => SetNotice(new Notice(NoticeKind.Info, message));

    public void ShowError(string message) => SetNotice(new Notice(NoticeKind.Error, message));

    public void DismissNotice() => SetNotice(null);

    /// <summary>
    /// 在背景執行耗時計算（例如配對）：期間 Session 拒絕任何變更；可取消；完成後回傳結果，取消則回傳 default。
    /// 計算本身只讀狀態，不寫檔，取消後賽事檔不變。
    /// </summary>
    public async Task<T?> RunInBackgroundAsync<T>(string description, Func<TournamentState, IProgress<int>, CancellationToken, T> work)
        where T : class
    {
        if (Session is null || BackgroundWork is not null)
            return null;

        using var cts = new CancellationTokenSource();
        _backgroundCts = cts;
        var snapshot = Session.State;
        var scope = Session.BeginBackgroundWork(description);
        BackgroundWork = description;
        BackgroundProgress = 0;
        Raise();
        var progress = new ThrottledProgress(p =>
        {
            BackgroundProgress = p;
            Raise();
        });

        try
        {
            return await Task.Run(() => work(snapshot, progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            ShowInfo($"已取消「{description}」，賽事內容未變更。");
            return null;
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            ShowError($"「{description}」失敗：{ex.Message}");
            return null;
        }
        finally
        {
            scope.Dispose();
            BackgroundWork = null;
            _backgroundCts = null;
            Raise();
        }
    }

    public void CancelBackground() => _backgroundCts?.Cancel();

    private bool Attach(Func<OpenedTournament> open, string message)
    {
        try
        {
            var opened = open();
            Close();
            _opened = opened;
            opened.OnWarning = ShowError;
            Session = new TournamentSession(opened.State, opened.Store, time);
            Session.StateChanged += (_, _) => OnStateChanged();
            SetNotice(new Notice(NoticeKind.Success, message));
            OnStateChanged();
            return true;
        }
        catch (Exception ex) when (ex is StorageException or LockUnavailableException or DomainException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private CommandResult Report(CommandResult result, string? successMessage)
    {
        if (!result.Succeeded)
            ShowError(result.Error!);
        else if (successMessage is not null)
            SetNotice(new Notice(NoticeKind.Success, successMessage));
        return result;
    }

    private void OnStateChanged()
    {
        Raise();
        _ = RefreshStalenessAsync();
    }

    /// <summary>以目前狀態的快照在背景重新判定過期輪次；狀態再次改變時自動取消並重算。</summary>
    public async Task RefreshStalenessAsync()
    {
        _staleCts?.Cancel();
        if (State is not { Settings.Format: TournamentFormat.Swiss } snapshot || snapshot.Rounds.Count(r => r.IsPublished) < 2)
        {
            StaleRounds = [];
            return;
        }

        var cts = new CancellationTokenSource();
        _staleCts = cts;
        IsCheckingStaleness = true;
        Raise();
        try
        {
            var stale = await Task.Run(() => SwissStaleness.FindStaleRounds(snapshot, 2, null, cts.Token), cts.Token);
            if (ReferenceEquals(State, snapshot))
                StaleRounds = stale;
        }
        catch (OperationCanceledException)
        {
            // 狀態已改變，新的判定會接手
        }
        finally
        {
            if (ReferenceEquals(_staleCts, cts))
            {
                IsCheckingStaleness = false;
                Raise();
            }
        }
    }

    private void SetNotice(Notice? notice)
    {
        Notice = notice;
        Raise();
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose() => Close();

    /// <summary>只在百分比改變時回報，避免大量重繪。</summary>
    private sealed class ThrottledProgress(Action<int> report) : IProgress<int>
    {
        private int _last = -1;

        public void Report(int value)
        {
            if (Interlocked.Exchange(ref _last, value) != value)
                report(value);
        }
    }
}
