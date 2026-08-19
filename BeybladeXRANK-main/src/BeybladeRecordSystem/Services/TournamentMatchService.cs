using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public record TournamentMatchWorkspace(
    TournamentMatch Match,
    TournamentMatchParticipant? CurrentParticipant,
    IReadOnlyList<BattleLineupSelection> VisibleSelections,
    IReadOnlyList<BattleTeamOrderSelection> VisibleTeamOrder,
    IReadOnlyList<BattleLineupSelection> CurrentPrivateSelections,
    IReadOnlyList<BattleTeamOrderSelection> CurrentPrivateTeamOrder,
    IReadOnlyList<Beyblade> AvailableBeyblades,
    bool IsOrganizer);

public record TournamentMatchAction(int MatchId, int SequenceNumber, TournamentMatchStatus Status, bool IsOrganizer);

public class TournamentMatchService(AppDbContext db)
{
    public async Task<TournamentMatchWorkspace?> GetWorkspaceAsync(int matchId, int userId)
    {
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return null;
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId);
        var isOrganizer = match.Tournament.OrganizerUserId == userId;
        if (!isOrganizer && participant is null) return null;

        var selections = match.Battle?.LineupSelections ?? [];
        var currentSequence = match.Battle?.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().SingleOrDefault() ?? 0;
        var pendingSequence = currentSequence + 1;
        var visible = match.Status >= TournamentMatchStatus.LineupReview
            ? selections.Where(x => match.Status != TournamentMatchStatus.ReorderSelection || x.SequenceNo <= currentSequence).OrderBy(x => x.SequenceNo).ThenBy(x => x.UserId).ThenBy(x => x.PositionNo).ToList()
            : selections.Where(x => x.UserId == userId).OrderBy(x => x.PositionNo).ToList();
        var teamOrder = match.Battle?.TeamOrderSelections ?? [];
        var visibleTeamOrder = match.Status >= TournamentMatchStatus.LineupReview
            ? teamOrder.Where(x => match.Status != TournamentMatchStatus.ReorderSelection || x.SequenceNo <= currentSequence).OrderBy(x => x.SequenceNo).ThenBy(x => x.TournamentEntryId).ThenBy(x => x.PositionNo).ToList()
            : participant is null ? [] : teamOrder.Where(x => x.TournamentEntryId == participant.TournamentEntryId).OrderBy(x => x.PositionNo).ToList();
        var privateSelections = match.Status == TournamentMatchStatus.ReorderSelection
            ? selections.Where(x => x.SequenceNo == pendingSequence && x.UserId == userId).OrderBy(x => x.PositionNo).ToList()
            : [];
        var privateTeamOrder = match.Status == TournamentMatchStatus.ReorderSelection && participant is not null
            ? teamOrder.Where(x => x.SequenceNo == pendingSequence && x.TournamentEntryId == participant.TournamentEntryId).OrderBy(x => x.PositionNo).ToList()
            : [];
        var blades = participant is null
            ? []
            : await db.Beyblades.Where(x => x.UserId == userId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        return new TournamentMatchWorkspace(match, participant, visible, visibleTeamOrder, privateSelections, privateTeamOrder, blades, isOrganizer);
    }

    public async Task<IReadOnlyList<TournamentMatchAction>> GetActionableAsync(int tournamentId, int userId)
    {
        return await db.TournamentMatches
            .Where(x => x.TournamentId == tournamentId &&
                (x.Tournament.OrganizerUserId == userId || x.Participants.Any(p => p.UserId == userId)) &&
                x.Status >= TournamentMatchStatus.AwaitingParticipationConfirmation &&
                x.Status < TournamentMatchStatus.Completed)
            .OrderBy(x => x.SequenceNumber)
            .Select(x => new TournamentMatchAction(x.Id, x.SequenceNumber, x.Status, x.Tournament.OrganizerUserId == userId))
            .ToListAsync();
    }

    public async Task<ServiceResult> RespondParticipationAsync(int matchId, int userId, bool accept)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await db.TournamentMatches.Include(x => x.Tournament).Include(x => x.Participants)
            .Include(x => x.SideAEntry).Include(x => x.SideBEntry).Include(x => x.Battle)
            .SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId);
        if (participant is null) return ServiceResult.Failure("你不是這場對局的參賽者。");
        if (participant.Status == (accept ? TournamentParticipationStatus.Accepted : TournamentParticipationStatus.Declined))
            return ServiceResult.Success();
        if (match.Status != TournamentMatchStatus.AwaitingParticipationConfirmation || participant.Status != TournamentParticipationStatus.Pending)
            return ServiceResult.Failure("目前不能變更出賽回覆。");

        var now = DateTime.UtcNow;
        participant.Status = accept ? TournamentParticipationStatus.Accepted : TournamentParticipationStatus.Declined;
        participant.RespondedAtUtc = now;
        participant.Version = Guid.NewGuid().ToByteArray();
        match.UpdatedAtUtc = now;
        match.Version = Guid.NewGuid().ToByteArray();
        if (!accept)
        {
            var declinedEntryId = participant.TournamentEntryId;
            match.WinnerEntryId = match.SideAEntryId == declinedEntryId ? match.SideBEntryId : match.SideAEntryId;
            match.LoserEntryId = declinedEntryId;
            match.Status = TournamentMatchStatus.Walkover;
            match.ResolutionReason = "ParticipationDeclined";
            match.CompletedAtUtc = now;
            foreach (var pending in match.Participants.Where(x => x.Status == TournamentParticipationStatus.Pending))
            {
                pending.Status = TournamentParticipationStatus.Invalidated;
                pending.Version = Guid.NewGuid().ToByteArray();
            }
            if (match.WinnerEntryId is null || match.LoserEntryId is null)
                return ServiceResult.Failure("對局缺少已解析的參賽單位。");
            await new TournamentProgressionService(db).CompleteMatchAndAdvanceAsync(
                match, match.WinnerEntryId.Value, match.LoserEntryId.Value,
                TournamentMatchStatus.Walkover, "ParticipationDeclined", now);
        }
        else if (match.Participants.All(x => x.Status == TournamentParticipationStatus.Accepted))
        {
            if (match.Battle is null)
            {
                match.Battle = new Battle
                {
                    SourceType = match.Tournament.Mode == TournamentMode.Individual
                        ? BattleSourceType.TournamentIndividual
                        : BattleSourceType.TournamentTeam,
                    ScoreToWin = match.Tournament.ScoreToWin,
                    PlayerAId = match.Tournament.Mode == TournamentMode.Individual ? match.SideAEntry!.IndividualUserId : null,
                    PlayerBId = match.Tournament.Mode == TournamentMode.Individual ? match.SideBEntry!.IndividualUserId : null,
                    CreatedByUserId = match.Tournament.OrganizerUserId,
                    Status = BattleStatus.Draft,
                    CreatedAtUtc = now,
                    Version = Guid.NewGuid().ToByteArray()
                };
            }
            match.Status = TournamentMatchStatus.LineupSelection;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SubmitIndividualLineupAsync(int matchId, int userId, IReadOnlyList<int> bladeIds)
        => await SubmitLineupAsync(matchId, userId, bladeIds);

    public async Task<ServiceResult> SubmitLineupAsync(int matchId, int userId, IReadOnlyList<int> bladeIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var expectedCount = match.Tournament.BeybladesPerPlayer;
        if (bladeIds.Count != expectedCount || bladeIds.Any(x => x <= 0) || bladeIds.Distinct().Count() != expectedCount)
            return ServiceResult.Failure($"必須依序選擇 {expectedCount} 顆不同的陀螺。");
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId);
        if (participant?.Status != TournamentParticipationStatus.Accepted)
            return ServiceResult.Failure("你目前不能提交這場對局的陣容。");
        if (match.Status != TournamentMatchStatus.LineupSelection || match.Battle is null)
            return ServiceResult.Failure("目前不是陣容提交階段。");
        var existing = match.Battle.LineupSelections.Where(x => x.SequenceNo == 1 && x.UserId == userId).OrderBy(x => x.PositionNo).ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.BeybladeId).SequenceEqual(bladeIds) ? ServiceResult.Success() : ServiceResult.Failure("陣容已提交，不能再次更換。");

        var blades = await db.Beyblades.Where(x => bladeIds.Contains(x.Id) && x.UserId == userId && !x.IsDeleted).ToDictionaryAsync(x => x.Id);
        if (blades.Count != expectedCount) return ServiceResult.Failure("所選陀螺必須屬於你且尚未刪除。");
        var userName = await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < bladeIds.Count; i++)
        {
            var blade = blades[bladeIds[i]];
            match.Battle.LineupSelections.Add(new BattleLineupSelection
            {
                SequenceNo = 1, UserId = userId, PositionNo = i + 1, BeybladeId = blade.Id,
                PlayerDisplayNameSnapshot = userName, BeybladeNameSnapshot = blade.Name, SubmittedAtUtc = now
            });
        }
        await db.SaveChangesAsync();

        var requiredUsers = match.Participants.Where(x => x.Status == TournamentParticipationStatus.Accepted).Select(x => x.UserId).ToArray();
        var expectedUsers = match.Tournament.Mode == TournamentMode.Individual ? 2 : match.Tournament.TeamSize!.Value * 2;
        var complete = requiredUsers.Length == expectedUsers && requiredUsers.All(id => match.Battle.LineupSelections.Count(x => x.SequenceNo == 1 && x.UserId == id) == expectedCount);
        if (complete)
        {
            if (match.Tournament.Mode == TournamentMode.Individual)
            {
                MaterializeIndividualLineup(match, 1);
                match.Status = TournamentMatchStatus.LineupReview;
            }
            else
            {
                match.Status = TournamentMatchStatus.TeamOrderSelection;
            }
            match.UpdatedAtUtc = now;
            match.Version = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> AssignMatchRepresentativeAsync(int matchId, int userId, int newRepresentativeUserId)
    {
        var match = await db.TournamentMatches.Include(x => x.Tournament).Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        if (match.Tournament.Mode != TournamentMode.Team || match.Status is not (TournamentMatchStatus.LineupSelection or TournamentMatchStatus.TeamOrderSelection))
            return ServiceResult.Failure("目前不能更換本場代表人。");
        var current = match.Participants.SingleOrDefault(x => x.UserId == userId && x.IsMatchRepresentative);
        if (current is null) return ServiceResult.Failure("只有本場代表人可以轉交代表權。");
        var replacement = match.Participants.SingleOrDefault(x => x.UserId == newRepresentativeUserId && x.TournamentEntryId == current.TournamentEntryId && x.Status == TournamentParticipationStatus.Accepted);
        if (replacement is null) return ServiceResult.Failure("新代表人必須是同隊且已確認出賽的隊員。");
        if (current.UserId == replacement.UserId) return ServiceResult.Success();
        current.IsMatchRepresentative = false;
        replacement.IsMatchRepresentative = true;
        current.Version = Guid.NewGuid().ToByteArray();
        replacement.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SubmitTeamOrderAsync(int matchId, int userId, IReadOnlyList<int> orderedUserIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var teamSize = match.Tournament.TeamSize;
        var representative = match.Participants.SingleOrDefault(x => x.UserId == userId && x.IsMatchRepresentative && x.Status == TournamentParticipationStatus.Accepted);
        if (match.Tournament.Mode != TournamentMode.Team || teamSize is null || representative is null)
            return ServiceResult.Failure("只有本場代表人可以提交隊員順序。");
        if (match.Status != TournamentMatchStatus.TeamOrderSelection || match.Battle is null)
            return ServiceResult.Failure("全體隊員完成選螺後才能提交隊員順序。");
        var validUsers = match.Participants.Where(x => x.TournamentEntryId == representative.TournamentEntryId && x.Status == TournamentParticipationStatus.Accepted).Select(x => x.UserId).Order().ToArray();
        if (orderedUserIds.Count != teamSize || orderedUserIds.Distinct().Count() != teamSize || !orderedUserIds.Order().SequenceEqual(validUsers))
            return ServiceResult.Failure("必須排列本隊所有已確認出賽的隊員。");
        var existing = match.Battle.TeamOrderSelections.Where(x => x.SequenceNo == 1 && x.TournamentEntryId == representative.TournamentEntryId).OrderBy(x => x.PositionNo).ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.UserId).SequenceEqual(orderedUserIds) ? ServiceResult.Success() : ServiceResult.Failure("本隊出戰順序已提交，不能再次更換。");
        var now = DateTime.UtcNow;
        for (var i = 0; i < orderedUserIds.Count; i++)
            match.Battle.TeamOrderSelections.Add(new BattleTeamOrderSelection
            {
                SequenceNo = 1,
                TournamentEntryId = representative.TournamentEntryId,
                UserId = orderedUserIds[i], PositionNo = i + 1,
                SubmittedByUserId = userId, SubmittedAtUtc = now
            });
        await db.SaveChangesAsync();
        var complete = new[] { match.SideAEntryId!.Value, match.SideBEntryId!.Value }
            .All(entryId => match.Battle.TeamOrderSelections.Count(x => x.SequenceNo == 1 && x.TournamentEntryId == entryId) == teamSize);
        if (complete)
        {
            MaterializeTeamLineup(match, 1);
            match.Status = TournamentMatchStatus.LineupReview;
            match.UpdatedAtUtc = now;
            match.Version = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SubmitReorderAsync(int matchId, int userId, IReadOnlyList<int> orderedBladeIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId && x.Status == TournamentParticipationStatus.Accepted);
        if (participant is null || match.Status != TournamentMatchStatus.ReorderSelection || match.Battle is null)
            return ServiceResult.Failure("目前不能提交重排。");
        var expectedCount = match.Tournament.BeybladesPerPlayer;
        var original = match.Battle.LineupSelections.Where(x => x.SequenceNo == 1 && x.UserId == userId).ToList();
        if (orderedBladeIds.Count != expectedCount || orderedBladeIds.Distinct().Count() != expectedCount ||
            !orderedBladeIds.Order().SequenceEqual(original.Select(x => x.BeybladeId).Order()))
            return ServiceResult.Failure("重排只能使用本場最初鎖定、且屬於自己的陀螺。");
        var sequenceNo = match.Battle.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().Single() + 1;
        var existing = match.Battle.LineupSelections.Where(x => x.SequenceNo == sequenceNo && x.UserId == userId).OrderBy(x => x.PositionNo).ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.BeybladeId).SequenceEqual(orderedBladeIds) ? ServiceResult.Success() : ServiceResult.Failure("這一組的陀螺順序已提交。");
        var now = DateTime.UtcNow;
        for (var i = 0; i < orderedBladeIds.Count; i++)
        {
            var snapshot = original.Single(x => x.BeybladeId == orderedBladeIds[i]);
            match.Battle.LineupSelections.Add(new BattleLineupSelection
            {
                SequenceNo = sequenceNo, UserId = userId, PositionNo = i + 1,
                BeybladeId = snapshot.BeybladeId,
                PlayerDisplayNameSnapshot = snapshot.PlayerDisplayNameSnapshot,
                BeybladeNameSnapshot = snapshot.BeybladeNameSnapshot,
                SubmittedAtUtc = now
            });
        }
        await db.SaveChangesAsync();
        await TryCompleteReorderAsync(match, sequenceNo, now);
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SubmitTeamReorderOrderAsync(int matchId, int userId, IReadOnlyList<int> orderedUserIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var representative = match.Participants.SingleOrDefault(x => x.UserId == userId && x.IsMatchRepresentative && x.Status == TournamentParticipationStatus.Accepted);
        if (match.Tournament.Mode != TournamentMode.Team || representative is null || match.Status != TournamentMatchStatus.ReorderSelection || match.Battle is null)
            return ServiceResult.Failure("只有本場代表人可以提交重排後的隊員順序。");
        var teamSize = match.Tournament.TeamSize!.Value;
        var validUsers = match.Participants.Where(x => x.TournamentEntryId == representative.TournamentEntryId && x.Status == TournamentParticipationStatus.Accepted).Select(x => x.UserId).Order().ToArray();
        if (orderedUserIds.Count != teamSize || orderedUserIds.Distinct().Count() != teamSize || !orderedUserIds.Order().SequenceEqual(validUsers))
            return ServiceResult.Failure("必須排列本隊所有出戰隊員。");
        var sequenceNo = match.Battle.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().Single() + 1;
        var existing = match.Battle.TeamOrderSelections.Where(x => x.SequenceNo == sequenceNo && x.TournamentEntryId == representative.TournamentEntryId).OrderBy(x => x.PositionNo).ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.UserId).SequenceEqual(orderedUserIds) ? ServiceResult.Success() : ServiceResult.Failure("這一組的隊員順序已提交。");
        var now = DateTime.UtcNow;
        for (var i = 0; i < orderedUserIds.Count; i++)
            match.Battle.TeamOrderSelections.Add(new BattleTeamOrderSelection
            {
                SequenceNo = sequenceNo, TournamentEntryId = representative.TournamentEntryId,
                UserId = orderedUserIds[i], PositionNo = i + 1,
                SubmittedByUserId = userId, SubmittedAtUtc = now
            });
        await db.SaveChangesAsync();
        await TryCompleteReorderAsync(match, sequenceNo, now);
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ConfirmLineupAsync(int matchId, int userId)
    {
        var match = await db.TournamentMatches.Include(x => x.Participants).Include(x => x.Battle).SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId && x.Status == TournamentParticipationStatus.Accepted);
        if (participant is null) return ServiceResult.Failure("你不是這場對局的有效參賽者。");
        if (participant.LineupConfirmed) return ServiceResult.Success();
        if (match.Status != TournamentMatchStatus.LineupReview || match.Battle is null) return ServiceResult.Failure("目前不能確認陣容。");
        var now = DateTime.UtcNow;
        participant.LineupConfirmed = true;
        participant.LineupConfirmedAtUtc = now;
        participant.Version = Guid.NewGuid().ToByteArray();
        if (match.Participants.Where(x => x.Status == TournamentParticipationStatus.Accepted).All(x => x.LineupConfirmed))
        {
            match.Status = TournamentMatchStatus.LineupLocked;
            match.Battle.Status = BattleStatus.LineupLocked;
            match.UpdatedAtUtc = now;
            match.Version = Guid.NewGuid().ToByteArray();
            match.Battle.Version = Guid.NewGuid().ToByteArray();
        }
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<int>> AssignSidesAndStartAsync(int matchId, int organizerUserId, BattleSide sideA)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await db.TournamentMatches.Include(x => x.Tournament).Include(x => x.Battle).SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult<int>.Failure("找不到對局。");
        if (match.Tournament.OrganizerUserId != organizerUserId) return ServiceResult<int>.Failure("只有主辦方裁判可以開始對戰。");
        if (match.Status == TournamentMatchStatus.InProgress && match.Battle is not null) return ServiceResult<int>.Success(match.Battle.Id);
        if (match.Status != TournamentMatchStatus.LineupLocked || match.Battle is null) return ServiceResult<int>.Failure("雙方確認陣容後才能開始對戰。");
        var battleService = new BattleService(db);
        var assigned = await battleService.AssignSidesAsync(match.Battle.Id, organizerUserId, sideA);
        if (!assigned.Succeeded) return ServiceResult<int>.Failure(assigned.Error!);
        var started = await battleService.StartBattleAsync(match.Battle.Id, organizerUserId);
        if (!started.Succeeded) return ServiceResult<int>.Failure(started.Error!);
        match.Status = TournamentMatchStatus.InProgress;
        match.UpdatedAtUtc = DateTime.UtcNow;
        match.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult<int>.Success(match.Battle.Id);
    }

    private IQueryable<TournamentMatch> MatchQuery() => db.TournamentMatches
        .AsSplitQuery()
        .Include(x => x.Tournament)
        .Include(x => x.SideAEntry).ThenInclude(x => x!.IndividualUser)
        .Include(x => x.SideAEntry).ThenInclude(x => x!.Members)
        .Include(x => x.SideBEntry).ThenInclude(x => x!.IndividualUser)
        .Include(x => x.SideBEntry).ThenInclude(x => x!.Members)
        .Include(x => x.Participants).ThenInclude(x => x.User)
        .Include(x => x.Battle).ThenInclude(x => x!.LineupSelections)
        .Include(x => x.Battle).ThenInclude(x => x!.TeamOrderSelections)
        .Include(x => x.Battle).ThenInclude(x => x!.Lineups)
        .Include(x => x.Battle).ThenInclude(x => x!.Rounds);

    private async Task TryCompleteReorderAsync(TournamentMatch match, int sequenceNo, DateTime now)
    {
        var battle = match.Battle!;
        var participants = match.Participants.Where(x => x.Status == TournamentParticipationStatus.Accepted).ToList();
        var allBladesSubmitted = participants.All(p => battle.LineupSelections.Count(x => x.SequenceNo == sequenceNo && x.UserId == p.UserId) == match.Tournament.BeybladesPerPlayer);
        var allOrdersSubmitted = match.Tournament.Mode == TournamentMode.Individual ||
            new[] { match.SideAEntryId!.Value, match.SideBEntryId!.Value }.All(entryId =>
                battle.TeamOrderSelections.Count(x => x.SequenceNo == sequenceNo && x.TournamentEntryId == entryId) == match.Tournament.TeamSize);
        if (!allBladesSubmitted || !allOrdersSubmitted) return;

        foreach (var current in battle.Lineups.Where(x => x.IsCurrent)) current.IsCurrent = false;
        if (match.Tournament.Mode == TournamentMode.Individual) MaterializeIndividualLineup(match, sequenceNo);
        else MaterializeTeamLineup(match, sequenceNo);
        await db.SaveChangesAsync();
        var first = battle.Lineups.Single(x => x.SequenceNo == sequenceNo && x.PositionNo == 1);
        db.BattleRounds.Add(new BattleRound
        {
            BattleId = battle.Id, LineupId = first.Id,
            RoundNo = battle.Rounds.Count == 0 ? 1 : battle.Rounds.Max(x => x.RoundNo) + 1,
            PositionNo = first.PositionNo,
            PlayerAId = first.PlayerAId, PlayerADisplayNameSnapshot = first.PlayerADisplayNameSnapshot,
            PlayerABeybladeId = first.PlayerABeybladeId, PlayerABeybladeNameSnapshot = first.PlayerABeybladeNameSnapshot,
            PlayerBId = first.PlayerBId, PlayerBDisplayNameSnapshot = first.PlayerBDisplayNameSnapshot,
            PlayerBBeybladeId = first.PlayerBBeybladeId, PlayerBBeybladeNameSnapshot = first.PlayerBBeybladeNameSnapshot,
            CreatedAtUtc = now
        });
        match.Status = TournamentMatchStatus.InProgress;
        match.UpdatedAtUtc = now;
        match.Version = Guid.NewGuid().ToByteArray();
        battle.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
    }

    private static void MaterializeIndividualLineup(TournamentMatch match, int sequenceNo)
    {
        var a = match.Battle!.LineupSelections.Where(x => x.SequenceNo == sequenceNo && x.UserId == match.Battle.PlayerAId).OrderBy(x => x.PositionNo).ToList();
        var b = match.Battle.LineupSelections.Where(x => x.SequenceNo == sequenceNo && x.UserId == match.Battle.PlayerBId).OrderBy(x => x.PositionNo).ToList();
        for (var i = 0; i < a.Count; i++) AddLineup(match.Battle, sequenceNo, i + 1, a[i], b[i]);
    }

    private static void MaterializeTeamLineup(TournamentMatch match, int sequenceNo)
    {
        var battle = match.Battle!;
        var aOrder = battle.TeamOrderSelections.Where(x => x.SequenceNo == sequenceNo && x.TournamentEntryId == match.SideAEntryId).OrderBy(x => x.PositionNo).ToList();
        var bOrder = battle.TeamOrderSelections.Where(x => x.SequenceNo == sequenceNo && x.TournamentEntryId == match.SideBEntryId).OrderBy(x => x.PositionNo).ToList();
        var position = 1;
        for (var bladePosition = 1; bladePosition <= match.Tournament.BeybladesPerPlayer; bladePosition++)
        {
            for (var memberPosition = 0; memberPosition < aOrder.Count; memberPosition++)
            {
                var a = battle.LineupSelections.Single(x => x.SequenceNo == sequenceNo && x.UserId == aOrder[memberPosition].UserId && x.PositionNo == bladePosition);
                var b = battle.LineupSelections.Single(x => x.SequenceNo == sequenceNo && x.UserId == bOrder[memberPosition].UserId && x.PositionNo == bladePosition);
                AddLineup(battle, sequenceNo, position++, a, b);
            }
        }
    }

    private static void AddLineup(Battle battle, int sequenceNo, int position, BattleLineupSelection a, BattleLineupSelection b)
    {
        battle.Lineups.Add(new BattleLineup
        {
            SequenceNo = sequenceNo, PositionNo = position,
            PlayerAId = a.UserId, PlayerADisplayNameSnapshot = a.PlayerDisplayNameSnapshot,
            PlayerABeybladeId = a.BeybladeId, PlayerABeybladeNameSnapshot = a.BeybladeNameSnapshot,
            PlayerBId = b.UserId, PlayerBDisplayNameSnapshot = b.PlayerDisplayNameSnapshot,
            PlayerBBeybladeId = b.BeybladeId, PlayerBBeybladeNameSnapshot = b.BeybladeNameSnapshot,
            IsCurrent = true
        });
    }
}
