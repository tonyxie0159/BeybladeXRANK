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

