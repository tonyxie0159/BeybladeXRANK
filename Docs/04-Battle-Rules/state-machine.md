# Battle 狀態與流程

## 快速邀請不是 Battle 狀態

```text
QuickBattleInvitation
  ├─ decline / withdraw -> hard delete invitation
  └─ accept -> transaction: create Battle(LineupSelection) + delete invitation
```

不得在接受前建立 Draft Battle，也不得使用 InvitationPending、Rejected 或 Cancelled 表示快速邀請。

## 快速 Battle 主流程

```text
LineupSelection
  -> LineupReview
      ├─ edit accepted -> 下一個 SequenceNo 的 LineupSelection
      └─ both confirmed -> LineupLocked
  -> SideSelection
  -> InProgress
      ├─ 一組完成且未達門檻 -> ReorderSelection -> InProgress
      ├─ 達門檻 -> VictoryPendingCompletion -> Completed
      ├─ forfeit -> Forfeited
      └─ confirmed cancel -> hard delete aggregate
```

### LineupSelection

- 每位玩家只能提交自己的 Beyblade 與順位。
- 快速對戰每人三顆且不可重複。
- 同一 Sequence 的雙方都提交前，對手不可看見選擇。
- 可離開新增 Beyblade、刷新、登出或瀏覽其他頁面，再由相同 BattleId 返回。

### LineupReview

- 雙方資料同時公開。
- 每位玩家各自確認。
- 每位玩家對每個 Lineup Sequence 最多提出一次重新編輯請求。
- 對方接受後雙方提交與確認失效，進入下一 Sequence；拒絕後維持原版本。

### LineupLocked 與 SideSelection

- 雙方確認後物化正式 BattleLineup，首次陀螺集合不可更換。
- 發起人明確指定資料 Side A 是 B Side 或 X Side，另一側自動為相反站位。
- 指定完成後狀態為 SideSelection；只有這個狀態可開始。
- 開始後 Side、首次 Lineup、Player／Entry 歸屬不可修改。

### InProgress

授權裁判可：

- 記錄 LaunchFault。
- 以單一交易記錄 BattleResult、完成 Round、重新計分並在合法時建立下一 Round。
- 在目前 Sequence 全部 Position 完成且雙方未達 ScoreToWin 時進入 ReorderSelection。
- 執行快速棄權或進入 Tournament 對局棄權流程。

### ReorderSelection

- 每位玩家只提交自己的原始陀螺新順序。
- Tournament 團體代表人另提交本隊出戰者新順序。
- 全部必要提交前保持私密。
- 全部完成後物化下一個 BattleLineup Sequence，分數與歷史不歸零，也不再回到 LineupReview。

### VictoryPendingCompletion

- 表示一側已達該 Battle 的 ScoreToWin。
- 不得新增後續正常得分或 Round。
- 可查看事件並透過授權 Revision 修正。
- 授權裁判執行 Finish 後才進入 Completed。

### Completed

- 禁止一般事件、Lineup 或 Side 修改。
- Revision 必須保存原因及 audit，並依 Tournament 下游狀態決定是否允許。

### Forfeited

- 勝方不必達 ScoreToWin。
- 已完成 Round 保留；當下未完成 Round 的 Event 標記 BattleTerminated 並排除統計。
- 快速對戰以 WinningPlayerId 表示勝者；Tournament 團體以 Match WinnerEntryId／LoserEntryId 表示。
- Forfeited 是終止狀態，只能查看，不接受一般 Revision。

## 取消與撤銷的來源差異

### 快速對戰取消

不是持久化狀態。發起人二次確認後，在單一交易硬刪除 Battle、LineupSelection、TeamOrderSelection、Lineup、Round、Event 與 Revision。成功後不進入任何統計。

### Tournament 取消

Tournament 進入 Cancelled，保留報名、賽程、原因與完成資料。未完成 Match／Battle 進入 Cancelled；已完成 Round 依有效規則保留，未完成 Round Event 排除。不得套用快速對戰硬刪除規則。

### Tournament Void／Reopen

主辦方提供原因並確認後：

- 原 Battle 進入 Voided，與 active TournamentMatch 解除並保存到 VoidedBattles。
- 原 Battle 完全排除統計，但保留操作者、時間、原因與前後快照。
- Match 清除結果、參賽確認與私密 Lineup，重新回到出賽通知流程。
- 下游已有資料時套用明確的逆序撤銷保護。

BattleStatus 的 Draft 數值只為既有持久化資料相容，不是有效新流程。舊雙邊 Lineup／Reorder Service API 已移除；新的 UI、Service contract 或測試不得建立 Draft Battle。

## Tournament Match 狀態

```text
WaitingForParticipants
  -> AwaitingParticipationConfirmation
      ├─ decline / organizer no-show -> Walkover
      └─ all accepted -> ReadyForLineup / LineupSelection
  -> TeamOrderSelection（團體需要時）
  -> LineupReview
  -> LineupLocked
  -> SideSelection
  -> InProgress
      ├─ ReorderSelection -> InProgress
      ├─ VictoryPendingCompletion -> Completed
      └─ participant forfeit -> Forfeited
```

另有 Bye／NotRequired／Voided／Cancelled 終止或賽程控制狀態。Bye 與 Walkover 不建立有比分 Battle。

No-show 只允許主辦方在 AwaitingParticipationConfirmation 對仍有必要選手為 Pending 的確定 Entry 二次確認操作；未回覆不會自動倒數。成功後未回覆敗方標記為 NoShow、對手取得 Walkover，重複操作因 Match 已終止而拒絕。

每個 Tournament 同時只能有一個 active Match。完成或 Walkover 後由 TournamentProgressionService 解析 Winner／Loser 來源並啟動下一個合法 Match；不得由 Client 指定晉級者或下一場。

## 持久化與重複請求

- 所有狀態與提交都在後端保存，頁面刷新不是狀態來源。
- 重複提交完成 Round、Finish、出賽回覆、棄權或賽程推進不得重複計分、通知或晉級。
- 可競爭的操作使用 transaction 與 Version concurrency token。
- 對戰入口必須能依目前狀態導向 Setup、Reorder、Battle 或 Tournament Match，不要求使用者記住 Id。
