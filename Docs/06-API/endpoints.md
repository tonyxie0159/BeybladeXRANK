# HTTP／Application Service Contract

本專案使用 Razor Pages，不建立公開 REST API。PageModel 只能呼叫本文件列出的有效 Application Service 契約；方法名稱以目前 C# Async 命名為準。

「待補」表示有效產品需求但目前實作尚未完成，不代表可改回舊流程。實作狀態以 08-Development/development-plan.md 為準。

## AuthService

- RegisterAsync(account, password, displayName)
- LoginAsync(account, password)
- ChangeDisplayNameAsync(userId, displayName)

Login 回傳 User 給 PageModel 建立 authentication cookie；Service 不保存明文密碼。

## BeybladeService

- GetMyBeybladesAsync(userId)
- CreateAsync(userId, name)
- RenameAsync(userId, beybladeId, name)
- DeleteAsync(userId, beybladeId)

所有 mutation 驗證 Beyblade.UserId；Delete 是軟刪除。

## QuickBattleFlowService

- GetInvitationsAsync(userId)
- GetIncomingInvitationCountAsync(userId)
- SendInvitationAsync(inviterUserId, inviteeUserId)
- AcceptInvitationAsync(invitationId, inviteeUserId)
- DeclineInvitationAsync(invitationId, inviteeUserId)
- WithdrawInvitationAsync(invitationId, inviterUserId)
- GetWorkspaceAsync(battleId, userId)
- SubmitLineupAsync(battleId, userId, orderedBladeIds)
- ConfirmLineupAsync(battleId, userId)
- RequestLineupEditAsync(battleId, userId)
- RespondLineupEditAsync(battleId, userId, accept)
- GetReorderWorkspaceAsync(battleId, userId)
- SubmitReorderAsync(battleId, userId, orderedBladeIds)
- GetActiveBattlesAsync(userId) — 回傳登入者可恢復的 Quick Battle 與 Setup／Reorder／Battle 目的頁狀態。

接受邀請前不得建立 Battle。Lineup／Reorder 只能提交目前登入者自己的 Beyblade，公開前的 workspace 不得包含對手私密 submission。

## BattleService

- AssignSidesAsync(battleId, operatorUserId, sideA)
- StartBattleAsync(battleId, operatorUserId)
- RecordLaunchFaultAsync(battleId, roundId, operatorUserId, actorPlayerId)
- RecordAndCompleteRoundAsync(battleId, roundId, operatorUserId, winnerPlayerId, resultType)
- FinishBattleAsync(battleId, operatorUserId)
- ForfeitQuickBattleAsync(battleId, creatorId, forfeitingPlayerId)
- CancelQuickBattleAsync(battleId, creatorId, confirmed)
- ReviseRoundAsync(battleId, roundId, operatorUserId, winnerPlayerId, resultType, reason, confirmDownstreamReset)
- GetBattleAsync(battleId, userId)

CreateDraftAsync、SetLineupAsync、LockLineupAsync、CreateReorderedLineupAsync 舊契約已移除，不得重新加入或由新 PageModel 模擬其雙邊代填行為。

BattleService 不接受任意 Score、Side 分數、ScoreToWin、WinningSide、Tournament WinnerEntry 或下一場 Match。

## TournamentService

### 建立與讀取

- CreateAsync(organizerUserId, request)
- GetListAsync(userId, filter, pageNumber) — WaitingForMe 包含邀請、待完成隊伍、主辦方賽前操作及登入者實際尚未完成的 Match 動作；Match 待辦優先，再依 UpdatedAtUtc 排序。列表回傳 Mode、RegistrationMode、Format、RuleSet、RulesSnapshot、Notes、身份與操作目的地。
- GetDetailsAsync(tournamentId) — 供登入者自身報名／組隊／主辦管理流程使用，不作為觀眾輸出契約。
- GetTeamWorkspaceAsync(tournamentId, userId)
- GetSystemPairingEntriesForOrganizerAsync(tournamentId, organizerUserId)
- GetPublicDetailsAsync(tournamentId, userId) — 回傳 Registered Entry、完整賽程、目前 Match、Winner／Loser、合法比分、Side、已物化公開 Lineup／實際玩家，以及取消前完成資料；同時只回傳登入者是否可開啟 private workspace。

列表只回傳登入者本人的待辦與身份，不公開 Account、私密 submission、其他人的 pending invitation 或未完成臨時隊伍。

### 即時同步與唯讀備援 handlers

- `/hubs/realtime` — 使用 Cookie 驗證，只將連線加入該登入 UserId 的私人群組。Server 在交易提交後發布通知或流程狀態已變更事件。

- `Tournaments/Index?handler=Poll` — 回傳目前分頁與篩選的變更 token。
- `Tournaments/Details/{id}?handler=Poll` — 回傳公開 read model 變更 token 與 Tournament status。
- `Tournaments/Match/{id}?handler=Poll` — 維持 workspace authorization，只回傳 Match／Battle 變更 token 與 status。

上述 handler 只接受 GET、回傳最小 JSON並使用 no-store；供 SignalR 斷線、重連、頁面回到前景及低頻備援同步使用，不重送 POST 或自動執行狀態轉換。

### 通知與玩家搜尋頁面

- `Notifications?handler=Unread` — 只回傳登入者未讀摘要。
- `Notifications` 的安全 POST handlers — 接受通知列舉動作、單筆已讀及全部已讀；每次重新驗證登入者、關聯實體、狀態與 anti-forgery token。
- `Players/Search?q=` — 以 DisplayName 模糊搜尋，只回傳最多十筆 UserId 與 DisplayName，不回傳 Account。

### 報名與邀請

- RegisterIndividualAsync(tournamentId, userId)
- WithdrawAsync(tournamentId, userId)
- CloseRegistrationAsync(tournamentId, organizerUserId)
- InviteParticipantAsync(tournamentId, organizerUserId, invitedUserId)
- GetPendingParticipantInvitationAsync(tournamentId, userId)
- RespondToTournamentInvitationAsync(invitationId, invitedUserId, accept)
- ReopenRegistrationAsync(tournamentId, organizerUserId) — 個人、整隊與系統配隊共用；必要時清除未開始賽程草稿與 SchedulePosition。

### 整隊報名

- CreateTemporaryTeamAsync(tournamentId, representativeUserId, teamName)
- InviteTeamMemberAsync(tournamentId, entryId, representativeUserId, invitedUserId)
- RespondToTeamInvitationAsync(invitationId, invitedUserId, accept)
- RegisterCompleteTeamAsync(tournamentId, entryId, representativeUserId)
- TransferRepresentativeAsync(tournamentId, entryId, currentRepresentativeId, newRepresentativeId)
- RespondToRepresentativeTransferAsync(invitationId, invitedUserId, accept)
- LeaveTeamAsync(tournamentId, userId)

### 系統配隊

- RegisterForSystemPairingAsync(tournamentId, userId)
- WithdrawFromSystemPairingAsync(tournamentId, userId)
- GenerateSystemAssignedTeamsAsync(tournamentId, organizerUserId)
- SwapSystemAssignedMembersAsync(tournamentId, organizerUserId, firstMemberId, secondMemberId)

系統配隊的重新開放也使用通用 ReopenRegistrationAsync，不再保留平行的專用契約。

### 賽程與生命週期

- GenerateScheduleDraftAsync(tournamentId, organizerUserId, randomSeed?)
- AbandonScheduleDraftAsync(tournamentId, organizerUserId)
- ReorderScheduleEntriesAsync(tournamentId, organizerUserId, orderedEntryIds)
- StartTournamentAsync(tournamentId, organizerUserId)
- CancelTournamentAsync(tournamentId, organizerUserId, reason)

Tournament 建立及賽程服務必須從 Server catalog 推導 RuleSet、限制、ScoreToWin、勝敗來源與 bracket topology。

## TournamentMatchService

- GetWorkspaceAsync(matchId, userId)
- GetActionableAsync(tournamentId, userId)
- GetActionableForUserAsync(userId, tournamentId?)
- RespondParticipationAsync(matchId, userId, accept)
- SubmitLineupAsync(matchId, userId, bladeIds)
- SubmitIndividualLineupAsync(matchId, userId, bladeIds)
- AssignMatchRepresentativeAsync(matchId, userId, newRepresentativeUserId)
- SubmitTeamOrderAsync(matchId, representativeUserId, orderedUserIds)
- ConfirmLineupAsync(matchId, userId)
- AssignSidesAndStartAsync(matchId, organizerUserId, sideA)
- SubmitReorderAsync(matchId, userId, orderedBladeIds)
- SubmitTeamReorderOrderAsync(matchId, representativeUserId, orderedUserIds)
- ForfeitAsync(matchId, userId, reason)
- VoidAndReopenAsync(matchId, organizerUserId, reason, confirmDownstreamReset)
- DeclareNoShowAsync(matchId, organizerUserId, absentEntryId, reason, confirmed) — 只接受 AwaitingParticipationConfirmation、仍有 Pending 必要選手且屬於本場的 Entry；二次確認後形成 Walkover、推進一次且不建立 Battle。

Match workspace 只提供主辦方與該 Match 參賽者的私密資料。觀眾使用 PublicDetails read model，不以繞過 workspace authorization 的方式觀賽。

## TournamentProgressionService

- CompleteMatchAndAdvanceAsync(completedMatch, winnerEntryId, loserEntryId, terminalStatus, resolutionReason, now)

此 Service 只由已授權且已驗證的 Battle／Match 完成流程呼叫。Client 不可直接指定 winner、loser、加賽名單或下一場。循環／瑞士規定輪次全部完成後，Service 以排名服務判斷冠軍完全同分，必要時自動建立 `Playoff` bracket；無需加賽或加賽完成後才將 Tournament 設為 Completed。Playoff 建立後，例行 Match 的 Revision／Void 會被拒絕，避免改變加賽候選基礎。

## TournamentStandingsService

- GetStandingsAsync(tournamentId)
- GetRequiredChampionPlayoffEntryIds(tournament)

`GetStandingsAsync` 統一處理四種賽制：循環／瑞士依固定 tie-break；單淘汰在完成後依決賽結果與淘汰輪次；雙敗在完成後依決勝 Grand Final 與第二敗所在階段產生正式名次。循環／瑞士的 Playoff 只覆寫冠軍，不加入原勝場、Buchholz、得失分或其他 tie-break；其餘完全同分 Entry 保持並列。EntryId 只供同名次顯示順序可重現。

## StatisticsService

- GetUserSummaryAsync(userId)
- GetUserStatisticsSectionsAsync(userId, side)
- GetUserStatisticsRowsAsync(userId, sort, source, side)
- GetBeybladeSourceSamplesAsync(userId)
- GetBeybladeSideSamplesAsync(userId, source)
- GetBeybladeStatisticsAsync(userId, sort, source, side)
- GetOpponentStatisticsAsync(userId, sort)
- GetOpponentBeybladeStatisticsAsync(userId, opponentId, sort)
- GetBattleHistoryAsync(userId, source)

來源與 Side 篩選後必須在 Server 重新聚合，不得只隱藏前端列。Voided、未完成 Round 與取消規則排除的 Event 不得進入查詢。

## 共通驗證

所有 mutation Service 必須驗證：

- 呼叫者是否有該資源與操作權限。
- aggregate 狀態是否允許操作。
- User、Beyblade、Entry、Match、Battle、Round 的關聯是否一致。
- Client 提供的 Id 是否屬於正確 Tournament／Battle／Player。
- concurrency conflict 是否回傳可刷新重試的結果。
- transaction 失敗時是否完整回滾。

Query Service 若由 PageModel 傳入 userId，PageModel 必須使用目前登入者 Id；不可直接綁定 Client 提交的資料所有者。
