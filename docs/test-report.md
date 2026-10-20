# TourneyDesk 測試報告

| 項目 | 內容 |
|---|---|
| 版本 | `main`（本報告隨同提交） |
| 測試日期 | 2026-10-20 |
| 測試環境 | Intel Core i7-13700（8P+8E）、32 GB RAM、Windows 11 Pro 10.0.26200、.NET SDK 9.0.315／Runtime 9.0.17、WebView2 Runtime（系統內建） |
| 測試框架 | xUnit 2.9.3、bUnit 2.12.5（自寫性質測試產生器，固定種子） |
| 結論 | **自動化測試 307／307 通過**；驗收標準 1～24 **全部符合**；第 25 項（CI）見[第 6 節](#6-ci驗收-25) |

---

## 1. 執行方式與結果

```powershell
dotnet build TourneyDesk.sln -c Release     # 0 警告、0 錯誤（warnings as errors）
dotnet test  TourneyDesk.sln -c Release     # 含效能測試
dotnet test  TourneyDesk.sln                # Debug：效能測試標示為略過
```

| 測試專案 | 案例數 | 通過 | 失敗 | 略過 |
|---|---|---|---|---|
| TourneyDesk.Core.Tests | 249 | 249 | 0 | 0 |
| TourneyDesk.Data.Tests | 23 | 23 | 0 | 0 |
| TourneyDesk.UI.Tests（bUnit） | 35 | 35 | 0 | 0 |
| **合計（Release）** | **307** | **307** | **0** | **0** |

Debug 組態：305 通過、2 略過（`ReleaseOnlyFact` 效能測試；Debug 未最佳化、耗時約 5 倍，不具代表性）。

### 穩定性
驗收過程中發現 1 個偶發失敗的 UI 測試（見[第 5 節](#5-測試期間發現並修正的問題)），修正根因後：

| 組態 | 連續執行整套次數 | 失敗次數 |
|---|---|---|
| Release | 15 | 0 |
| Debug | 10 | 0 |
| Release（最終證據執行，含 TRX） | 1 | 0 |

### 各測試類別

| 類別 | 案例 | 類別 | 案例 |
|---|---|---|---|
| Core.Architecture（相依規則、禁用浮點數） | 2 | Core.Swiss.SwissPairingEngineTests | 18 |
| Core.Commands.PlayerCommandsTests | 20 | Core.Swiss.SwissPropertyTests | 4 |
| Core.Commands.SwissCommandsTests | 31 | Core.Swiss.SwissSampleTests | 3 |
| Core.Domain.DomainBasicsTests | 35 | Core.Performance（Swiss、Standings） | 2 |
| Core.Hashing.StateHasherTests | 26 | Data.Architecture | 1 |
| Core.Import.PlayerCsvImportTests | 17 | Data.FileSystemLockTests | 6 |
| Core.Knockout.KnockoutTests | 15 | Data.TournamentFileTests | 16 |
| Core.Layout.LayoutTests | 15 | UI.Architecture | 1 |
| Core.Locking.LockManagerTests | 10 | UI.PlayersViewTests | 6 |
| Core.Matching.MaxWeightMatchingTests | 9 | UI.RenderingTests | 9 |
| Core.RoundRobin.RoundRobinTests | 7 | UI.RoundsAndResultsTests | 7 |
| Core.Session（Session 12、UndoProperty 1） | 13 | UI.ShellTests | 12 |
| Core.Standings（Calculator 20、Property 2） | 22 | | |

---

## 2. 驗收標準逐項對照

判定方式欄為需求指定的方式；「證據」列出對應的測試（類別.方法）或檔案。

| # | 標準 | 判定方式 | 證據 | 結果 |
|---|---|---|---|---|
| 1 | CSV 匯入：UTF-8（有無 BOM）與 Big5 正確；重複註冊編號、積分超出範圍、缺欄位時錯誤列有行號與原因，其餘列照常匯入 | 單元測試 | `PlayerCsvImportTests.Utf8_with_and_without_bom_is_decoded`（2 例）、`Big5_is_decoded`、`Invalid_rows_report_line_and_reason_while_others_import`（既有重複、檔內重複、3001、-5、缺欄位、姓名空白、非整數、編號非整數 8 種錯誤，各有行號）、`Missing_required_header_is_reported`、`Quoted_fields_…_correct_line_numbers`；UI：`PlayersViewTests.Csv_preview_lists_error_lines_and_imports_valid_rows` | ✅ |
| 2 | 瑞士制第 1 輪：偶數與奇數都與範例檔手算一致（奇數時最後一名輪空） | 單元測試 | `SwissPairingEngineTests.Round_one_matches_hand_calculated_sample`（even／odd）；`samples/swiss-round1/`（手算說明＋預期檔） | ✅ |
| 3 | 與窮舉參考解逐案一致：N ≤ 12、≥ 500 組隨機歷史（含退賽、遲到加入、已有輪空者、棄權），固定種子 | 性質測試 | `SwissPropertyTests.Pairing_matches_brute_force_reference_for_up_to_12_players`：**600 組**、種子 3；比對完整配對（含先後手）。另隨機限制分層數以驗證分段固定。變異測試確認可抓出 ⑤ 方向錯誤 | ✅ |
| 4 | ①～④ 與位元遮罩 DP 參考解一致：N ≤ 18、≥ 200 組 | 性質測試 | `SwissPropertyTests.Optimum_values_match_bitmask_dp_reference_for_up_to_18_players`：**220 組**、種子 4 | ✅ |
| 5 | 決定性：打亂輸入順序、重新指派內部 id，結果（以註冊編號表示）相同，≥ 200 組 | 性質測試 | `SwissPropertyTests.Pairing_is_independent_of_input_order_and_internal_ids`：**220 組**、種子 5（N 至 40、0～7 輪歷史） | ✅ |
| 6 | 無法避免重複對戰時重複場數最小；輪空者全都輪空過時選排名最後者；皆有手算範例 | 單元測試 | `SwissPairingEngineTests.Unavoidable_repeat_is_kept_to_the_minimum`（6 人、A 已與全員交手 → 重複 1 場，配對 A-F B-E C-D 附推導）、`When_everyone_had_a_bye_the_last_ranked_player_gets_it`；補充 `Bye_goes_to_lowest_ranked_player_without_previous_bye`、`Avoiding_repeats_takes_priority_over_avoiding_repeated_bye` | ✅ |
| 7 | 先後手：「次數較少者先手」「次數相同看上一場」「仍相同看排名」 | 單元測試 | `Colors_fewer_whites_gets_white`、`Colors_equal_whites_previous_black_gets_white`、`Colors_still_equal_higher_rank_gets_white`（3 例）、`Colors_previous_black_beats_no_previous_game` | ✅ |
| 8 | 400 人 9 輪：每輪每位在籍選手恰好一次、無重複輪空、無重複對戰、每輪耗時符合上限 | 效能測試 | `SwissPerformanceTests.Full_400_player_9_round_tournament_meets_rules_and_time_limit`（Release）。實測見[第 3 節](#3-效能實測)：最慢 **0.93 秒** < 3 秒 | ✅ |
| 9 | 配對可取消、進度單調遞增；任何時間點取消，賽事檔與取消前逐位元組一致 | 單元測試 | `SwissPairingEngineTests.Progress_is_monotonic_and_ends_at_100`、`Cancellation_at_any_progress_point_throws_without_result`（每個進度點各取消一次）；`TournamentFileTests.Cancelled_pairing_leaves_file_byte_identical`（真實 SQLite 檔、每個進度點取消後比對 SHA-256）；UI：`Background_work_can_be_cancelled_without_changing_state` | ✅ |
| 10 | 循環賽 N = 2～64：每對恰好一次（雙循環兩次）、每輪至多一次、奇數時輪空次數相同、先後手差 ≤ 1、雙循環第二循環互換 | 性質測試 | `RoundRobinTests.Schedule_properties_hold_for_2_to_64_players`（63 個 N × 單／雙循環，全部性質）；手算 `Schedule_matches_hand_calculated_sample`（單循環 7 人、雙循環 4 人） | ✅ |
| 11 | 循環賽進行中退賽：其後場次為棄權、對手得 2 半分；輪空依設定計分 | 單元測試 | `RoundRobinTests.Withdrawal_during_round_robin_forfeits_later_games`、`Bye_points_follow_setting_in_round_robin`（0／1／2） | ✅ |
| 12 | 淘汰籤表 N = 2～128 種子位置、輪空位置一致，輪空只在第 1 輪且屬於最高種子；N = 11 與預期檔逐格比對 | 性質＋單元 | `KnockoutTests.Seeding_and_byes_hold_for_2_to_128_players`（以獨立的性質刻畫標準位置：每組和為 S+1 且較小種子構成 S/2 的標準位置；輪空 = 種子 1..S−N 且只在第 1 輪）、`Standard_positions_for_small_sizes`（2／4／8）、`Bracket_for_11_players_matches_expected_file_cell_by_cell`（`samples/knockout/expected-bracket-11.txt` 16 格） | ✅ |
| 13 | 淘汰賽更正勝者：連鎖清除範圍正確、需確認、可一步復原；退賽者場次以對手晉級 | 單元測試 | `Correcting_winner_lists_cascade_requires_confirmation_and_clears_dependents`（手算範例：清除準決賽 1、決賽、季軍戰）、`Correction_with_cascade_is_undone_in_one_step`、`Correction_without_dependents_has_empty_impact`、`Withdrawn_players_opponent_advances_immediately_when_known`、`Withdrawn_players_later_match_is_walkover_once_opponent_is_known`；UI：`RenderingTests.Correcting_knockout_winner_lists_cascade_and_applies_after_confirmation` | ✅ |
| 14 | 瑞士制更正已完成輪次後：較後輪次標示過期或維持不變，系統不自行改動已發布配對；「從第 k 輪重新配對」可執行並可復原 | 單元測試 | `Correction_that_changes_next_proposal_marks_later_round_stale_without_touching_it`、`Correction_that_does_not_change_next_proposal_keeps_round_valid`、`Manually_adjusted_round_is_not_stale_by_itself`、`Repair_from_round_requires_confirmed_impact_and_regenerates_draft`、`Repair_from_round_is_undoable_through_session`；UI：`Stale_rounds_are_flagged_and_repair_requires_confirmation` | ✅ |
| 15 | 同分者名次相同、下一名次跳號（1、2、2、4）；棄權、輪空、雙方棄權的得分與假設一致 | 單元測試 | `StandingsCalculatorTests.Competition_ranking_skips_after_ties`、`Forfeit_scores_follow_assumptions`（3 例）、`Bye_scores_follow_tournament_setting`（3 例）、`DomainBasicsTests.Result_points_follow_assumptions`（7 例）；12 人範例最終榜出現並列第 3、下一名第 5 | ✅ |
| 16 | 同分判定完整手算範例（直接對戰適用與不適用、分出較小群組後重新比較、虛擬對手、去掉最低分、退賽對手）與預期檔逐項一致 | 單元測試 | `Tiebreak_sample_matches_expected_file_item_by_item`（`samples/tiebreak/`：9 人 4 輪，README 逐項計算）；`SwissSampleTests.Final_standings_match_hand_calculation`（12 人範例） | ✅ |
| 17 | 成績登錄順序不影響結果；半分總和守恆式 | 性質測試 | `StandingsPropertyTests.Result_entry_order_does_not_affect_standings`（200 組、種子 17）、`Total_half_points_are_conserved`（300 組、種子 1717，含部分未登錄） | ✅ |
| 18 | 存取往返無損；版本過新、損毀、非本程式檔案時顯示明確訊息且不修改檔案；舊版可升級並先備份 | 單元測試 | `Committed_transitions_round_trip_losslessly`（24 組隨機操作序列，逐段重新開檔比對雜湊）、`Create_then_open_round_trips_state`、`Too_new_version_…`、`Corrupted_file_…`、`Non_sqlite_file_…`、`Foreign_sqlite_database_…`（皆比對檔案 SHA-256 不變）、`Truncated_file_is_rejected`、`Missing_file_has_readable_message`、`Old_version_is_backed_up_then_upgraded`（備份位元組與原檔相同）、`Second_upgrade_backup_name_uses_injected_clock` | ✅ |
| 19 | 鎖定：第二個程式被拒並顯示持有者；殘留鎖確認後接手；他機持有不可自動接手；關閉與當掉後狀態正確 | 單元測試（注入時鐘與程序檢查） | `LockManagerTests`（10 例：第二程式拒絕、他機持有中不可強制、正常關閉釋放、當機殘留需確認、程序仍在則拒絕、PID 重用、他機需確認字串、內容損毀、電腦名稱大小寫）；`FileSystemLockTests`（6 例，真實檔案系統）；UI：鎖定提示 3 例。**實機**：強制結束程式後重開出現殘留鎖提示並成功接手；正常關閉後鎖定檔已刪除 | ✅ |
| 20 | 復原與重做 ≥ 50 層，涵蓋所有操作種類；每步復原後整個賽事的雜湊與該步之前相同 | 性質測試 | `UndoPropertyTests.Every_undo_restores_exact_previous_state_and_redo_reapplies`（45 組、種子 20、每組 ≥ 50 步，逐步復原比對雜湊、再逐步重做比對；並斷言 17 種命令全部出現）；`TournamentSessionTests.Undo_supports_at_least_fifty_levels` | ✅ |
| 21 | 版面計算：各格無重疊、連線端點正確、分頁無遺漏與重複；SVG 決定性 | 單元＋bUnit | `LayoutTests`（N=2..128 無重疊且在頁內、連線端點與正交、父居中、籤表分頁 6 例、交叉表分頁 4 例含 400 人、決定性）；bUnit：`Bracket_svg_is_deterministic`、`Bracket_svg_draws_every_box_and_connector_at_layout_coordinates`、`Crosstable_svg_is_deterministic_and_draws_every_cell`、`Printable_bracket_renders_each_paginated_page` | ✅ |
| 22 | 400 人積分榜重算符合上限；400 列時 DOM 渲染列數遠少於 400 | 效能＋bUnit | `StandingsPerformanceTests`：**1.3 ms** < 200 ms；`PlayersViewTests.Four_hundred_players_are_virtualized`：模擬 520 px 可視區時只渲染 10～40 列（無可視區資訊時亦 ≤ 100 列） | ✅ |
| 23 | 核心與 UI 邏輯有單元測試，≥ 180 個案例（含 ≥ 7 組性質測試），dotnet test 全數通過 | 執行測試 | **307 個案例**、全數通過；以隨機輸入、固定種子的性質測試 11 組（第 4 節） | ✅ |
| 24 | README 含建置步驟、截圖、架構、瑞士制最佳化與同分判定演算法、效能數據、設計取捨與已知限制 | 檢視 README | `README.md` 各節齊備；截圖 6 張為實機畫面（`docs/screenshots/`） | ✅ |
| 25 | GitHub Actions 在 Windows runner 建置並測試，CI 綠燈，README 有徽章，上傳 win-x64 發佈檔 | 檢視 Actions | `.github/workflows/ci.yml`、README 徽章已指向 `hn83320589/TourneyDesk`。執行結果見第 6 節 | 見第 6 節 |

---

## 3. 效能實測

`dotnet test -c Release --filter Category=Performance`，單獨執行、連續三次：

| 輪次 | 第 1 次 | 第 2 次 | 第 3 次 |
|---|---|---|---|
| 1 | 178 | 178 | 193 |
| 2 | 739 | 799 | 745 |
| 3 | 538 | 543 | 552 |
| 4 | 449 | 627 | 640 |
| 5 | 452 | 499 | 506 |
| 6 | 778 | 763 | 780 |
| 7 | 645 | 569 | 605 |
| 8 | 743 | 787 | 807 |
| 9 | 903 | 929 | 902 |
| **最慢** | **903 ms** | **929 ms** | **902 ms** |

（單位 ms；需求 < 3000 ms。i7-13700 為大小核架構，數值會因排程落點略有波動；開發期量測最慢為 760 ms。）

- 400 人積分榜重算：1.3～1.4 ms（需求 < 200 ms）。
- 參考：500 人（人數上限）每輪最慢 2.1 秒。
- CI 雲端 runner 以 `TOURNEYDESK_PERF_FACTOR=2` 放寬門檻為 6 秒。

---

## 4. 性質測試清單（≥ 7 組，皆為隨機輸入＋固定種子）

失敗時訊息印出「主種子、第幾案例、案例種子」，可用 `Property.Replay(案例種子, …)` 單獨重現。

| # | 測試 | 主種子 | 案例 |
|---|---|---|---|
| 1 | 瑞士制 vs 窮舉參考解（N ≤ 12） | 3 | 600 |
| 2 | 瑞士制 ①～④ vs 位元 DP（N ≤ 18） | 4 | 220 |
| 3 | 瑞士制決定性 | 5 | 220 |
| 4 | 引擎結果不劣於隨機合法方案（N 20～60） | 6 | 60 |
| 5 | 同分判定：登錄順序無關 | 17 | 200 |
| 6 | 半分總和守恆 | 1717 | 300 |
| 7 | 復原／重做雜湊（隨機操作序列） | 20 | 45 |
| 8 | Blossom vs 暴力（完全圖，含緊邊性質） | 20261020 | 400 |
| 9 | Blossom vs 暴力（稀疏圖） | 7 | 300 |
| 10 | 賽事檔往返無損 | 18 | 24 |
| 11 | （窮舉）循環賽 N = 2～64、淘汰賽 N = 2～128 | — | 126／127 |

**參考解獨立性**：`tests/TourneyDesk.Core.Tests/Reference/` 只讀取賽事原始資料（選手、對局、輪空），自行計算分數、排名、曾交手、輪空紀錄與先後手，不呼叫正式程式碼的任何計算；窮舉以「輪空者 × 全部完美匹配」逐一比較字典序，DP 以位元遮罩求 ①～④。

---

## 5. 測試期間發現並修正的問題

本次驗收（以及前一階段）發現的問題都已修正並加入測試：

| 問題 | 發現方式 | 修正 | 提交 |
|---|---|---|---|
| UI 測試偶發失敗（約 1/8，整套平行執行時）。根因：bUnit 的 `WaitForAssertion` 只在元件重繪時重新檢查，而 `RoundsView` 不監聽 Workspace 變更（實際 App 由外層 Shell 重繪），背景判定若在首次渲染後才完成即逾時 | 重複執行整套測試；第一次只放寬逾時無效，失敗訊息「Check count: 1」指出根因 | 測試改為直接等待背景判定完成後再渲染；正式程式不受影響 | `f1c4ef6` |
| 列印草稿輪次的對戰表時，「與最佳解比較」面板與頁首一併印出 | 實機開啟 WebView2 列印預覽 | 比較面板、過期提示標為只在螢幕顯示；列印時隱藏頁首列；加回歸斷言 | `4554dc9` |
| 舊版升級備份檔名的時間戳使用 `DateTime.Now`，違反「現在時間透過 TimeProvider」 | 靜態掃描 | `TournamentFile.Open` 改為注入 `TimeProvider`；新增測試 | `ac55bec` |
| 「分數不得用浮點數」缺少自動檢查（設計決定 Q8 漏做） | 對照設計文件 | 新增反射測試掃描 Core 全部公開 API；變異測試確認有效 | `ac55bec` |
| （前一階段）`INSERT OR REPLACE` 遇註冊編號衝突會默默刪除另一位選手 | 交易回滾測試 | 改為 `ON CONFLICT(id) DO UPDATE` | `5e6652f` |
| （前一階段）匯出 CSV 有試算表公式注入風險 | 自動安全檢查 | 危險開頭字元前加單引號 | `c7a1813` |

---

## 6. CI（驗收 25）

- 設定：`.github/workflows/ci.yml` —— `windows-latest`、`actions/setup-dotnet@v4`（9.0.x）、Release 建置與全部測試（含效能測試，門檻 ×2）、上傳測試結果與 `TourneyDesk-win-x64`（self-contained）發佈檔。
- README 徽章：`https://github.com/hn83320589/TourneyDesk/actions/workflows/ci.yml/badge.svg`。
- 執行結果：推送後填寫於下方。

<!-- CI-RESULT -->

---

## 7. 技術限制檢查

| 限制 | 檢查方式 | 結果 |
|---|---|---|
| C#／.NET 9；WinForms + BlazorWebView；不用 WPF／WinUI／MAUI／Avalonia | 專案檔與發佈輸出檢查（發佈資料夾無 WPF／WinUI／MAUI／Avalonia 組件；WebView2 套件自動加入的 WPF 參考已移除） | ✅ |
| Core 純 net9.0、不引用 Razor／WinForms；UI 為 RCL 不引用 WinForms | 架構測試 3 例（反射檢查參考組件） | ✅ |
| Data 使用 Microsoft.Data.Sqlite 9.0.x | 套件清單 | ✅ |
| 分數一律整數半分，不用 double／float | `NoFloatingPointTests`（反射）；靜態掃描 `src` 無 `double`／`float`／`decimal` | ✅ |
| 「現在時間」透過 TimeProvider；「程序是否存在」透過介面 | 靜態掃描 `src` 無 `DateTime.Now`／`UtcNow`；鎖定以 `IProcessInspector` 注入 | ✅ |
| 不使用付費授權套件 | 套件清單：Microsoft.AspNetCore.Components.*、Microsoft.Data.Sqlite（MIT）、xunit（Apache-2.0）、bunit（MIT）、Microsoft.NET.Test.Sdk（MIT） | ✅ |
| 不呼叫外部網路服務；字型、腳本、圖示皆本機 | 靜態掃描：`src` 中的網址僅有缺少 WebView2 時顯示給使用者的下載網址文字與 SVG xmlns 識別字；匯出 HTML 測試斷言無 `<script>`／`<link>`／外部網址 | ✅ |
| 名稱與排序採 ordinal | 靜態掃描無文化相依比較；排序皆依數值或註冊編號 | ✅ |
| WebView2 Runtime 不存在時顯示說明視窗與下載連結文字、不崩潰 | **實機**：以環境變數 `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` 指向不存在的資料夾，程式顯示說明視窗（`docs/screenshots/webview2-missing.png`） | ✅ |
| 窮舉與 DP 參考解寫在測試專案、與正式程式碼獨立 | 第 4 節 | ✅ |

---

## 8. 實機驗證（手動）

| 項目 | 結果 |
|---|---|
| 發佈 self-contained win-x64（約 112 MB）並直接啟動 | ✅ 正常顯示主視窗 |
| 以參數開啟示範賽事（40 人瑞士制） | ✅ |
| 草稿點選兩人交換 → 比較面板即時顯示 ④ 由 1 變 5、「與最佳解不同」→ 復原 → 重新開檔確認已還原 | ✅ |
| 積分榜、交叉表、淘汰籤表（輪空、季軍戰）畫面 | ✅ |
| WebView2 列印預覽：積分榜（2 頁）、對戰表（每桌一列含桌號、輪空列） | ✅（修正面板誤印後；`docs/screenshots/print-*.png`） |
| 強制結束程式 → 重新開啟顯示殘留鎖（電腦、PID、開啟時間）→ 接手成功 | ✅ |
| 正常關閉 → 鎖定檔已刪除 | ✅ |
| 賽事已有草稿時嘗試遲到加入 → 顯示可讀錯誤、資料未變更 | ✅ |
| 缺少 WebView2 Runtime 的說明視窗 | ✅ |
| 400 人／示範賽事產生工具（輪次完成時產生 `.r{N}.bak`／`.ko-r{N}.bak`） | ✅ |

---

## 9. 未涵蓋與限制

- **網路磁碟**：鎖定在真實 SMB 共用上未實測（測試環境無網路磁碟）。鎖定機制依賴 Windows 共用模式，SMB 伺服器會強制；判定邏輯已以注入介面完整測試。
- **實體列印**：只驗證到 WebView2 列印預覽，未送到實體印表機。
- **淘汰籤表列印**：分頁由版面測試與 bUnit 驗證，未在實機列印預覽中逐頁檢視。
- 效能數值依硬體與排程有波動；500 人（上限）最慢約 2.1 秒，餘裕較小。
