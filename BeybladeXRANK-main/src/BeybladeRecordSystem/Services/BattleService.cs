using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeybladeRecordSystem.Services;

public class BattleService(AppDbContext db)
{
    public async Task<ServiceResult<Battle>> CreateDraftAsync(int creatorId, int opponentId)
    {
        if (creatorId == opponentId) return ServiceResult<Battle>.Failure("不可與自己對戰。");
        if (!await db.Users.AnyAsync(x => x.Id == creatorId) || !await db.Users.AnyAsync(x => x.Id == opponentId))
            return ServiceResult<Battle>.Failure("找不到指定的玩家。");

        var battle = new Battle
        {
            SourceType = BattleSourceType.Quick,
            ScoreToWin = 4,
            PlayerAId = creatorId,
            PlayerBId = opponentId,
            SideADesignation = BattleSide.B,
            CreatedByUserId = creatorId,
            Status = BattleStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid().ToByteArray()
        };
        db.Battles.Add(battle);
        await db.SaveChangesAsync();
        return ServiceResult<Battle>.Success(battle);
    }

    public async Task<ServiceResult> SetLineupAsync(int battleId, int creatorId, IReadOnlyList<int> playerASelection, IReadOnlyList<int> playerBSelection)
    {
        var battle = await db.Battles.Include(x => x.PlayerA).Include(x => x.PlayerB).Include(x => x.Lineups).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult.Failure("只有建立者可設定陣容。");
        if (battle.Status != BattleStatus.Draft) return ServiceResult.Failure("陣容已鎖定，不能更換陀螺。");
        if (!IsValidSelection(playerASelection) || !IsValidSelection(playerBSelection)) return ServiceResult.Failure("雙方必須各選三顆不同的陀螺。");

        var selectedIds = playerASelection.Concat(playerBSelection).ToArray();
        var blades = await db.Beyblades.Where(x => selectedIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id);
        if (blades.Count != 6 || playerASelection.Any(x => !blades.TryGetValue(x, out var blade) || blade.UserId != battle.PlayerAId) || playerBSelection.Any(x => !blades.TryGetValue(x, out var blade) || blade.UserId != battle.PlayerBId))
            return ServiceResult.Failure("所選陀螺必須屬於正確玩家且尚未刪除。");

        db.BattleLineups.RemoveRange(battle.Lineups);
        for (var index = 0; index < 3; index++)
        {
            var a = blades[playerASelection[index]];
            var b = blades[playerBSelection[index]];
            db.BattleLineups.Add(new BattleLineup
            {
                BattleId = battle.Id,
                SequenceNo = 1,
                PositionNo = index + 1,
                PlayerAId = battle.PlayerAId,
                PlayerADisplayNameSnapshot = battle.PlayerA!.DisplayName,
                PlayerABeybladeId = a.Id,
                PlayerABeybladeNameSnapshot = a.Name,
                PlayerBId = battle.PlayerBId,
                PlayerBDisplayNameSnapshot = battle.PlayerB!.DisplayName,
                PlayerBBeybladeId = b.Id,
                PlayerBBeybladeNameSnapshot = b.Name,
                IsCurrent = true
            });
        }
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> LockLineupAsync(int battleId, int creatorId)
    {
        var battle = await db.Battles.Include(x => x.Lineups).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult.Failure("只有建立者可鎖定陣容。");
        if (battle.Status != BattleStatus.Draft) return ServiceResult.Failure("目前狀態不能鎖定陣容。");
        var lineup = battle.Lineups.Where(x => x.SequenceNo == 1).OrderBy(x => x.PositionNo).ToList();
        if (lineup.Count != 3 || lineup.Select(x => x.PlayerABeybladeId).Distinct().Count() != 3 || lineup.Select(x => x.PlayerBBeybladeId).Distinct().Count() != 3)
            return ServiceResult.Failure("雙方各需三顆不同的陀螺才能鎖定。");
        battle.Status = BattleStatus.LineupLocked;
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> AssignSidesAsync(int battleId, int operatorUserId, BattleSide sideA)
    {
        var battle = await db.Battles.SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != operatorUserId) return ServiceResult.Failure("只有裁判可指定 B/X Side。");
        if (battle.Status != BattleStatus.LineupLocked) return ServiceResult.Failure("只有陣容鎖定後、開始對戰前可以指定 Side。");

        battle.SideADesignation = sideA;
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<BattleRound>> StartBattleAsync(int battleId, int creatorId)
    {
        var battle = await db.Battles.Include(x => x.Lineups).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult<BattleRound>.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult<BattleRound>.Failure("只有建立者可開始對戰。");
        if (battle.Status != BattleStatus.LineupLocked) return ServiceResult<BattleRound>.Failure("目前狀態不能開始對戰。");
        var first = battle.Lineups.SingleOrDefault(x => x.IsCurrent && x.PositionNo == 1);
        if (first is null) return ServiceResult<BattleRound>.Failure("找不到已鎖定的陣容。");
        var round = CreateRound(battle, first, 1);
        battle.Status = BattleStatus.InProgress;
        battle.StartedAtUtc = DateTime.UtcNow;
        battle.Version = Guid.NewGuid().ToByteArray();
        db.BattleRounds.Add(round);
        await db.SaveChangesAsync();
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
        }
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
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
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<BattleRound>> CreateReorderedLineupAsync(int battleId, int creatorId, IReadOnlyList<int> orderedBladeIdsA, IReadOnlyList<int> orderedBladeIdsB)
    {
        var battle = await db.Battles.Include(x => x.Lineups).Include(x => x.Rounds).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult<BattleRound>.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult<BattleRound>.Failure("只有建立者可重新排列陣容。");
        if (battle.Status != BattleStatus.InProgress) return ServiceResult<BattleRound>.Failure("目前狀態不能重新排列。");
        if (!IsValidSelection(orderedBladeIdsA) || !IsValidSelection(orderedBladeIdsB)) return ServiceResult<BattleRound>.Failure("雙方必須各排列三顆不同的陀螺。");

        var initial = battle.Lineups.Where(x => x.SequenceNo == 1).ToList();
        if (!orderedBladeIdsA.Order().SequenceEqual(initial.Select(x => x.PlayerABeybladeId).Order()) || !orderedBladeIdsB.Order().SequenceEqual(initial.Select(x => x.PlayerBBeybladeId).Order()))
            return ServiceResult<BattleRound>.Failure("重排只能使用最初鎖定的三顆陀螺。");
        var currentSequence = battle.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().SingleOrDefault();
        var currentLineupIds = battle.Lineups.Where(x => x.SequenceNo == currentSequence).Select(x => x.Id).ToHashSet();
        var currentRounds = battle.Rounds.Where(x => currentLineupIds.Contains(x.LineupId)).ToList();
        if (currentRounds.Count != 3 || currentRounds.Any(x => x.Status != BattleRoundStatus.Completed))
            return ServiceResult<BattleRound>.Failure("目前三個順位的 Round 都完成後才能重排。");

        foreach (var item in battle.Lineups.Where(x => x.IsCurrent)) item.IsCurrent = false;
        var sequenceNo = currentSequence + 1;
        var aSnapshots = initial.ToDictionary(x => x.PlayerABeybladeId, x => x.PlayerABeybladeNameSnapshot);
        var bSnapshots = initial.ToDictionary(x => x.PlayerBBeybladeId, x => x.PlayerBBeybladeNameSnapshot);
        var newLineup = new List<BattleLineup>();
        for (var index = 0; index < 3; index++)
        {
            newLineup.Add(new BattleLineup
            {
                BattleId = battle.Id, SequenceNo = sequenceNo, PositionNo = index + 1, IsCurrent = true,
                PlayerAId = initial.Single(x => x.PlayerABeybladeId == orderedBladeIdsA[index]).PlayerAId,
                PlayerADisplayNameSnapshot = initial.Single(x => x.PlayerABeybladeId == orderedBladeIdsA[index]).PlayerADisplayNameSnapshot,
                PlayerABeybladeId = orderedBladeIdsA[index], PlayerABeybladeNameSnapshot = aSnapshots[orderedBladeIdsA[index]],
                PlayerBId = initial.Single(x => x.PlayerBBeybladeId == orderedBladeIdsB[index]).PlayerBId,
                PlayerBDisplayNameSnapshot = initial.Single(x => x.PlayerBBeybladeId == orderedBladeIdsB[index]).PlayerBDisplayNameSnapshot,
                PlayerBBeybladeId = orderedBladeIdsB[index], PlayerBBeybladeNameSnapshot = bSnapshots[orderedBladeIdsB[index]]
            });
        }
        db.BattleLineups.AddRange(newLineup);
        await db.SaveChangesAsync();
        var firstRound = CreateRound(battle, newLineup[0], battle.Rounds.Max(x => x.RoundNo) + 1);
        db.BattleRounds.Add(firstRound);
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult<BattleRound>.Success(firstRound);
    }

    public async Task<ServiceResult> ReviseRoundAsync(int battleId, int roundId, int creatorId, int winnerPlayerId, ResultType resultType, string? reason)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles.Include(x => x.TournamentMatch).Include(x => x.Rounds).ThenInclude(x => x.Events).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到對戰。");
        if (battle.CreatedByUserId != creatorId) return ServiceResult.Failure("只有建立者可修改判決。");
        if (battle.Status is not (BattleStatus.InProgress or BattleStatus.VictoryPendingCompletion)) return ServiceResult.Failure("目前狀態不能修改判決。");
        var round = battle.Rounds.SingleOrDefault(x => x.Id == roundId);
        if (round is null) return ServiceResult.Failure("找不到指定的 Round。");
        if (winnerPlayerId != round.PlayerAId && winnerPlayerId != round.PlayerBId) return ServiceResult.Failure("勝者不屬於指定順位。");

        var previous = round.Events.Where(x => x.IsEffective).OrderBy(x => x.EventSequence).Select(EventSnapshot.From).ToList();
        foreach (var eventToReplace in round.Events.Where(x => x.IsEffective && x.EventType == BattleRoundEventType.BattleResult)) eventToReplace.IsEffective = false;
        round.Events.Add(new BattleRoundEvent
        {
            EventSequence = round.Events.Count + 1, EventType = BattleRoundEventType.BattleResult,
            WinnerPlayerId = winnerPlayerId, ResultType = resultType, ScoreAwarded = BattleRules.ScoreFor(resultType),
            IsEffective = true, CreatedAtUtc = DateTime.UtcNow
        });
        var revised = round.Events.Where(x => x.IsEffective).OrderBy(x => x.EventSequence).Select(EventSnapshot.From).ToList();
        db.BattleRoundRevisions.Add(new BattleRoundRevision
        {
            BattleRoundId = round.Id, ChangedByUserId = creatorId, ChangedAtUtc = DateTime.UtcNow, Reason = reason?.Trim(),
            PreviousEffectiveEventSnapshot = JsonSerializer.Serialize(previous), NewEffectiveEventSnapshot = JsonSerializer.Serialize(revised)
        });
        await RecalculateBattleAsync(battle);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<Battle>> GetBattleAsync(int battleId, int userId)
    {
        var battle = await db.Battles.Include(x => x.PlayerA).Include(x => x.PlayerB).Include(x => x.Lineups).Include(x => x.Rounds).ThenInclude(x => x.Events)
            .Include(x => x.TournamentMatch).ThenInclude(x => x!.SideAEntry)
            .Include(x => x.TournamentMatch).ThenInclude(x => x!.SideBEntry)
            .SingleOrDefaultAsync(x => x.Id == battleId && (x.CreatedByUserId == userId || x.PlayerAId == userId || x.PlayerBId == userId ||
                (x.TournamentMatch != null && x.TournamentMatch.Participants.Any(p => p.UserId == userId))));
        return battle is null ? ServiceResult<Battle>.Failure("找不到對戰。") : ServiceResult<Battle>.Success(battle);
    }

    private static bool IsValidSelection(IReadOnlyList<int> selection) => selection.Count == 3 && selection.Distinct().Count() == 3 && selection.All(x => x > 0);

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
        battle.SideAScore = battle.Rounds.Sum(round => round.Events
            .Where(x => x.IsEffective && x.WinnerPlayerId == round.PlayerAId)
            .Sum(x => x.ScoreAwarded));
        battle.SideBScore = battle.Rounds.Sum(round => round.Events
            .Where(x => x.IsEffective && x.WinnerPlayerId == round.PlayerBId)
            .Sum(x => x.ScoreAwarded));
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

    private sealed record EventSnapshot(int EventSequence, BattleRoundEventType EventType, int? ActorPlayerId, int? WinnerPlayerId, ResultType? ResultType, int ScoreAwarded)
    {
        public static EventSnapshot From(BattleRoundEvent battleEvent) => new(battleEvent.EventSequence, battleEvent.EventType, battleEvent.ActorPlayerId, battleEvent.WinnerPlayerId, battleEvent.ResultType, battleEvent.ScoreAwarded);
    }
}
