using System.ComponentModel;
using System.Diagnostics;
using TourneyDesk.Core.Locking;

namespace TourneyDesk.Data.Locking;

/// <summary>以作業系統查詢同一台電腦上的程序是否仍存在（PID + 啟動時間）。</summary>
public sealed class SystemProcessInspector : IProcessInspector
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    public static ProcessIdentity Current()
    {
        using var self = Process.GetCurrentProcess();
        return new ProcessIdentity(Environment.MachineName, self.Id, self.StartTime.ToUniversalTime());
    }

    public bool IsAlive(int processId, DateTimeOffset processStartTime)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;
            var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
            return (started - processStartTime).Duration() <= StartTimeTolerance;
        }
        catch (ArgumentException)
        {
            return false; // 該 PID 目前沒有程序
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return true; // 無權查詢啟動時間：保守視為仍存在，避免誤接手
        }
    }
}
