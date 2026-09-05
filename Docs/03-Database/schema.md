# 資料庫規格

PostgreSQL 18 + Npgsql EF Core migrations。以下是現行正式資料概念；省略 navigation property，但不省略會影響規則與歷史的欄位。正式 schema 由獨立 migration service 套用，Web Application 啟動時不直接修改 schema。

## User

- `Id` PK
- `Account` UNIQUE NOT NULL
- `NormalizedAccount` UNIQUE NOT NULL
- `PasswordHash` NOT NULL
- `DisplayName` NOT NULL
- `NormalizedDisplayName` UNIQUE NOT NULL
- `CreatedAtUtc`、`UpdatedAtUtc`

Account 是不公開的登入識別；Account 與 DisplayName 都以 trim 後、英文不分大小寫的正規化值建立唯一索引。所有資料關聯使用 UserId，不使用 DisplayName。

## UserNotification

- `Id` PK、`UserId` FK
- `Kind`、`Title`、`Message`、`TargetUrl`
- `EntityType`、`EntityId` nullable
- `ActionType`、`ActionEntityId` nullable、`DedupeKey` UNIQUE
- `CreatedAtUtc`、`ReadAtUtc` nullable、`ResolvedAtUtc` nullable

通知與對應業務變更在同一交易保存。`TargetUrl` 與 `ActionType` 只能由 Server 列舉的安全動作產生，不接受 Client 任意處理器或網址。

## Beyblade

- `Id` PK
- `UserId` FK
- `Name` NOT NULL
- `IsDeleted`
- `CreatedAtUtc`、`UpdatedAtUtc`

`(UserId, Name)` 唯一。Delete 採軟刪除；歷史 Lineup／Round 仍保留 Id 與名稱 Snapshot，新 Lineup 不得選擇已刪除項目。

## 零件目錄與配置版本

詳見 [零件系統](parts-system.md) 及 [279 個零件名稱](parts-catalog.md)。Part 保存分類與名稱，PartSeries 提供系列篩選，BeybladeConfiguration 與 BeybladeConfigurationPart 保存不可覆寫的完整配置版本。

Beyblade 新增 nullable UpperName；有效陀螺以 UserId＋UpperName 唯一。BeybladeConfiguration 為一對多，新增 VersionNo 與 PartsKey，分別以 BeybladeId＋VersionNo、BeybladeId＋PartsKey 唯一。各版本不可覆寫；相同零件集合重用原版本。舊資料不自動補登。BattleLineupSelection.BeybladeConfigurationId、BattleLineup.PlayerAConfigurationId／PlayerBConfigurationId 是 nullable 配置快照關聯；舊對戰維持 null。Round 透過 LineupId 查詢當場配置。

## QuickBattleInvitation

- `Id` PK
- `InviterUserId` FK
- `InviteeUserId` FK
- `CreatedAtUtc`
- `Version` concurrency token

邀請是 Battle 之外的獨立 aggregate。接受時交易式建立 Battle 並刪除邀請；拒絕／撤回時只硬刪除邀請。不得保存 Rejected／Cancelled 快速邀請歷史。

## Battle

- `Id` PK
- `SourceType`：Quick／TournamentIndividual／TournamentTeam
- `ScoreToWin`
- `TournamentMatchId` nullable，active Tournament Battle 專用
- `VoidedTournamentMatchId` nullable，已撤銷 Tournament Battle 專用
- `PlayerAId`、`PlayerBId` nullable；快速／個人賽有值，團體賽整場可為 null
- `CreatedByUserId` FK
- `Status`
- `SideAScore`、`SideBScore`
- `SideADesignation` nullable：B／X
- `WinningSide` nullable：B／X
- `WinningPlayerId` nullable；團體賽以 TournamentMatch WinnerEntryId 為準
- `LineupSequenceNo`
- `PlayerALineupConfirmed`、`PlayerBLineupConfirmed`
- `PlayerAEditRequestUsed`、`PlayerBEditRequestUsed`
- `PendingLineupEditRequestedByUserId` nullable
- `VoidedByUserId`、`VoidReason`、`VoidSnapshot`、`VoidedAtUtc` nullable
- `CreatedAtUtc`、`StartedAtUtc`、`CompletedAtUtc`
- `Version` concurrency token

正式比分欄位只有 `SideAScore`／`SideBScore`。程式中的 `PlayerAScore`／`PlayerBScore` 是暫時的 NotMapped 相容 alias，不是資料庫規格，後續不得再被新程式依賴。

不保存 `BSidePlayerId`、`XSidePlayerId` 或 `ForfeitedPlayerId`：

- Side A 的站位取自 `SideADesignation`，Side B 為相反值。
- 快速／個人賽玩家站位依 Player A/B 換算。
- 團體站位依 TournamentMatch Side A/B Entry 換算。
- 棄權方由勝方的相反側或 `TournamentMatch.LoserEntryId` 推導。

Quick Battle 必須沒有 TournamentMatchId；非 Voided Tournament Battle 必須有 TournamentMatchId；Voided Battle 轉移到 VoidedTournamentMatchId 並保存完整 audit。

## BattleLineupSelection

保存每位玩家尚未物化、需要保密的提交：

- `Id` PK
- `BattleId` FK
- `SequenceNo`
- `UserId` FK
- `PositionNo`
- `BeybladeId` FK
- `PlayerDisplayNameSnapshot`
- `BeybladeNameSnapshot`
- `SubmittedAtUtc`

同一 Battle／Sequence／User／Position 唯一；同一 Battle／Sequence 不得重複 BeybladeId。公開前只可查詢登入者自己的 selection。

## BattleTeamOrderSelection

保存團體代表人的私密出戰順序：

- `Id` PK
- `BattleId` FK
- `SequenceNo`
- `TournamentEntryId` FK
- `UserId` FK，該順位實際選手
- `PositionNo`
- `SubmittedByUserId` FK
- `SubmittedAtUtc`

同一 Sequence／Entry 的 Position 與 User 都必須唯一。

## BattleLineup

雙方必要提交完成後物化的正式配對：

- `Id` PK
- `BattleId` FK
- `SequenceNo`
- `PositionNo`
- `PlayerAId`、`PlayerBId` nullable FK
- `PlayerADisplayNameSnapshot`、`PlayerBDisplayNameSnapshot`
- `PlayerABeybladeId`、`PlayerBBeybladeId` FK
- `PlayerABeybladeNameSnapshot`、`PlayerBBeybladeNameSnapshot`
- `IsCurrent`

每次重排建立新 Sequence，不修改舊 Lineup。Position 數量依 Battle RuleSet，不固定假設為三。

## BattleRound

- `Id` PK
- `BattleId` FK
- `LineupId` FK
- `RoundNo`
- `PositionNo`
- `PlayerAId`、`PlayerBId` nullable FK
- `PlayerADisplayNameSnapshot`、`PlayerBDisplayNameSnapshot`
- `PlayerABeybladeId`、`PlayerBBeybladeId`
- `PlayerABeybladeNameSnapshot`、`PlayerBBeybladeNameSnapshot`
- `Status`
- `CreatedAtUtc`、`CompletedAtUtc` nullable

Round 是一個正式 Player／Beyblade 配對的一段對戰，不是單一得分事件。BeybladeId 從 FK-backed BattleLineup 複製；目前 Round 本身不另建 Beyblade navigation FK，歷史完整性由 LineupId、Id snapshot 與名稱 snapshot 共同維持。

B／X Side 不在每個 Round 重複保存，而是從不可變的 Battle.SideADesignation 與 Round Player A/B 方向推導。

## BattleRoundEvent

- `Id` PK
- `BattleRoundId` FK
- `EventSequence`
- `EventType`：LaunchFault／LaunchFaultPenalty／BattleResult
- `ActorPlayerId` nullable
- `WinnerPlayerId` nullable
- `ResultType` nullable
- `ScoreAwarded`
- `IsEffective`
- `InvalidationReason` nullable：SupersededByRevision／VictoryThresholdReached／BattleTerminated
- `CreatedAtUtc`

規則：

- LaunchFault：ScoreAwarded = 0。
- LaunchFaultPenalty：第二次有效失誤產生，WinnerPlayerId 是對手，ScoreAwarded = 1。
- BattleResult：WinnerPlayerId 與 ResultType 必填，分數由 Server rule mapping 決定。
- 同一 Round 的 EventSequence 唯一；統計只聚合有效事件。

## BattleRoundRevision

- `Id` PK
- `BattleRoundId` FK
- `ChangedByUserId` FK
- `ChangedAtUtc`
- `Reason`
- `PreviousEffectiveEventSnapshot`、`NewEffectiveEventSnapshot`
- `PreviousBattleSnapshot`、`NewBattleSnapshot`

新 Revision 流程要求 Reason，並保存 Round 與整場重播前後 JSON snapshot。資料欄位為了相容既有 migration 可為 nullable，但 Application 不得建立沒有原因的新 Revision。

## Tournament

- 模式：`Mode`、`Format`、`RegistrationMode`、`RuleSet`
- 狀態：`Status`、`RegistrationStage`
- 規則快照：`TeamSize`、`BeybladesPerPlayer`、`ScoreToWin`、`RulesSnapshot`
- 基本資料：`Name`、`OrganizerUserId`、`TargetEntryCount`、`Notes`
- 取消資料：`CancellationReason`、`CancelledAtUtc`
- 時間：Created／Updated／RegistrationClosed／Started／Completed
- `Version` concurrency token

建立後以 Server-side TournamentRuleCatalog 決定規則欄位，Client 不可提交任意 TeamSize、BeybladesPerPlayer 或 ScoreToWin。

## TournamentEntry 與 TournamentEntryMember

`TournamentEntry`：

- TournamentId、RegistrationNumber、SchedulePosition
- DisplayNameSnapshot、TeamName nullable、IndividualUserId nullable
- Status、Created／Updated／Registered／Withdrawn 時間

`TournamentEntryMember`：

- TournamentId、TournamentEntryId、UserId
- MemberOrder、IsRepresentative、DisplayNameSnapshot、JoinedAtUtc

同一 Tournament 的 RegistrationNumber 唯一；同一 User 同時只能屬於一個未 Withdrawn Entry。團體只存在於該 Tournament。

## TournamentInvitation

- TournamentId、TournamentEntryId nullable
- InvitedUserId、InvitedByUserId
- Type：Tournament／Team／RepresentativeTransfer
- Status：Pending／Accepted／Declined／Invalidated／Cancelled
- Created／Responded／Invalidated 時間

Tournament invitation 必須保留狀態歷史；它與拒絕後硬刪除的 QuickBattleInvitation 不同。

## TournamentMatch 與 TournamentMatchParticipant

`TournamentMatch` 保存：

- Bracket（Winners／Losers／GrandFinal／RoundRobin／Swiss／Playoff）、RoundNumber、MatchNumber、SequenceNumber、Status
- Side A/B 的來源種類與來源 Id
- SideAEntryId、SideBEntryId、WinnerEntryId、LoserEntryId
- WinnerToMatchId、LoserToMatchId
- IsBye、IsSeedQualifier、IsResetFinal、ResolutionReason
- Created／Updated／Completed 時間及 Version

`TournamentMatchParticipant` 保存：

- TournamentMatchId、TournamentEntryId、UserId
- Participation Status：Pending／Accepted／Declined／Invalidated／NoShow
- IsMatchRepresentative、LineupConfirmed
- Notified／Responded／LineupConfirmed 時間及 Version

主辦方的未到判定將未回覆的敗方 Participant 標記為 NoShow，WinnerEntryId／LoserEntryId 與 `NoShow` 原因保存在 Match。Bye／Walkover 不建立虛構比分 Battle。每個 Tournament 同時只能有一個 active Match；完成、棄權、撤銷及 Revision 推進必須交易化並防止重複晉級。

`Playoff` 是循環／瑞士例行排名完全同分且影響冠軍時，由系統自動附加的單淘汰 bracket；沿用 WinnerToMatchId 與 MatchWinner source，不新增或複製 Entry，也不把加賽結果混入例行賽 tie-break 統計。

公開 Tournament 詳情是唯讀 projection，不建立額外資料表。它只從 Registered Entry、Match、目前有效 Battle 與已物化 BattleLineup 組合 Snapshot；未完成的 BattleLineupSelection／BattleTeamOrderSelection 不屬於公開資料來源。

## 不建立 Statistics Table

玩家、陀螺、對手、來源與 B／X Side 戰績全部從 Battle、BattleLineup、BattleRound、有效 BattleRoundEvent 及 TournamentMatch 聚合。
