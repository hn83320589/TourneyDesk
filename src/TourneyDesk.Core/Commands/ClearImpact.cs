using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace TourneyDesk.Core.Commands;

/// <summary>
/// 破壞性操作（重新配對、連鎖清除）的影響清單。使用者確認的是這份清單；
/// 命令執行時會重新計算並比對指紋，若影響範圍在確認後已改變則拒絕執行。
/// </summary>
public sealed record ClearImpact(ImmutableArray<string> Items)
{
    public bool IsEmpty => Items.IsEmpty;

    public string Fingerprint =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Items))));

    public string Summary(int maxLines = 20) =>
        Items.Length <= maxLines
            ? string.Join("；", Items)
            : string.Join("；", Items.Take(maxLines)) + $"；…共 {Items.Length} 項";
}
