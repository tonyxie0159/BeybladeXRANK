using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Realtime;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeybladeRecordSystem.Services;

public class BattleService(AppDbContext db, IRealtimePublisher? realtimePublisher = null)
{
    public async Task<ServiceResult> AssignSidesAsync(int battleId, int operatorUserId, BattleSide sideA)
    {
        var battle = await db.Battles.SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != operatorUserId) return ServiceResult.Failure("只有裁判可指定 B/X Side。");
        if (battle.Status != BattleStatus.LineupLocked) return ServiceResult.Failure("只有陣容鎖定後、開始對戰前可以指定 Side。");

        battle.SideADesignation = sideA;
        battle.Status = BattleStatus.SideSelection;
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<BattleRound>> StartBattleAsync(int battleId, int creatorId)
    {
        var battle = await db.Battles.Include(x => x.Lineups).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult<BattleRound>.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult<BattleRound>.Failure("只有建立者可開始對戰。");
        if (battle.Status != BattleStatus.SideSelection || battle.SideADesignation is null)
            return ServiceResult<BattleRound>.Failure("裁判必須先明確指定 B/X Side 才能開始對戰。");
        var first = battle.Lineups.SingleOrDefault(x => x.IsCurrent && x.PositionNo == 1);
        if (first is null) return ServiceResult<BattleRound>.Failure("找不到已鎖定的陣容。");
        var round = CreateRound(battle, first, 1);
        battle.Status = BattleStatus.InProgress;
        battle.StartedAtUtc = DateTime.UtcNow;
        battle.Version = Guid.NewGuid().ToByteArray();
        db.BattleRounds.Add(round);
        await db.SaveChangesAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult<BattleRound>.Success(round);
    }

    public async Task<ServiceResult> RecordLaunchFaultAsync(int battleId, int roundId, int creatorId, int actorPlayerId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await GetOperationalRoundAsync(battleId, roundId, creatorId);
        if (!result.Succeeded) return ServiceResult.Failure(result.Error!);
        var (battle, round) = result.Value!;
        if (actorPlayerId != round.PlayerAId && actorPlayerId != round.PlayerBId) return ServiceResult.Failure("失誤玩家不屬於目前順位。");

        var sequence = round.Events.Count + 1;
        round.Events.Add(new BattleRoundEvent { EventSequence = sequence, EventType = BattleRoundEventType.LaunchFault, ActorPlayerId = actorPlayerId, ScoreAwarded = 0, IsEffective = true, CreatedAtUtc = DateTime.UtcNow });
        var faults = BattleRules.FaultCount(round.Events, actorPlayerId);
        if (faults == 2)
        {
            round.Events.Add(new BattleRoundEvent
            {
                EventSequence = sequence + 1,
                EventType = BattleRoundEventType.LaunchFaultPenalty,
                ActorPlayerId = actorPlayerId,
                WinnerPlayerId = actorPlayerId == round.PlayerAId ? round.PlayerBId : round.PlayerAId,
                ScoreAwarded = 1,
                IsEffective = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        await RecalculateBattleAsync(battle);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RecordBattleResultAsync(int battleId, int roundId, int creatorId, int winnerPlayerId, ResultType resultType)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await GetOperationalRoundAsync(battleId, roundId, creatorId);
        if (!result.Succeeded) return ServiceResult.Failure(result.Error!);
        var (battle, round) = result.Value!;
        if (winnerPlayerId != round.PlayerAId && winnerPlayerId != round.PlayerBId) return ServiceResult.Failure("勝者不屬於目前順位。");
        if (round.Events.Any(x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult)) return ServiceResult.Failure("此局已記錄勝負結果，請使用判決修改。");

        round.Events.Add(new BattleRoundEvent
        {
            EventSequence = round.Events.Count + 1,
            EventType = BattleRoundEventType.BattleResult,
            WinnerPlayerId = winnerPlayerId,
            ResultType = resultType,
            ScoreAwarded = BattleRules.ScoreFor(resultType),
            IsEffective = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await RecalculateBattleAsync(battle);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<BattleRound?>> CompleteRoundAsync(int battleId, int roundId, int creatorId)
    {
        var result = await GetOperationalRoundAsync(battleId, roundId, creatorId);
        if (!result.Succeeded) return ServiceResult<BattleRound?>.Failure(result.Error!);
        var (battle, round) = result.Value!;
        if (!round.Events.Any(x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult)) return ServiceResult<BattleRound?>.Failure("記錄勝負結果後才能完成此局。");
        round.Status = BattleRoundStatus.Completed;
        round.CompletedAtUtc = DateTime.UtcNow;
        BattleRound? nextRound = null;
        if (battle.Status == BattleStatus.InProgress)
        {
            var nextPosition = round.PositionNo + 1;
            var currentLineup = battle.Lineups.Single(x => x.Id == round.LineupId);
            var nextLineup = battle.Lineups.SingleOrDefault(x => x.SequenceNo == currentLineup.SequenceNo && x.PositionNo == nextPosition);
            if (nextLineup is not null)
            {
                nextRound = CreateRound(battle, nextLineup, battle.Rounds.Max(x => x.RoundNo) + 1);
                db.BattleRounds.Add(nextRound);
            }
            else if (battle.TournamentMatch is not null)
            {
                battle.TournamentMatch.Status = TournamentMatchStatus.ReorderSelection;
                battle.TournamentMatch.UpdatedAtUtc = DateTime.UtcNow;
                battle.TournamentMatch.Version = Guid.NewGuid().ToByteArray();
            }
            else if (battle.SourceType == BattleSourceType.Quick)
            {
                battle.Status = BattleStatus.ReorderSelection;
            }
        }
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult<BattleRound?>.Success(nextRound);
    }

    public async Task<ServiceResult<BattleRound?>> RecordAndCompleteRoundAsync(
        int battleId,
        int roundId,
        int creatorId,
        int winnerPlayerId,
        ResultType resultType)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await GetOperationalRoundAsync(battleId, roundId, creatorId);
        if (!result.Succeeded) return ServiceResult<BattleRound?>.Failure(result.Error!);
        var (battle, round) = result.Value!;
        if (winnerPlayerId != round.PlayerAId && winnerPlayerId != round.PlayerBId)
            return ServiceResult<BattleRound?>.Failure("勝者不屬於目前順位。");
        if (round.Events.Any(x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult))
            return ServiceResult<BattleRound?>.Failure("此局已記錄勝負結果，請使用判決修改。");

        round.Events.Add(new BattleRoundEvent
        {
            EventSequence = round.Events.Count + 1,
            EventType = BattleRoundEventType.BattleResult,
            WinnerPlayerId = winnerPlayerId,
            ResultType = resultType,
            ScoreAwarded = BattleRules.ScoreFor(resultType),
            IsEffective = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await RecalculateBattleAsync(battle);
        round.Status = BattleRoundStatus.Completed;
        round.CompletedAtUtc = DateTime.UtcNow;
        BattleRound? nextRound = null;
        if (battle.Status == BattleStatus.InProgress)
        {
            var currentLineup = battle.Lineups.Single(x => x.Id == round.LineupId);
            var nextLineup = battle.Lineups.SingleOrDefault(x =>
                x.SequenceNo == currentLineup.SequenceNo && x.PositionNo == round.PositionNo + 1);
            if (nextLineup is not null)
            {
                nextRound = CreateRound(battle, nextLineup, battle.Rounds.Max(x => x.RoundNo) + 1);
                db.BattleRounds.Add(nextRound);
            }
            else if (battle.TournamentMatch is not null)
            {
                battle.TournamentMatch.Status = TournamentMatchStatus.ReorderSelection;
                battle.TournamentMatch.UpdatedAtUtc = DateTime.UtcNow;
                battle.TournamentMatch.Version = Guid.NewGuid().ToByteArray();
            }
            else if (battle.SourceType == BattleSourceType.Quick)
            {
                battle.Status = BattleStatus.ReorderSelection;
            }
        }
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult<BattleRound?>.Success(nextRound);
    }

    public async Task<ServiceResult> FinishBattleAsync(int battleId, int creatorId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles.Include(x => x.TournamentMatch).Include(x => x.Rounds).ThenInclude(x => x.Events).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult.Failure("只有建立者可結束對戰。");
        if (battle.Status != BattleStatus.VictoryPendingCompletion) return ServiceResult.Failure("尚未達成勝利條件，不能結束對戰。");
        var sideAWon = battle.SideAScore >= battle.ScoreToWin;
        battle.WinningPlayerId = battle.SourceType == BattleSourceType.TournamentTeam
            ? null
            : sideAWon ? battle.PlayerAId : battle.PlayerBId;
        battle.WinningSide = sideAWon
            ? battle.SideADesignation
            : battle.SideADesignation is null ? null : BattleRules.Opposite(battle.SideADesignation.Value);
        var now = DateTime.UtcNow;
        foreach (var round in battle.Rounds.Where(x => x.Status == BattleRoundStatus.InProgress && x.Events.Any(e => e.IsEffective && e.ScoreAwarded > 0)))
        {
            round.Status = BattleRoundStatus.Completed;
            round.CompletedAtUtc = now;
        }
        battle.Status = BattleStatus.Completed;
        battle.CompletedAtUtc = now;
        battle.Version = Guid.NewGuid().ToByteArray();
        if (battle.TournamentMatch is not null)
        {
            var winnerEntryId = sideAWon ? battle.TournamentMatch.SideAEntryId : battle.TournamentMatch.SideBEntryId;
            var loserEntryId = sideAWon ? battle.TournamentMatch.SideBEntryId : battle.TournamentMatch.SideAEntryId;
            if (winnerEntryId is null || loserEntryId is null) return ServiceResult.Failure("Tournament Match 缺少已解析的參賽單位。");
            await new TournamentProgressionService(db).CompleteMatchAndAdvanceAsync(
                battle.TournamentMatch, winnerEntryId.Value, loserEntryId.Value,
                TournamentMatchStatus.Completed, "BattleCompleted", now);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ForfeitQuickBattleAsync(int battleId, int creatorId, int forfeitingPlayerId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles
            .Include(x => x.Rounds).ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.SourceType != BattleSourceType.Quick)
            return ServiceResult.Failure("Tournament Battle 必須從賽事對局辦理棄權。");
        if (battle.CreatedByUserId != creatorId)
            return ServiceResult.Failure("只有建立者裁判可以判定棄權。");
        if (battle.Status is not (BattleStatus.InProgress or BattleStatus.ReorderSelection))
            return ServiceResult.Failure("只有進行中或等待重排的快速對戰可以判定棄權。");
        if (battle.PlayerAId is null || battle.PlayerBId is null ||
            forfeitingPlayerId != battle.PlayerAId && forfeitingPlayerId != battle.PlayerBId)
            return ServiceResult.Failure("棄權者不屬於這場對戰。");

        var now = DateTime.UtcNow;
        foreach (var round in battle.Rounds.Where(x => x.Status == BattleRoundStatus.InProgress))
            foreach (var roundEvent in round.Events)
            {
                roundEvent.IsEffective = false;
                roundEvent.InvalidationReason = BattleRoundEventInvalidationReason.BattleTerminated;
            }

        (battle.SideAScore, battle.SideBScore) = BattleRules.CalculateScores(battle.Rounds);
        var sideAWon = forfeitingPlayerId == battle.PlayerBId;
        battle.Status = BattleStatus.Forfeited;
        battle.WinningPlayerId = sideAWon ? battle.PlayerAId : battle.PlayerBId;
        battle.WinningSide = sideAWon
            ? battle.SideADesignation
            : battle.SideADesignation is null ? null : BattleRules.Opposite(battle.SideADesignation.Value);
        battle.CompletedAtUtc = now;
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CancelQuickBattleAsync(int battleId, int creatorId, bool confirmed)
    {
        if (!confirmed)
            return ServiceResult.Failure("請先明確確認整場資料將永久刪除且不列入統計。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.SourceType != BattleSourceType.Quick)
            return ServiceResult.Failure("Tournament Battle 必須使用賽事取消或撤銷流程。");
        if (battle.CreatedByUserId != creatorId)
            return ServiceResult.Failure("只有建立者裁判可以取消對戰。");
        if (battle.Status is not (BattleStatus.InProgress or BattleStatus.ReorderSelection or BattleStatus.VictoryPendingCompletion))
            return ServiceResult.Failure("只有尚未正式完成的快速對戰可以取消。");

        var claimedVersion = Guid.NewGuid().ToByteArray();
        var claimed = await db.Battles
            .Where(x => x.Id == battleId && x.Version.SequenceEqual(battle.Version))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, claimedVersion));
        if (claimed != 1)
            return ServiceResult.Failure("對戰資料已被其他操作更新，請重新整理後再取消。");

        await db.BattleRoundRevisions
            .Where(x => x.BattleRound.BattleId == battleId)
            .ExecuteDeleteAsync();
        await db.BattleRoundEvents
            .Where(x => x.BattleRound.BattleId == battleId)
            .ExecuteDeleteAsync();
        await db.BattleRounds.Where(x => x.BattleId == battleId).ExecuteDeleteAsync();
        await db.BattleLineupSelections.Where(x => x.BattleId == battleId).ExecuteDeleteAsync();
        await db.BattleTeamOrderSelections.Where(x => x.BattleId == battleId).ExecuteDeleteAsync();
        await db.BattleLineups.Where(x => x.BattleId == battleId).ExecuteDeleteAsync();
        await db.Battles.Where(x => x.Id == battleId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
        if (realtimePublisher is not null && battle.PlayerAId is int playerAId && battle.PlayerBId is int playerBId)
            await realtimePublisher.PublishUsersAsync([playerAId, playerBId], "battle-state", new { battleId, status = "Cancelled", targetUrl = "/Battles/Invitations" });
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ReviseRoundAsync(
        int battleId,
        int roundId,
        int creatorId,
        int winnerPlayerId,
        ResultType resultType,
        string? reason,
        bool confirmDownstreamReset = false)
    {
        var revisionReason = reason?.Trim() ?? string.Empty;
        if (revisionReason.Length is < 1 or > 500)
            return ServiceResult.Failure("修改原因須為 1 至 500 個字元。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles.AsSplitQuery()
            .Include(x => x.TournamentMatch).ThenInclude(x => x!.Tournament)
            .Include(x => x.Lineups)
            .Include(x => x.Rounds).ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult.Failure("只有建立者可修改判決。");
        if (battle.Status is not (BattleStatus.InProgress or BattleStatus.ReorderSelection or BattleStatus.VictoryPendingCompletion or BattleStatus.Completed))
            return ServiceResult.Failure("目前狀態不能修改判決。");
        if (battle.TournamentMatch is { } regulationMatch && regulationMatch.Bracket != TournamentBracket.Playoff &&
            regulationMatch.Tournament.Format is TournamentFormat.RoundRobin or TournamentFormat.Swiss &&
            await db.TournamentMatches.AnyAsync(x => x.TournamentId == regulationMatch.TournamentId &&
                x.Bracket == TournamentBracket.Playoff))
            return ServiceResult.Failure("冠軍加賽已建立，不能直接修改例行對局判決，以免加賽名單與排名失去一致性。");
        var round = battle.Rounds.SingleOrDefault(x => x.Id == roundId);
        if (round is null) return ServiceResult.Failure("找不到指定的 Round。");
        if (winnerPlayerId != round.PlayerAId && winnerPlayerId != round.PlayerBId) return ServiceResult.Failure("勝者不屬於指定順位。");

        var wasCompleted = battle.Status == BattleStatus.Completed;
        var wasAwaitingReorder = battle.Status == BattleStatus.ReorderSelection;
        var replacement = new BattleRoundEvent
        {
            BattleRoundId = round.Id,
            EventSequence = round.Events.Count + 1,
            EventType = BattleRoundEventType.BattleResult,
            WinnerPlayerId = winnerPlayerId,
            ResultType = resultType,
            ScoreAwarded = BattleRules.ScoreFor(resultType),
            IsEffective = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var replay = BuildRevisionReplay(battle, round, replacement);
        Tournament? tournament = null;
        TournamentMatch? targetMatch = battle.TournamentMatch;
        List<TournamentMatch> downstream = [];
        int? revisedWinnerEntryId = null;
        int? revisedLoserEntryId = null;
        var outcomeChanged = false;
        if (wasCompleted && targetMatch is not null)
        {
            revisedWinnerEntryId = replay.ThresholdReached
                ? replay.SideAWon ? targetMatch.SideAEntryId : targetMatch.SideBEntryId
                : null;
            revisedLoserEntryId = replay.ThresholdReached
                ? replay.SideAWon ? targetMatch.SideBEntryId : targetMatch.SideAEntryId
                : null;
            if (replay.ThresholdReached && (revisedWinnerEntryId is null || revisedLoserEntryId is null))
                return ServiceResult.Failure("Tournament Match 缺少已解析的參賽單位。");
            outcomeChanged = targetMatch.WinnerEntryId != revisedWinnerEntryId || targetMatch.LoserEntryId != revisedLoserEntryId;
            if (outcomeChanged)
            {
                tournament = await db.Tournaments.AsSplitQuery()
                    .Include(x => x.Entries).ThenInclude(x => x.Members)
                    .Include(x => x.Matches).ThenInclude(x => x.Participants)
                    .Include(x => x.Matches).ThenInclude(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
                    .SingleAsync(x => x.Id == targetMatch.TournamentId);
                targetMatch = tournament.Matches.Single(x => x.Id == targetMatch.Id);
                battle = targetMatch.Battle!;
                round = battle.Rounds.Single(x => x.Id == roundId);
                if (tournament.Format == TournamentFormat.Swiss &&
                    tournament.Matches.Any(x => x.Bracket == targetMatch.Bracket && x.RoundNumber > targetMatch.RoundNumber))
                    return ServiceResult.Failure("瑞士輪後續配對已建立；請先由最末輪逆序撤銷，才能變更此場勝方。");
                downstream = FindDirectDownstream(tournament, targetMatch.Id);
                var blocked = downstream.FirstOrDefault(x => TournamentMatchService.IsStartedOrResolvedDownstream(x.Status));
                if (blocked is not null)
                    return ServiceResult.Failure($"下游對局 #{blocked.SequenceNumber} 已開始或已有正式結果；必須先由下游逆序撤銷。");
                if (!confirmDownstreamReset && downstream.Any(TournamentMatchService.HasPreparedLineup))
                    return ServiceResult.Failure("勝方變更會撤銷下游既有 Lineup；請勾選確認後再繼續。");
            }
        }

        var previous = round.Events.Where(x => x.IsEffective).OrderBy(x => x.EventSequence).Select(EventSnapshot.From).ToList();
        var previousBattleSnapshot = SerializeBattleSnapshot(battle, targetMatch);
        foreach (var laterRound in battle.Rounds.Where(x => x.RoundNo > round.RoundNo))
        {
            foreach (var laterEvent in laterRound.Events.Where(x =>
                         x.IsEffective || x.InvalidationReason == BattleRoundEventInvalidationReason.VictoryThresholdReached))
            {
                laterEvent.IsEffective = false;
                laterEvent.InvalidationReason = BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision;
            }
            laterRound.Status = BattleRoundStatus.Completed;
            laterRound.CompletedAtUtc ??= DateTime.UtcNow;
        }
        foreach (var eventToReplace in replay.SupersededEventIds.Select(id => battle.Rounds.SelectMany(x => x.Events).Single(e => e.Id == id)))
        {
            eventToReplace.IsEffective = false;
            eventToReplace.InvalidationReason = BattleRoundEventInvalidationReason.SupersededByRevision;
        }
        foreach (var candidate in battle.Rounds.SelectMany(x => x.Events).Where(x => replay.CandidateEventIds.Contains(x.Id)))
        {
            candidate.IsEffective = replay.EffectiveEventIds.Contains(candidate.Id);
            candidate.InvalidationReason = candidate.IsEffective ? null : BattleRoundEventInvalidationReason.VictoryThresholdReached;
        }
        replacement.IsEffective = replay.ReplacementEffective;
        replacement.InvalidationReason = replacement.IsEffective ? null : BattleRoundEventInvalidationReason.VictoryThresholdReached;
        round.Events.Add(replacement);
        battle.SideAScore = replay.SideAScore;
        battle.SideBScore = replay.SideBScore;
        var now = DateTime.UtcNow;
        ApplyRevisedOutcome(battle, targetMatch, wasCompleted, replay, revisedWinnerEntryId, revisedLoserEntryId, now);
        round.Status = BattleRoundStatus.Completed;
        round.CompletedAtUtc ??= now;
        if (!replay.ThresholdReached)
            PrepareContinuationAfterRevision(battle, targetMatch, round, now);

        if (outcomeChanged && tournament is not null && targetMatch is not null)
        {
            foreach (var dependent in downstream)
                ResetDownstreamAfterRevision(
                    dependent, targetMatch.Id, revisedWinnerEntryId, revisedLoserEntryId,
                    creatorId, battle.Id, now);
            ApplyResetFinalRule(tournament, targetMatch, downstream, revisedWinnerEntryId, revisedLoserEntryId, now);
            tournament.Status = TournamentStatus.InProgress;
            tournament.CompletedAtUtc = null;
            tournament.UpdatedAtUtc = now;
            tournament.Version = Guid.NewGuid().ToByteArray();
        }

        var revised = round.Events.Where(x => x.IsEffective).OrderBy(x => x.EventSequence).Select(EventSnapshot.From).ToList();
        db.BattleRoundRevisions.Add(new BattleRoundRevision
        {
            BattleRoundId = round.Id,
            ChangedByUserId = creatorId,
            ChangedAtUtc = now,
            Reason = revisionReason,
            PreviousEffectiveEventSnapshot = JsonSerializer.Serialize(previous),
            NewEffectiveEventSnapshot = JsonSerializer.Serialize(revised),
            PreviousBattleSnapshot = previousBattleSnapshot,
            NewBattleSnapshot = SerializeBattleSnapshot(battle, targetMatch)
        });
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        if (outcomeChanged && tournament is not null && replay.ThresholdReached)
        {
            ActivateNextReadyMatch(tournament, now);
            SetTournamentCompletionState(tournament, now);
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<Battle>> GetBattleAsync(int battleId, int userId)
    {
        var battle = await db.Battles.Include(x => x.PlayerA).Include(x => x.PlayerB).Include(x => x.Lineups).Include(x => x.Rounds).ThenInclude(x => x.Events)
            .Include(x => x.Rounds).ThenInclude(x => x.Revisions).ThenInclude(x => x.ChangedByUser)
            .Include(x => x.TournamentMatch).ThenInclude(x => x!.SideAEntry)
            .Include(x => x.TournamentMatch).ThenInclude(x => x!.SideBEntry)
            .Include(x => x.VoidedTournamentMatch).ThenInclude(x => x!.SideAEntry)
            .Include(x => x.VoidedTournamentMatch).ThenInclude(x => x!.SideBEntry)
            .Include(x => x.VoidedTournamentMatch).ThenInclude(x => x!.Participants)
            .Include(x => x.VoidedByUser)
            .SingleOrDefaultAsync(x => x.Id == battleId && (x.CreatedByUserId == userId || x.PlayerAId == userId || x.PlayerBId == userId ||
                (x.TournamentMatch != null && x.TournamentMatch.Participants.Any(p => p.UserId == userId)) ||
                (x.VoidedTournamentMatch != null && x.VoidedTournamentMatch.Participants.Any(p => p.UserId == userId)) ||
                (x.Status == BattleStatus.Voided && x.LineupSelections.Any(s => s.UserId == userId))));
        return battle is null ? ServiceResult<Battle>.Failure("找不到對戰。") : ServiceResult<Battle>.Success(battle);
    }

    private static RevisionReplay BuildRevisionReplay(
        Battle battle,
        BattleRound revisedRound,
        BattleRoundEvent replacement)
    {
        var superseded = revisedRound.Events
            .Where(x => x.EventType == BattleRoundEventType.BattleResult &&
                (x.IsEffective || x.InvalidationReason == BattleRoundEventInvalidationReason.VictoryThresholdReached))
            .Select(x => x.Id)
            .ToHashSet();
        var candidates = battle.Rounds.Where(round => round.RoundNo <= revisedRound.RoundNo)
            .SelectMany(round => round.Events
                .Where(x => !superseded.Contains(x.Id) &&
                    (x.IsEffective || x.InvalidationReason == BattleRoundEventInvalidationReason.VictoryThresholdReached))
                .Select(x => new ReplayCandidate(round, x, false)))
            .Append(new ReplayCandidate(revisedRound, replacement, true))
            .OrderBy(x => x.Round.RoundNo)
            .ThenBy(x => x.Event.EventSequence)
            .ToList();

        var candidateIds = candidates.Where(x => !x.IsReplacement).Select(x => x.Event.Id).ToHashSet();
        var effectiveIds = new HashSet<int>();
        var replacementEffective = false;
        var sideAScore = 0;
        var sideBScore = 0;
        var thresholdReached = false;
        var sideAWon = false;
        foreach (var candidate in candidates)
        {
            if (thresholdReached) continue;
            if (candidate.IsReplacement) replacementEffective = true;
            else effectiveIds.Add(candidate.Event.Id);
            if (candidate.Event.WinnerPlayerId == candidate.Round.PlayerAId)
                sideAScore += candidate.Event.ScoreAwarded;
            else if (candidate.Event.WinnerPlayerId == candidate.Round.PlayerBId)
                sideBScore += candidate.Event.ScoreAwarded;
            if (sideAScore >= battle.ScoreToWin || sideBScore >= battle.ScoreToWin)
            {
                thresholdReached = true;
                sideAWon = sideAScore >= battle.ScoreToWin;
            }
        }

        return new RevisionReplay(
            superseded, candidateIds, effectiveIds, replacementEffective,
            sideAScore, sideBScore, thresholdReached, sideAWon);
    }

    private void ApplyRevisedOutcome(
        Battle battle,
        TournamentMatch? match,
        bool wasCompleted,
        RevisionReplay replay,
        int? revisedWinnerEntryId,
        int? revisedLoserEntryId,
        DateTime now)
    {
        if (replay.ThresholdReached)
        {
            if (wasCompleted)
            {
                battle.Status = BattleStatus.Completed;
                battle.CompletedAtUtc ??= now;
                battle.WinningPlayerId = battle.SourceType == BattleSourceType.TournamentTeam
                    ? null
                    : replay.SideAWon ? battle.PlayerAId : battle.PlayerBId;
                battle.WinningSide = replay.SideAWon
                    ? battle.SideADesignation
                    : battle.SideADesignation is null ? null : BattleRules.Opposite(battle.SideADesignation.Value);
                if (match is not null)
                {
                    match.WinnerEntryId = revisedWinnerEntryId;
                    match.LoserEntryId = revisedLoserEntryId;
                    match.Status = TournamentMatchStatus.Completed;
                    match.ResolutionReason = "BattleRevised";
                    match.CompletedAtUtc ??= now;
                }
            }
            else
            {
                battle.Status = BattleStatus.VictoryPendingCompletion;
                battle.CompletedAtUtc = null;
                battle.WinningPlayerId = null;
                battle.WinningSide = null;
                if (match is not null)
                    match.Status = TournamentMatchStatus.VictoryPendingCompletion;
            }
        }
        else
        {
            battle.Status = BattleStatus.InProgress;
            battle.CompletedAtUtc = null;
            battle.WinningPlayerId = null;
            battle.WinningSide = null;
            if (match is not null)
            {
                match.WinnerEntryId = null;
                match.LoserEntryId = null;
                match.CompletedAtUtc = null;
                match.ResolutionReason = "BattleRevisionReopened";
            }
            if (match is not null)
                match.Status = TournamentMatchStatus.InProgress;
        }

        if (match is not null)
        {
            match.UpdatedAtUtc = now;
            match.Version = Guid.NewGuid().ToByteArray();
        }
    }

    private void PrepareContinuationAfterRevision(Battle battle, TournamentMatch? match, BattleRound revisedRound, DateTime now)
    {
        var revisedLineup = battle.Lineups.Single(x => x.Id == revisedRound.LineupId);
        var nextLineup = battle.Lineups
            .Where(x => x.SequenceNo == revisedLineup.SequenceNo && x.PositionNo == revisedRound.PositionNo + 1)
            .OrderBy(x => x.PositionNo)
            .FirstOrDefault();
        if (nextLineup is not null)
        {
            var reusableRound = battle.Rounds
                .Where(x => x.RoundNo > revisedRound.RoundNo && x.LineupId == nextLineup.Id && x.Events.Count == 0)
                .OrderBy(x => x.RoundNo)
                .FirstOrDefault();
            if (reusableRound is not null)
            {
                reusableRound.Status = BattleRoundStatus.InProgress;
                reusableRound.CompletedAtUtc = null;
            }
            else
            {
                db.BattleRounds.Add(CreateRound(battle, nextLineup, battle.Rounds.Max(x => x.RoundNo) + 1));
            }
            if (match is not null) match.Status = TournamentMatchStatus.InProgress;
        }
        else if (match is not null)
        {
            match.Status = TournamentMatchStatus.ReorderSelection;
        }
        else if (battle.SourceType == BattleSourceType.Quick)
        {
            battle.Status = BattleStatus.ReorderSelection;
        }
    }

    private static List<TournamentMatch> FindDirectDownstream(Tournament tournament, int sourceMatchId) =>
        tournament.Matches.Where(x => x.Id != sourceMatchId &&
            ((x.SideASourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                x.SideASourceReferenceId == sourceMatchId) ||
             (x.SideBSourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                x.SideBSourceReferenceId == sourceMatchId)))
            .ToList();

    private void ResetDownstreamAfterRevision(
        TournamentMatch dependent,
        int sourceMatchId,
        int? winnerEntryId,
        int? loserEntryId,
        int organizerUserId,
        int revisedBattleId,
        DateTime now)
    {
        if (dependent.Battle is { } dependentBattle)
            TournamentMatchService.VoidBattle(
                dependentBattle, dependent, organizerUserId,
                $"上游 Battle #{revisedBattleId} 修正後參賽者變更", now);
        db.TournamentMatchParticipants.RemoveRange(dependent.Participants);
        if (dependent.SideASourceReferenceId == sourceMatchId)
            dependent.SideAEntryId = ResolveRevisedSource(dependent.SideASourceKind, dependent.SideAEntryId, winnerEntryId, loserEntryId);
        if (dependent.SideBSourceReferenceId == sourceMatchId)
            dependent.SideBEntryId = ResolveRevisedSource(dependent.SideBSourceKind, dependent.SideBEntryId, winnerEntryId, loserEntryId);
        dependent.WinnerEntryId = null;
        dependent.LoserEntryId = null;
        dependent.Status = TournamentMatchStatus.WaitingForParticipants;
        dependent.ResolutionReason = $"UpstreamBattleRevised:{revisedBattleId}";
        dependent.CompletedAtUtc = null;
        dependent.UpdatedAtUtc = now;
        dependent.Version = Guid.NewGuid().ToByteArray();
    }

    private static int? ResolveRevisedSource(
        TournamentParticipantSourceKind? kind,
        int? current,
        int? winnerEntryId,
        int? loserEntryId) => kind switch
    {
        TournamentParticipantSourceKind.MatchWinner => winnerEntryId,
        TournamentParticipantSourceKind.MatchLoser => loserEntryId,
        _ => current
    };

    private static void ApplyResetFinalRule(
        Tournament tournament,
        TournamentMatch revisedMatch,
        IReadOnlyList<TournamentMatch> downstream,
        int? winnerEntryId,
        int? loserEntryId,
        DateTime now)
    {
        if (!revisedMatch.IsResetFinal && revisedMatch.Bracket == TournamentBracket.GrandFinal &&
            winnerEntryId is not null && loserEntryId is not null)
        {
            var resetFinal = downstream.SingleOrDefault(x => x.IsResetFinal);
            if (resetFinal is null) return;
            int? undefeatedEntryId = null;
            if (revisedMatch.SideASourceKind == TournamentParticipantSourceKind.MatchWinner &&
                tournament.Matches.Single(x => x.Id == revisedMatch.SideASourceReferenceId).Bracket == TournamentBracket.Winners)
                undefeatedEntryId = revisedMatch.SideAEntryId;
            else if (revisedMatch.SideBSourceKind == TournamentParticipantSourceKind.MatchWinner &&
                tournament.Matches.Single(x => x.Id == revisedMatch.SideBSourceReferenceId).Bracket == TournamentBracket.Winners)
                undefeatedEntryId = revisedMatch.SideBEntryId;
            if (winnerEntryId == undefeatedEntryId)
            {
                resetFinal.Status = TournamentMatchStatus.NotRequired;
                resetFinal.WinnerEntryId = winnerEntryId;
                resetFinal.LoserEntryId = loserEntryId;
                resetFinal.ResolutionReason = "ResetFinalNotRequired";
                resetFinal.CompletedAtUtc = now;
            }
        }
    }

    private static void ActivateNextReadyMatch(Tournament tournament, DateTime now)
    {
        if (tournament.Matches.Any(x => IsActiveMatchStatus(x.Status))) return;
        var next = tournament.Matches
            .Where(x => !x.IsBye && x.Status == TournamentMatchStatus.WaitingForParticipants &&
                x.SideAEntryId is not null && x.SideBEntryId is not null)
            .OrderBy(x => x.SequenceNumber)
            .FirstOrDefault();
        if (next is null) return;
        next.Status = TournamentMatchStatus.AwaitingParticipationConfirmation;
        next.UpdatedAtUtc = now;
        next.Version = Guid.NewGuid().ToByteArray();
        foreach (var entryId in new[] { next.SideAEntryId!.Value, next.SideBEntryId!.Value })
        {
            var entry = tournament.Entries.Single(x => x.Id == entryId);
            if (tournament.Mode == TournamentMode.Individual)
                next.Participants.Add(CreateRevisionParticipant(next, entry, entry.IndividualUserId!.Value, false, now));
            else
                foreach (var member in entry.Members.OrderBy(x => x.MemberOrder))
                    next.Participants.Add(CreateRevisionParticipant(next, entry, member.UserId, member.IsRepresentative, now));
        }
    }

    private static TournamentMatchParticipant CreateRevisionParticipant(
        TournamentMatch match,
        TournamentEntry entry,
        int userId,
        bool isRepresentative,
        DateTime now) => new()
    {
        TournamentMatch = match,
        TournamentEntryId = entry.Id,
        UserId = userId,
        IsMatchRepresentative = isRepresentative,
        Status = TournamentParticipationStatus.Pending,
        NotifiedAtUtc = now,
        Version = Guid.NewGuid().ToByteArray()
    };

    private static bool IsActiveMatchStatus(TournamentMatchStatus status) => status is
        TournamentMatchStatus.AwaitingParticipationConfirmation or TournamentMatchStatus.ReadyForLineup or
        TournamentMatchStatus.LineupSelection or TournamentMatchStatus.TeamOrderSelection or
        TournamentMatchStatus.LineupReview or TournamentMatchStatus.LineupLocked or
        TournamentMatchStatus.SideSelection or TournamentMatchStatus.InProgress or
        TournamentMatchStatus.ReorderSelection or TournamentMatchStatus.VictoryPendingCompletion;

    private static void SetTournamentCompletionState(Tournament tournament, DateTime now)
    {
        var complete = tournament.Matches.All(x => x.Status is
            TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or
            TournamentMatchStatus.Forfeited or TournamentMatchStatus.NotRequired);
        tournament.Status = complete ? TournamentStatus.Completed : TournamentStatus.InProgress;
        tournament.CompletedAtUtc = complete ? tournament.CompletedAtUtc ?? now : null;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
    }

    private static string SerializeBattleSnapshot(Battle battle, TournamentMatch? match) => JsonSerializer.Serialize(new
    {
        battle.Id,
        battle.Status,
        battle.SideAScore,
        battle.SideBScore,
        battle.WinningSide,
        battle.WinningPlayerId,
        MatchId = match?.Id,
        MatchStatus = match?.Status,
        WinnerEntryId = match?.WinnerEntryId,
        LoserEntryId = match?.LoserEntryId,
        Events = battle.Rounds.OrderBy(x => x.RoundNo).SelectMany(round =>
            round.Events.OrderBy(x => x.EventSequence).Select(roundEvent => new
            {
                round.RoundNo,
                roundEvent.EventSequence,
                roundEvent.EventType,
                roundEvent.WinnerPlayerId,
                roundEvent.ScoreAwarded,
                roundEvent.IsEffective,
                roundEvent.InvalidationReason
            }))
    });

    private static BattleRound CreateRound(Battle battle, BattleLineup lineup, int roundNo) => new()
    {
        BattleId = battle.Id, LineupId = lineup.Id, RoundNo = roundNo, PositionNo = lineup.PositionNo,
        PlayerAId = lineup.PlayerAId, PlayerADisplayNameSnapshot = lineup.PlayerADisplayNameSnapshot,
        PlayerABeybladeId = lineup.PlayerABeybladeId, PlayerABeybladeNameSnapshot = lineup.PlayerABeybladeNameSnapshot,
        PlayerBId = lineup.PlayerBId, PlayerBDisplayNameSnapshot = lineup.PlayerBDisplayNameSnapshot,
        PlayerBBeybladeId = lineup.PlayerBBeybladeId, PlayerBBeybladeNameSnapshot = lineup.PlayerBBeybladeNameSnapshot,
        CreatedAtUtc = DateTime.UtcNow
    };

    private async Task<ServiceResult<(Battle Battle, BattleRound Round)>> GetOperationalRoundAsync(int battleId, int roundId, int creatorId)
    {
        var battle = await db.Battles.Include(x => x.TournamentMatch).Include(x => x.Lineups).Include(x => x.Rounds).ThenInclude(x => x.Events).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult<(Battle, BattleRound)>.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult<(Battle, BattleRound)>.Failure("只有建立者可記錄對戰事件。");
        if (battle.Status != BattleStatus.InProgress) return ServiceResult<(Battle, BattleRound)>.Failure("目前對戰狀態不能新增事件。");
        var round = battle.Rounds.SingleOrDefault(x => x.Id == roundId);
        if (round is null || round.Status == BattleRoundStatus.Completed) return ServiceResult<(Battle, BattleRound)>.Failure("找不到可操作的 BattleRound。");
        return ServiceResult<(Battle, BattleRound)>.Success((battle, round));
    }

    private async Task RecalculateBattleAsync(Battle battle)
    {
        (battle.SideAScore, battle.SideBScore) = BattleRules.CalculateScores(battle.Rounds);
        battle.Status = BattleRules.StatusForScore(battle.SideAScore, battle.SideBScore, battle.ScoreToWin);
        if (battle.TournamentMatch is not null)
        {
            battle.TournamentMatch.Status = battle.Status == BattleStatus.VictoryPendingCompletion
                ? TournamentMatchStatus.VictoryPendingCompletion
                : TournamentMatchStatus.InProgress;
            battle.TournamentMatch.UpdatedAtUtc = DateTime.UtcNow;
            battle.TournamentMatch.Version = Guid.NewGuid().ToByteArray();
        }
        battle.Version = Guid.NewGuid().ToByteArray();
        await Task.CompletedTask;
    }

    private async Task PublishBattleStateAsync(Battle battle)
    {
        if (realtimePublisher is null) return;
        var userIds = new HashSet<int> { battle.CreatedByUserId };
        if (battle.PlayerAId is int playerAId) userIds.Add(playerAId);
        if (battle.PlayerBId is int playerBId) userIds.Add(playerBId);
        if (battle.TournamentMatchId is int matchId)
        {
            foreach (var participantId in await db.TournamentMatchParticipants.AsNoTracking()
                         .Where(x => x.TournamentMatchId == matchId)
                         .Select(x => x.UserId)
                         .ToListAsync())
                userIds.Add(participantId);
        }

        var targetUrl = GetRealtimeTargetUrl(battle);
        await realtimePublisher.PublishUsersAsync(userIds, "battle-state", new
        {
            battleId = battle.Id,
            tournamentMatchId = battle.TournamentMatchId,
            status = battle.Status.ToString(),
            targetUrl
        });
    }

    private static string GetRealtimeTargetUrl(Battle battle)
    {
        if (battle.TournamentMatchId is int tournamentMatchId && battle.Status is
            BattleStatus.LineupSelection or BattleStatus.LineupReview or BattleStatus.LineupLocked or
            BattleStatus.SideSelection or BattleStatus.ReorderSelection)
            return $"/Tournaments/Match/{tournamentMatchId}";

        return battle.Status switch
        {
            BattleStatus.LineupSelection or BattleStatus.LineupReview or BattleStatus.LineupLocked or BattleStatus.SideSelection
                => $"/Battles/Setup/{battle.Id}",
            BattleStatus.ReorderSelection => $"/Battles/Reorder/{battle.Id}",
            BattleStatus.Completed or BattleStatus.Forfeited => $"/Battles/Details/{battle.Id}",
            _ => $"/Battles/Battle/{battle.Id}"
        };
    }

    private sealed record ReplayCandidate(BattleRound Round, BattleRoundEvent Event, bool IsReplacement);

    private sealed record RevisionReplay(
        IReadOnlySet<int> SupersededEventIds,
        IReadOnlySet<int> CandidateEventIds,
        IReadOnlySet<int> EffectiveEventIds,
        bool ReplacementEffective,
        int SideAScore,
        int SideBScore,
        bool ThresholdReached,
        bool SideAWon);

    private sealed record EventSnapshot(
        int EventSequence,
        BattleRoundEventType EventType,
        int? ActorPlayerId,
        int? WinnerPlayerId,
        ResultType? ResultType,
        int ScoreAwarded,
        BattleRoundEventInvalidationReason? InvalidationReason)
    {
        public static EventSnapshot From(BattleRoundEvent battleEvent) => new(
            battleEvent.EventSequence,
            battleEvent.EventType,
            battleEvent.ActorPlayerId,
            battleEvent.WinnerPlayerId,
            battleEvent.ResultType,
            battleEvent.ScoreAwarded,
            battleEvent.InvalidationReason);
    }
}
