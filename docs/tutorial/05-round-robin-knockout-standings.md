# 第 5 章　循環賽、淘汰賽與排名

> 本章目標：看懂如何用數論（模運算）一次排出公平的循環賽程、用有向無環圖（DAG）描述淘汰籤表，以及用遞迴分群實作同分判定。這三個題目都比瑞士制簡單，但各有一個漂亮的核心想法。

---

## 5.1 循環賽：圓桌法

程式：`src/TourneyDesk.Core/RoundRobin/RoundRobinScheduler.cs`

### 需求

- 每兩人恰好對戰一次（雙循環兩次）。
- 每輪每人最多一場。
- 奇數人數時，每人輪空次數相同。
- 每人先後手次數相差 ≤ 1。
- 雙循環的第二循環先後手互換。

### 種子與虛擬種子

先依「積分高→低、註冊編號小→大」排出種子 1..n。設 m = n（偶數）或 n + 1（奇數；種子 m 是虛擬的，配到它就輪空）。總共 m − 1 輪。

### 配對公式

> 第 r 輪（1 ≤ r ≤ m − 1）：種子 r 對種子 m；其餘種子 i、j 若 **i + j ≡ 2r (mod m − 1)** 則對戰（餘數用 1..m−1 表示）。

```csharp
for (var r = 1; r <= m - 1; r++)
{
    var pairs = new List<(int A, int B)> { (r, m) };
    for (var i = 1; i <= m - 1; i++)
    {
        if (i == r) continue;
        var j = Residue((2 * r) - i, m - 1);   // 解 i + j ≡ 2r
        if (i < j) pairs.Add((i, j));
    }
    // ...
}
```

### 手算範例：m = 6

模數 m − 1 = 5。

| 輪 | 對種子 6 | i + j ≡ 2r (mod 5) 的其餘配對 |
|---|---|---|
| 1 | 1-6 | 2r = 2：2-5（7≡2）、3-4（7≡2） |
| 2 | 2-6 | 2r = 4：1-3（4）、4-5（9≡4） |
| 3 | 3-6 | 2r = 6≡1：1-5（6≡1）、2-4（6≡1） |
| 4 | 4-6 | 2r = 8≡3：1-2（3）、3-5（8≡3） |
| 5 | 5-6 | 2r = 10≡5：1-4（5）、2-3（5） |

檢查：15 對種子各出現一次 ✓。若 n = 5，種子 6 是虛擬的，每人恰好輪空一次 ✓。

### 為什麼每對恰好相遇一次？

給定 i < j < m，它們在哪一輪相遇？要解 2r ≡ i + j (mod m − 1)。因為 m 是偶數，m − 1 是**奇數**，2 與 m − 1 互質，所以 2 在模 m − 1 下有反元素，r 有唯一解。而且 r ≠ i、r ≠ j（否則 2i ≡ i + j，得 i ≡ j，矛盾），所以這一輪 i、j 不會被拿去對種子 m。至於種子 k 與 m，只在第 k 輪相遇。

> **教學重點**：一個好的公式附帶一個短證明，比一段複雜的模擬程式可靠得多。程式碼的註解裡直接寫了這個證明的關鍵句。

### 先後手：奇偶規則

> 對種子 i < j：**i + j 為奇數時 i 先手，否則 j 先手。**

```csharp
var loWhite = (lo + hi) % 2 == 1;
games.Add(loWhite ? (seeded[lo - 1], seeded[hi - 1]) : (seeded[hi - 1], seeded[lo - 1]));
```

為什麼平衡？看種子 k 對上所有其他種子 x：

- x < k：k 是 hi，k 先手 ⇔ x + k 為偶數。
- x > k：k 是 lo，k 先手 ⇔ x + k 為奇數。

把 1..m 除了 k 以外的數依奇偶與大小分類，兩類數量相差至多 1，所以 k 的先後手次數相差 ≤ 1。這個規則與「第幾輪」無關，非常簡單。性質測試對 N = 2～64 全部驗證。

### 雙循環

第二循環複製第一循環的每一輪，先後手互換：

```csharp
if (doubleRoundRobin)
    foreach (var (games, bye) in cycle)
        rounds.Add(NewRound(rounds.Count + 1, [.. games.Select(g => (g.Black, g.White))], bye));
```

### 循環賽的退賽

退賽之後的已發布對局判退賽者棄權（對手得 2 半分）；退賽者的輪空取消（分數凍結）。這是 `WithdrawPlayer` 命令的工作（`src/TourneyDesk.Core/Commands/ParticipationCommands.cs`）。

---

## 5.2 單淘汰：籤表是一張 DAG

程式：`src/TourneyDesk.Core/Knockout/`、`src/TourneyDesk.Core/Domain/Bracket.cs`

### 標準種子位置

籤表大小 S 是 ≥ N 的最小 2 的冪次。位置的排法要讓「種子 1 和種子 2 只會在決賽相遇」，遞迴定義：

> positions(2) = [1, 2]；positions(2k) 把 positions(k) 的每個 s 展開為 **s, 2k + 1 − s**。

```csharp
var positions = new List<int> { 1, 2 };
for (var k = 2; k < size; k *= 2)
    positions = [.. positions.SelectMany(s => new[] { s, (2 * k) + 1 - s })];
```

S = 8：[1,2] → [1,4,2,3] → [1,8,4,5,2,7,3,6]，第一輪就是 1v8、4v5、2v7、3v6。每一對的和都是 S + 1，所以高種子永遠對上相對應的低種子。

種子編號 > N 的位置是空的，它的對手一定是最高的 S − N 位種子之一 → **輪空只出現在第 1 輪，且屬於最高種子**。

### 用「來源」描述參賽者

第 2 輪以後的場次，參賽者還不知道是誰。與其存「選手 Id」，不如存「來源」：

```csharp
public enum KnockoutSourceKind { Seed, Winner, Loser }
public readonly record struct KnockoutSource(KnockoutSourceKind Kind, int Value);
// Seed(3)：種子 3；WinnerOf(5)：第 5 場的勝者；LoserOf(9)：第 9 場的敗者（季軍戰）
```

整張籤表就是一張**有向無環圖**：每場比賽指向它依賴的前面場次。要知道某格是誰，就沿著來源往回解：

```csharp
public Participant Resolve(KnockoutSource source) => source.Kind switch
{
    Seed   => 種子存在 ? Participant.Of(Seeds[i]) : Participant.Empty,
    Winner => 該場有結果 ? Participant.Of(勝者) : Participant.Pending,
    Loser  => ...
};
```

`Participant` 有三種狀態：已知選手、待定（Pending，前一場還沒打）、空位（Empty，輪空）。

### 為什麼用 DAG？

- **更正勝者時的連鎖清除**變得很直接：沿著 DAG 找出所有依賴這場（直接或間接）的後續場次。

  ```csharp
  // src/TourneyDesk.Core/Knockout/KnockoutRules.cs
  public static ImmutableArray<int> DependentMatches(Bracket bracket, int matchId)
  {
      var affected = new HashSet<int> { matchId };
      foreach (var m in bracket.Matches.Where(m => m.Id > matchId))   // 場次 Id 是拓撲順序
          if (affected.Any(id => m.A.DependsOn(id) || m.B.DependsOn(id)))
          { affected.Add(m.Id); result.Add(m.Id); }
      return result.ToImmutable();
  }
  ```

  場次 Id 依輪次遞增產生，天然就是拓撲排序，所以一次線性掃描就夠了。

- **季軍戰**只是兩個 `LoserOf` 來源，不需要特別的資料結構。

### 正規化：自動決定的場次

有些場次不用比就知道結果：一方是空位（輪空）、一方已退賽（walkover）。`Normalize` 依場次順序掃一次，把能自動決定的都填上：

```csharp
if (a.IsKnown && b.IsEmpty)        outcome = Bye(a);
else if (b.IsKnown && a.IsEmpty)   outcome = Bye(b);
else if (a.IsKnown && b.IsKnown)
{
    // 一方退賽 → 另一方 walkover；雙方都退賽 → 較高種子晉級
}
```

每個改變籤表的命令（開始、登錄勝者、更正、退賽）最後都呼叫 `Normalize`，所以規則只寫在一個地方。

### 淘汰賽名次

名次由「在哪一輪被淘汰」決定，不用同分判定。程式用一個排序鍵：冠軍 3R+3、亞軍 3R+2、季軍 3R+1、殿軍 3R、第 r 輪敗者 3r（R 是總輪數），鍵越大名次越前，相同鍵並列（`KnockoutStandings.cs`）。乘以 3 是為了在同一個整數軸上留出冠亞季殿的空間。

---

## 5.3 排名與同分判定

程式：`src/TourneyDesk.Core/Standings/`

### 第一步：ScoreSheet

先把所有已登錄的成績整理成「每位選手逐輪的得分紀錄」：

```csharp
public enum EntryKind { Played, Forfeit, Bye }
public readonly record struct ScoreEntry(int Round, EntryKind Kind, int? Opponent, int Points);
```

區分 `Played`（雙方到場打完）、`Forfeit`（棄權）、`Bye`（輪空），因為不同的同分判定只計某些類型：直接對戰、SB、勝場只計實際打完的對局。

### 第二步：每位選手的指標

```csharp
// PlayerMetrics.Compute（節錄）
foreach (var e in sheet.EntriesOf(playerId))
{
    if (e.Kind == EntryKind.Played)
    {
        var opponentTotal = sheet.Total(e.Opponent!.Value);   // 對手「目前」總分
        opponentScores.Add(opponentTotal);
        sb += opponentTotal * e.Points;                         // 半分 × 半分，仍是整數
        if (e.Points == HalfPoints.Win) wins++;
    }
    else
    {
        opponentScores.Add(VirtualOpponentScore(playerId, e.Round, sheet));  // 輪空或棄權 → 虛擬對手
    }
}
var buchholz = opponentScores.Sum();
var cut1 = opponentScores.Count == 0 ? 0 : buchholz - opponentScores.Min();
```

### 虛擬對手

輪空或棄權時沒有真正的對手，Buchholz 要用一個「虛擬對手」的分數代替：

> 虛擬對手分數 = 該選手在該輪之前的累計分數 + 1 半分 × max(0, 已完成輪數 − 該輪 + 1)

直覺：假設這位虛擬對手在那一輪之前和你同分，之後每輪都和局。

**範例**：已完成 5 輪。選手甲第 2 輪輪空，第 2 輪之前有 1 勝（2 半分）。虛擬對手 = 2 + 1 × (5 − 2 + 1) = 6 半分（3 分）。

依決策 Q5，遲到加入前、退賽後的輪次沒有「排定」的場次，不產生虛擬對手。

### 第三步：遞迴分群

規則：依總分分組 → 每個同分群組依序比較 ② 直接對戰 → ③ Cut-1 → ④ Buchholz → ⑤ SB → ⑥ 勝場。**某一項把群組分成數個子群組時，每個仍並列的子群組從 ② 重新開始。**

為什麼要重新開始？直接對戰只在「群組內兩兩都下過」時適用。5 人同分時可能不適用，但分出一個 2 人子群組後，這兩人可能下過，直接對戰就變得適用了。

這個「分群後從頭再來」的結構，天然就是遞迴：

```csharp
// src/TourneyDesk.Core/Standings/StandingsCalculator.cs
private static void Resolve(List<int> group, int fromCriterion, List<List<int>> blocks, ...)
{
    if (group.Count == 1) { blocks.Add(group); return; }

    for (var criterion = fromCriterion; criterion <= LastTieBreak; criterion++)
    {
        var values = CriterionValues(criterion, group, ...);
        if (values is null) continue;                    // 直接對戰不適用 → 下一項

        var subgroups = group.GroupBy(id => values[id]).OrderByDescending(g => g.Key).ToList();
        if (subgroups.Count == 1) continue;              // 這一項分不開 → 下一項

        foreach (var sub in subgroups)
            Resolve([.. sub], FirstTieBreak, blocks, ...);   // 每個子群組從 ② 重新開始
        return;
    }
    blocks.Add(group);                                   // 全部相同 → 並列
}
```

`blocks` 依序收集最終的名次區塊，最後轉成競賽排名（1、2、2、4）：

```csharp
foreach (var block in blocks)
{
    var rank = placed + 1;                               // 同一區塊同名次
    foreach (var id in block.OrderBy(id => state.Players[id].RegNo))   // 並列者依註冊編號顯示
        rows.Add(new StandingRow(rank, ...));
    placed += block.Count;                               // 下一區塊跳號
}
```

### 直接對戰的適用條件

```csharp
/// <summary>僅在群組內每兩人之間都至少有一場實際對局時適用；否則回傳 null。</summary>
private static Dictionary<int, long>? DirectEncounter(List<int> group, HashSet<(int, int)> playedPairs, ScoreSheet sheet)
```

回傳 `null` 表示「不適用」，與「適用但大家一樣」（回傳相同值）區分開——兩者都會跳到下一項，但語意不同，顯示上也不同：不適用時，該選手在積分榜的「直接對戰」格留空；適用時則顯示群組內的得分。

### 如何驗證

- `samples/tiebreak/`：9 人 4 輪的手算範例，README 逐項計算每個指標，測試逐項比對預期檔。
- 性質測試：**登錄成績的順序不影響積分榜**（200 組）；**所有人的半分總和守恆**（每場對局雙方得分和為 2，雙方棄權為 0，輪空為設定值；300 組）。

---

## 5.4 本章小結

1. 循環賽用 i + j ≡ 2r (mod m − 1) 一次排出全部賽程；m − 1 為奇數保證每對恰好相遇一次。
2. 先後手用與輪次無關的奇偶規則，差距 ≤ 1。
3. 淘汰籤表的每一格存「來源」而不是選手，整張籤表是 DAG；連鎖清除就是沿 DAG 找依賴。
4. 自動晉級規則集中在 `Normalize`，所有命令共用。
5. 同分判定是「分群後從頭再來」的遞迴；不適用與相同要用不同的值表示。

下一章：狀態怎麼可靠地存進檔案？多人共用檔案時怎麼避免衝突？→ [第 6 章　資料層與檔案鎖定](06-data-and-locking.md)
