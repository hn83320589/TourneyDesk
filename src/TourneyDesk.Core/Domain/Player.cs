namespace TourneyDesk.Core.Domain;

/// <param name="Id">內部識別碼，只用於關聯；任何排序與決定性都不得依賴它。</param>
/// <param name="RegNo">註冊編號（自動遞增），對外顯示與同分排序使用。</param>
/// <param name="JoinRound">遲到加入的起始輪；null 表示從第 1 輪起參賽。</param>
/// <param name="WithdrawRound">退賽生效輪（自該輪起不再配對）；null 表示在籍。</param>
public sealed record Player(int Id, int RegNo, string Name, int Rating, int? JoinRound = null, int? WithdrawRound = null)
{
    public const int MinRating = 0;
    public const int MaxRating = 3000;
    public const int MaxNameLength = 50;

    public int FirstRound => JoinRound ?? 1;

    public bool IsWithdrawn => WithdrawRound is not null;

    /// <summary>此選手在第 <paramref name="round"/> 輪是否應參與配對。</summary>
    public bool IsActiveIn(int round) =>
        round >= FirstRound && (WithdrawRound is null || round < WithdrawRound);

    public static void ValidateFields(string name, int rating)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("姓名不可空白。");
        if (name.Length > MaxNameLength)
            throw new DomainException($"姓名不可超過 {MaxNameLength} 個字元。");
        if (rating is < MinRating or > MaxRating)
            throw new DomainException($"積分必須是 {MinRating} 到 {MaxRating} 的整數。");
    }
}
