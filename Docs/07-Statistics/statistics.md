# 戰績規格

## 玩家總戰績

至少：

- 勝場
- 敗場
- 勝率
- 得分
- 失分
- 因發射失誤失分

勝率：

`勝場 / (勝場 + 敗場)`

沒有比賽時避免除以零。

只聚合有效完成或以棄權結束的 Battle。被拒絕或撤回的邀請，以及由裁判取消並硬刪除的 Battle，不得影響任何統計。

Forfeited Battle 依 `WinningPlayerId` 計算玩家勝敗。棄權前已完成的 Round 依有效事件計算玩家與陀螺戰績；棄權當下尚未完成 Round 的所有事件全部排除。

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

勝負以該陀螺在 BattleRound 中作為參戰陀螺的有效 BattleResult 判定。

LaunchFaultPenalty 是分數事件，不應單獨把整個 Round 判為敗場。

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

陀螺列表：

- Score DESC/ASC
- AgainstScore DESC/ASC
- WinRate DESC/ASC

所有排序由 Server / LINQ / SQL 執行，不在前端一次載入全部資料後排序。
