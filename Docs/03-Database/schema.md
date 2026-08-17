# 資料庫規格

SQLite + EF Core。

## User

- Id PK
- Account UNIQUE NOT NULL
- PasswordHash NOT NULL
- DisplayName NOT NULL
- CreatedAtUtc
- UpdatedAtUtc

## Beyblade

- Id PK
- UserId FK
- Name NOT NULL
- IsDeleted
- CreatedAtUtc
- UpdatedAtUtc

Unique constraint：

`(UserId, Name)`。

若採軟刪除，唯一索引需依 SQLite/EF Core 實作方式確保不影響歷史資料。

## Battle

- Id PK
- PlayerAId FK
- PlayerBId FK
- CreatedByUserId FK
- Status
- PlayerAScore
- PlayerBScore
- WinningPlayerId nullable
- ForfeitedPlayerId nullable
- BSidePlayerId nullable
- XSidePlayerId nullable
- CreatedAtUtc
- StartedAtUtc nullable
- CompletedAtUtc nullable
- Version / concurrency token（如實作需要）

PlayerAId != PlayerBId。

`Forfeited` 時 WinningPlayerId 與 ForfeitedPlayerId 必須是本場不同玩家。B Side 與 X Side 必須分別對應本場兩名玩家。

## BattleLineup

表示某一個順位版本。

- Id PK
- BattleId FK
- SequenceNo
- PlayerA_BeybladeId FK
- PlayerA_BeybladeNameSnapshot
- PlayerB_BeybladeId FK
- PlayerB_BeybladeNameSnapshot
- IsCurrent

每次重新排列產生新的 lineup sequence，而不是修改歷史順位。

每個 lineup 必須包含：

- Player A 1/2/3
- Player B 1/2/3

## BattleRound

- Id PK
- BattleId FK
- LineupId FK
- RoundNo
- PositionNo
- PlayerA_BeybladeId FK
- PlayerA_BeybladeNameSnapshot
- PlayerB_BeybladeId FK
- PlayerB_BeybladeNameSnapshot
- Status
- CreatedAtUtc
- CompletedAtUtc nullable

Round 是「一顆陀螺對另一顆陀螺的一段對戰」，不是單一勝負事件。

## BattleRoundEvent

- Id PK
- BattleRoundId FK
- EventSequence
- EventType
- ActorPlayerId nullable
- WinnerPlayerId nullable
- ResultType nullable
- ScoreAwarded
- IsEffective
- CreatedAtUtc

EventType 至少：

- LaunchFault
- LaunchFaultPenalty
- BattleResult

LaunchFault：
- 一次發射失誤。
- ScoreAwarded = 0。

LaunchFaultPenalty：
- 第二次失誤產生的 1 分。
- WinnerPlayerId = 對手。
- ScoreAwarded = 1。

BattleResult：
- SpinFinish / KnockOut / Burst / Extreme。
- WinnerPlayerId 必填。
- ScoreAwarded 依 ResultType。

## BattleRoundRevision

- Id PK
- BattleRoundId FK
- ChangedByUserId FK
- ChangedAtUtc
- Reason nullable
- PreviousEffectiveEventSnapshot
- NewEffectiveEventSnapshot

第一版可以將事件快照序列化為 JSON 字串儲存。

目的不是建立複雜版本控制，而是保留判決修改歷史。

## 不建立 Statistics Table

玩家、陀螺、對手戰績全部從 Battle / BattleRound / BattleRoundEvent 聚合。
