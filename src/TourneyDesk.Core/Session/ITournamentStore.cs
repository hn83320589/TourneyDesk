using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Session;

/// <summary>檔案層介面：每次狀態轉移都必須在單一交易中寫入。</summary>
public interface ITournamentStore
{
    /// <summary>
    /// 把 <paramref name="before"/> → <paramref name="after"/> 的差異與 <paramref name="audit"/> 在單一交易中寫入。
    /// 失敗時丟出 <see cref="StorageException"/>，且不得留下部分寫入。
    /// </summary>
    void Commit(TournamentState before, TournamentState after, IReadOnlyList<AuditEntry> audit);
}

/// <summary>檔案存取失敗（被鎖、磁碟錯誤等）；訊息可直接呈現給使用者。</summary>
public sealed class StorageException(string message, Exception? inner = null) : Exception(message, inner);
