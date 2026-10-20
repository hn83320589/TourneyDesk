using TourneyDesk.Core.Domain;

namespace TourneyDesk.Core.Swiss;

/// <summary>
/// 先後手（配對確定後才決定）：
/// 先手次數較少者先手 → 相同時，上一場為後手者先手 → 仍相同時，賽前排名較高者先手。
/// 「上一場為後手」只在其中一人上一場為後手、另一人不是（先手或尚無對局）時才分出先後。
/// </summary>
public static class ColorAllocator
{
    public static PairedGame Assign(SwissRoundInput input, int idA, int idB)
    {
        var a = input.Get(idA);
        var b = input.Get(idB);
        if (a.WhiteCount != b.WhiteCount)
            return a.WhiteCount < b.WhiteCount ? new PairedGame(idA, idB) : new PairedGame(idB, idA);

        var aLastBlack = a.LastColor == PieceColor.Black;
        var bLastBlack = b.LastColor == PieceColor.Black;
        if (aLastBlack != bLastBlack)
            return aLastBlack ? new PairedGame(idA, idB) : new PairedGame(idB, idA);

        return input.RankOf[idA] < input.RankOf[idB] ? new PairedGame(idA, idB) : new PairedGame(idB, idA);
    }
}
