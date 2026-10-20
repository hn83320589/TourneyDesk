using System.Text.Json;
using TourneyDesk.Core.Locking;

namespace TourneyDesk.Data.Locking;

/// <summary>
/// 以鎖定檔（{賽事檔}.lock）實作開檔鎖定。持有者在整個開啟期間保留可寫 handle 並只允許他人讀取，
/// 這個共用模式由作業系統（含 SMB 伺服器，即網路磁碟）強制執行。
/// 程式當掉時 handle 由作業系統釋放，鎖定檔留下 → 下次開啟時依記錄判定為殘留鎖。
/// </summary>
public sealed class FileSystemLockFile(string tournamentPath) : ILockFile
{
    public string LockPath { get; } = tournamentPath + ".lock";

    public LockProbe Probe()
    {
        if (!File.Exists(LockPath))
            return LockProbe.None;

        try
        {
            using var probe = new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return new LockProbe(LockProbeKind.Unheld, ReadInfo(probe));
        }
        catch (FileNotFoundException)
        {
            return LockProbe.None;
        }
        catch (IOException)
        {
            // 共用違規：另一個程式正持有 handle
            return new LockProbe(LockProbeKind.Held, TryReadWhileHeld());
        }
        catch (UnauthorizedAccessException)
        {
            return new LockProbe(LockProbeKind.Held, TryReadWhileHeld());
        }
    }

    public ILockHandle Acquire(LockInfo info)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LockUnavailableException($"無法取得開檔鎖定：{ex.Message}");
        }

        stream.SetLength(0);
        JsonSerializer.Serialize(stream, info);
        stream.Flush(flushToDisk: true);
        return new Handle(stream, info, LockPath);
    }

    private LockInfo? TryReadWhileHeld()
    {
        try
        {
            using var reader = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ReadInfo(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // 讀不到內容時仍視為被持有，只是無法顯示持有者
        }
    }

    private static LockInfo? ReadInfo(Stream stream)
    {
        try
        {
            return stream.Length == 0 ? null : JsonSerializer.Deserialize<LockInfo>(stream);
        }
        catch (JsonException)
        {
            return null; // 內容損毀 → 由判定規則要求強制確認
        }
    }

    private sealed class Handle(FileStream stream, LockInfo info, string path) : ILockHandle
    {
        private bool _disposed;

        public LockInfo Info => info;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            stream.Dispose();
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 刪除失敗時鎖定檔會留下，下次開啟會依「殘留鎖」規則處理（本機、程序已結束 → 確認後接手）
            }
        }
    }
}
