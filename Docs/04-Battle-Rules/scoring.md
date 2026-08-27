# 計分規則

本規則適用快速與 Tournament Battle；差異只來自 Battle 建立時鎖定的 `ScoreToWin` 與 Lineup 配對數量。

## BattleResult

| ResultType | 中文名稱 | ScoreAwarded |
|---|---|---:|
| SpinFinish | 轉停勝利 | 1 |
| KnockOut | 擊飛勝利 | 2 |
| Burst | 爆裂勝利 | 2 |
| Extreme | 極限勝利 | 3 |

Client 只能提交 WinnerPlayerId 與 ResultType 的操作意圖。Server 必須驗證操作者、Battle、Round、參賽玩家及目前狀態，並在單一交易決定 ScoreAwarded、建立事件、完成 Round、重算比分及建立合法的下一 Round；Client 不可提交任意 Score、整場比分或勝方。

## 發射失誤

fault count 以同一個未完成 Round、同一實際 Player／Beyblade 的有效事件重建：

```text
0 --LaunchFault--> 1
1 --LaunchFault--> LaunchFaultPenalty(對手 +1) --> 0
```

- LaunchFault 的 ScoreAwarded = 0。
- 第二次有效 LaunchFault 同時建立 ScoreAwarded = 1 的 LaunchFaultPenalty。
- LaunchFault 與 LaunchFaultPenalty 都不完成 Round，同一顆陀螺繼續。
- Penalty 分數同時進入 Battle 比分、得分方／陀螺得分及失誤方／陀螺失分。

## Round 與 Battle 分數

- 一個正常完成的 Round 必須有且只有一個有效 BattleResult。
- Round 可同時包含多個 LaunchFault、LaunchFaultPenalty 與一個 BattleResult。
- Battle 比分是所有有效 RoundEvent 依實際 Side A/B 方向重算的總和，正式欄位為 `SideAScore`／`SideBScore`。
- 快速對戰 `ScoreToWin = 4`。
- Tournament 依 RuleSet 使用 4、5、6 或 8 分，不得在程式或文件中假設固定 4 分。

每次新增有效得分或執行 Revision 後：

```text
if SideAScore >= ScoreToWin || SideBScore >= ScoreToWin
    Battle -> VictoryPendingCompletion
else
    Battle -> InProgress 或 ReorderSelection（依當前流程）
```

首次達門檻後不得再新增正常得分事件，但不自動 Completed。授權裁判檢查後執行 Finish；Server 再驗證門檻、唯一勝方及 Match 狀態。

## 範例

A 的龍騎士對 B 的霸王：

1. A LaunchFault。
2. A 再次 LaunchFault，建立 B +1 的 LaunchFaultPenalty，A fault 歸零。
3. A 以 SpinFinish 得 1。
4. Server 在記錄 SpinFinish 的同一交易完成 Round。

此 Round：

- A 得分 1、失分 1。
- B 得分 1、失分 1。
- A 的發射失誤失分增加 1。
- LaunchFaultPenalty 不會取代 SpinFinish，也不會單獨形成 Round 勝敗。

## Revision 重播

- 修改指定 Round 時，以新 BattleResult 取代舊有效結果並留下 Revision。
- 修改較早 Round 時，所有後續 Round 及 Event 保存原始資料並標記為 `EarlierRoundRevision` 失效，不參與比分或統計。
- 從修訂結果重新計算比分；未達門檻時從下一站位建立新 Round，已達門檻時進入 `VictoryPendingCompletion`。
- 首次達 ScoreToWin 後、不屬於較早局修訂截斷的事件標記為 `VictoryThresholdReached` 並失效。
- 棄權、取消或撤銷造成的失效使用不同 InvalidationReason，不得被一般 Revision 恢復。
