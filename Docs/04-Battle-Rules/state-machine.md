# Battle 狀態與規則

## Battle 狀態

```text
InvitationPending -> LineupSelection -> LineupReview
                       ^                 |
                       +-- edit accepted-+
LineupReview -> LineupLocked -> SideSelection -> InProgress
InProgress -> ReorderSelection -> InProgress
InProgress -> VictoryPendingCompletion -> Completed
InProgress -> Forfeited
InProgress -> cancel -> hard delete aggregate
```

被拒絕或由發起人撤回的未接受邀請直接硬刪除，不建立 Battle、Rejected 或 Cancelled 狀態。進行中的 Battle 若由裁判取消，也硬刪除整個 aggregate，不保留 Cancelled 狀態。

## 狀態規則

### InvitationPending

只顯示站內待處理邀請。接受後進入陣容選擇；拒絕或由發起人撤回後硬刪除。

### LineupSelection

可以：

- 玩家選擇自己的三顆陀螺及順位
- 離開頁面新增陀螺後返回同一場對戰
- 刷新自己的陀螺清單與對戰狀態

雙方都提交前不得公開任一方的選擇。

### LineupReview

雙方選擇同時公開。雙方各自確認，或各自對每版陣容提出至多一次重新編輯請求；接受請求會使雙方解鎖，拒絕則維持原版且提出者不得再次要求。

### LineupLocked

雙方三顆陀螺與順位鎖定。

不可更換。

### SideSelection

裁判必須將兩名玩家分別指定為 B Side（藍色）與 X Side（紅色）。開始後站邊、陣容與所有賽前資料均不可修改。

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

### Forfeited

裁判指定棄權者，另一方成為 `WinningPlayerId`，不要求勝者達到 4 分。棄權前已完成的 Round 保留並計入戰績；棄權當下尚未完成 Round 的所有事件均不計入戰績。Forfeited 是唯讀終止狀態。

### 取消（不是狀態）

裁判經二次確認後，以單一資料庫交易硬刪除 Battle、Lineup、Round、Event 與 Revision。任何刪除失敗時整筆交易回滾；成功後該場內容不得進入任何戰績聚合。

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
- 雙方各自保密排序並提交
- 雙方都提交後直接生效，不再進入 LineupReview
