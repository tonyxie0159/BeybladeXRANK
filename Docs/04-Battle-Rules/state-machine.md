# Battle 狀態與規則

## Battle 狀態

```text
Draft
  |
LineupLocked
  |
InProgress
  |
VictoryPendingCompletion
  |
Completed
```

Cancelled 若需求沒有提出，第一版不需要。

## 狀態規則

### Draft

可以：

- 選對手
- 選陀螺
- 排序

### LineupLocked

雙方三顆陀螺與順位鎖定。

不可更換。

### InProgress

可以：

- 建立 Round
- 記錄 BattleResult
- 記錄 LaunchFault
- 發生 LaunchFaultPenalty
- 完成一顆陀螺的 Round
- 三顆打完後建立新的 Lineup

### VictoryPendingCompletion

表示某方已達 >=4 分。

仍可：

- 查看事件
- 修改該局判決
- 檢查比分

不可自行繼續新增下一顆正常對戰。

操作人必須：

`Finish Battle`

### Completed

不可進行一般新增事件。

若產品流程允許修改已完成 Battle，必須經明確的「修改判決」流程，並重新驗證整場結果；第一版不提供任意編輯。

## Round 完成

一個 Round 的正常完成條件是：

- 記錄一個有效 BattleResult。

LaunchFault 不會完成 Round。

LaunchFaultPenalty 也不會完成 Round。

## Launch Fault

同一 Round、同一顆陀螺的 fault count：

```text
0 -> 第一次失誤 -> 1
1 -> 第二次失誤 -> 對手 +1 -> 0
```

fault count 必須能從有效事件重建，避免單獨保存的計數與歷史不同步。

## 勝利條件

每次有效分數變更後：

```text
if PlayerAScore >= 4 || PlayerBScore >= 4
    Battle -> VictoryPendingCompletion
```

若判決修改使兩邊都低於 4：

```text
VictoryPendingCompletion -> InProgress
```

若修改使另一方達 >=4：

仍由操作人檢查後按 Finish Battle。

## 重新排列

只有：

- 三個 Position Round 都完成
- 雙方都 <4

才允許建立新 Lineup。

新 Lineup：

- 只能使用原本三顆
- 順序可以改
- 分數不歸零
- RoundNo 繼續遞增

