using System.Collections.Concurrent;
using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Swiss;

/// <summary>
/// 以輸入指紋（<see cref="SwissRoundInput.Fingerprint"/>）快取引擎建議。引擎是輸入的純函式，所以快取是精確的：
/// 只有輸入改變的輪次才需要重算。可跨執行緒使用；超過容量時整個清空（簡單且足夠：每場賽事至多 15 輪）。
/// </summary>
public sealed class SwissProposalCache(int capacity = 256)
{
    private readonly ConcurrentDictionary<string, Pairing> _entries = new(StringComparer.Ordinal);
    private int _hits;
    private int _misses;

    public int Hits => _hits;

    public int Misses => _misses;

    public int Count => _entries.Count;

    public Pairing GetOrCompute(SwissRoundInput input, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(input.Fingerprint, out var cached))
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        var pairing = SwissPairingEngine.Pair(input, null, cancellationToken).Pairing;
        Interlocked.Increment(ref _misses);
        if (_entries.Count >= capacity)
            _entries.Clear();
        _entries[input.Fingerprint] = pairing;
        return pairing;
    }
}
