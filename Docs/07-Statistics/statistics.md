# 戰績規格

## 資料來源與排除原則

不建立 Statistics Table；每次由 Battle、BattleLineup、BattleRound、有效 BattleRoundEvent 與 TournamentMatch 聚合。

陀螺總戰績按母陀螺 BeybladeId 彙總，個別陀螺頁再依當場 Lineup 的配置 ID 分版本。CX 超越／輔助戰刃改變但命名不變時，也以不同版本列出。null 配置另列「未記錄版本」並計入總數；事後補登不得回填。各版本的勝敗與分數加總等於母陀螺總數；總勝率由總勝敗計算。對手陀螺比較以雙方陀螺及配置 ID 分組，不再使用名稱文字作為識別。

玩家主要分區固定為：

1. 快速對戰。
2. Tournament 個人賽。
3. Tournament 團體隊伍結果。
4. Tournament 團體實際上場小局。

四者不可合成一個主要勝率。陀螺總覽可合併有效來源，但必須提供快速／個人／團體來源篩選及各來源樣本數。

排除：

- 尚未接受、已拒絕或撤回的快速邀請。
- 快速對戰取消後已硬刪除的 aggregate。
- Voided Battle。
- 未完成 Round 及標記為無效的 Event。
- Tournament 取消或棄權當下尚未完成 Round 的 Event。

Tournament 取消前合法完成的 Match／Battle／Round 依其原來源保留於戰績；Bye 與 Walkover 不建立虛構 Battle 或陀螺勝敗。

## 玩家總戰績

至少：

- 勝場
- 敗場
- 勝率
- 得分
- 失分
- 因發射失誤失分
- B Side 勝／敗與勝率
- X Side 勝／敗與勝率

勝率：

`勝場 / (勝場 + 敗場)`

沒有比賽時避免除以零。

快速／Tournament 個人 Forfeited Battle 依 `WinningPlayerId` 計算玩家勝敗；團體隊伍結果依 `TournamentMatch.WinnerEntryId`／`LoserEntryId`。棄權前已完成的 Round 依有效事件計算玩家與陀螺戰績；棄權當下尚未完成 Round 的所有事件全部排除。

### B／X Side 歸屬與篩選

- `Battle.SideADesignation` 表示 Battle Side A 被指定的站位，另一方必為相反站位。
- 快速對戰與個人賽依玩家是 Battle Player A 或 Player B 換算其 B／X Side。
- 團體賽隊伍結果依玩家所屬 `TournamentEntry` 是 Side A 或 Side B 換算；玩家實際小局與陀螺戰績依 `BattleRound.PlayerAId`／`PlayerBId` 換算，隊員繼承所屬隊伍的 Side。
- B Side 與 X Side 勝率分母各自只包含該 Side 的有效勝敗樣本，不得共用總樣本數。
- 未保存 `SideADesignation` 的舊資料可保留在「全部 Side」總計，但不得推測或計入 B／X 分項勝率。
- 個人分項與陀螺戰績都必須支援全部／B Side／X Side 篩選；對戰歷史必須顯示使用者當場 Side。
- Side 樣本數以該查詢實際使用的整場勝敗或完成 Round 為分母；UI 必須標示樣本數，0 樣本勝率顯示 0 或無資料，不可造成除以零。

## 陀螺戰績

每顆陀螺：

- 名稱
- 勝場
- 敗場
- 勝率
- 得分
- 失分
- 因發射失誤失分
- 因發射失誤造成對手得分
- B Side 勝／敗與勝率
- X Side 勝／敗與勝率

勝負以該陀螺在 BattleRound 中作為參戰陀螺的有效 BattleResult 判定。

LaunchFaultPenalty 是分數事件，不應單獨把整個 Round 判為敗場。

不同 Tournament RuleSet 的 ScoreToWin 不同，因此陀螺比較以完成 Round、平均每局得失分、ResultType、LaunchFault 與對位為主，不以整場總分建立強弱排行榜。

## 對手戰績

使用者可以查看：

`自己 vs 對手`

至少：

- 勝
- 敗
- 勝率
- 得分
- 失分

並可深入：

`自己的陀螺 vs 對手的陀螺`

顯示：

- 得分
- 失分
- 勝負
- 對方名稱 Snapshot

## 排序

個人分項列表與陀螺列表：

- Score DESC/ASC
- AgainstScore DESC/ASC
- ScoreDifference DESC/ASC
- WinRate DESC/ASC
- BSideWinRate DESC/ASC
- XSideWinRate DESC/ASC

兩份列表都必須提供來源篩選與全部／B Side／X Side 篩選。Side 篩選後，勝敗、得失分、平均值、ResultType 與發射失誤均以篩選後樣本重新聚合，不得只隱藏前端列。

所有篩選、聚合與排序由 Server 執行，不在前端一次載入全部資料後排序。穩定次排序使用名稱或 Id，只處理顯示順序，不改變統計勝負。

## 對戰歷史

每筆至少顯示：

- BattleId、來源、完成時間。
- 對手玩家或隊伍 DisplayName。
- 自己／對方得分與勝負。
- 使用者當場為 B Side、X Side 或未記錄。

歷史可連回授權範圍內的 Battle 詳情。進行中的 Battle 不屬於戰績歷史，必須由獨立的 active battle 清單提供返回入口。
