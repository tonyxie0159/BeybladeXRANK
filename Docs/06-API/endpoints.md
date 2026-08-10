# HTTP / Service Contract

本專案採 Razor Pages，不要求建立公開 REST API 專案。

以下列出 Application Service 操作契約，供 PageModel 使用。

## AuthService

- Register(account, password, displayName)
- Login(account, password)
- ChangeDisplayName(userId, displayName)

## BeybladeService

- GetMyBeyblades(userId)
- Create(userId, name)
- Rename(userId, beybladeId, name)
- Delete(userId, beybladeId)

## BattleService

- CreateDraft(creatorId, opponentId)
- SetLineup(battleId, playerASelection, playerBSelection)
- LockLineup(battleId)
- StartBattle(battleId)
- RecordLaunchFault(battleId, roundId, playerId)
- RecordBattleResult(battleId, roundId, winnerPlayerId, resultType)
- CompleteRound(battleId, roundId)
- CreateReorderedLineup(battleId, orderedBladeIdsA, orderedBladeIdsB)
- GetBattleState(battleId)
- GetBattleHistory(battleId)
- ReviseRound(battleId, roundId, revisedResult, reason)
- FinishBattle(battleId)

## StatisticsService

- GetUserSummary(userId)
- GetBeybladeStatistics(userId, sort)
- GetOpponentStatistics(userId)
- GetOpponentBeybladeStatistics(userId, opponentId)
- GetBattleHistory(userId)

## 重要

所有 Service 方法都必須驗證：

- 使用者是否有權限。
- Battle 是否屬於目前使用者可操作範圍。
- Battle 狀態是否允許該操作。
- Entity 是否屬於指定使用者。
- Round 是否屬於 Battle。
- Beyblade 是否屬於正確 Player。

不要信任 hidden field、query string 或 client score。

