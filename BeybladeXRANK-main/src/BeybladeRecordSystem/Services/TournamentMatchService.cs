using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeybladeRecordSystem.Services;

public record TournamentMatchWorkspace(
    TournamentMatch Match,
    TournamentMatchParticipant? CurrentParticipant,
    IReadOnlyList<BattleLineupSelection> VisibleSelections,
    IReadOnlyList<BattleTeamOrderSelection> VisibleTeamOrder,
    IReadOnlyList<BattleLineupSelection> CurrentPrivateSelections,
    IReadOnlyList<BattleTeamOrderSelection> CurrentPrivateTeamOrder,
    IReadOnlyList<Beyblade> AvailableBeyblades,
    IReadOnlyList<LineupPresetItem> RecentLineup,
    bool IsOrganizer)
{
    public string PollToken => string.Join(':',
        Match.Status,
        Convert.ToBase64String(Match.Version),
        Match.Battle is null ? string.Empty : Convert.ToBase64String(Match.Battle.Version));
}

public enum TournamentMatchActionKind
{
    RespondParticipation,
    SubmitLineup,
    SubmitTeamOrder,
    ConfirmLineup,
    SubmitReorder,
    SubmitTeamReorder,
    ReviewParticipation,
    AssignSides,
    RecordBattle,
    CompleteBattle
}

public record TournamentMatchAction(
    int TournamentId,
    int MatchId,
    int SequenceNumber,
    TournamentMatchStatus Status,
    TournamentMatchActionKind Kind,
    string Label,
    bool IsOrganizer);

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
            : await db.Beyblades.WithConfiguration().Where(x => x.UserId == userId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        var recentLineup = participant is null || blades.Count == 0 ||
            match.Status != TournamentMatchStatus.LineupSelection || privateSelections.Count > 0
            ? []
            : await LineupPreset.GetMostRecentValidAsync(
                db, userId, match.Tournament.BeybladesPerPlayer, blades);
        return new TournamentMatchWorkspace(match, participant, visible, visibleTeamOrder, privateSelections, privateTeamOrder, blades, recentLineup, isOrganizer);
    }

    public async Task<IReadOnlyList<TournamentMatchAction>> GetActionableAsync(int tournamentId, int userId)
    {
        return await GetActionableForUserAsync(userId, tournamentId);
    }

    public async Task<IReadOnlyList<TournamentMatchAction>> GetActionableForUserAsync(
        int userId,
        int? tournamentId = null)
    {
        var query = db.TournamentMatches.AsNoTracking().AsSplitQuery()
            .Include(x => x.Tournament)
            .Include(x => x.Participants)
            .Include(x => x.Battle).ThenInclude(x => x!.LineupSelections)
            .Include(x => x.Battle).ThenInclude(x => x!.TeamOrderSelections)
            .Include(x => x.Battle).ThenInclude(x => x!.Lineups)
            .Where(x =>
                (tournamentId == null || x.TournamentId == tournamentId) &&
                (x.Tournament.OrganizerUserId == userId || x.Participants.Any(p => p.UserId == userId)) &&
                x.Status >= TournamentMatchStatus.AwaitingParticipationConfirmation &&
                x.Status < TournamentMatchStatus.Completed);

        var matches = await query.OrderBy(x => x.SequenceNumber).ToListAsync();
        return matches
            .Select(x => CreateAction(x, userId))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
    }

    private static TournamentMatchAction? CreateAction(TournamentMatch match, int userId)
    {
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId);
        if (participant is not null)
        {
            if (match.Status == TournamentMatchStatus.AwaitingParticipationConfirmation &&
                participant.Status == TournamentParticipationStatus.Pending)
                return Action(TournamentMatchActionKind.RespondParticipation, "回覆出賽通知");

            if (match.Status == TournamentMatchStatus.LineupSelection &&
                participant.Status == TournamentParticipationStatus.Accepted &&
                match.Battle is not null &&
                !match.Battle.LineupSelections.Any(x => x.SequenceNo == 1 && x.UserId == userId))
                return Action(TournamentMatchActionKind.SubmitLineup, "提交私密陣容");

            if (match.Status == TournamentMatchStatus.TeamOrderSelection &&
                participant.Status == TournamentParticipationStatus.Accepted &&
                participant.IsMatchRepresentative &&
                match.Battle is not null &&
                !match.Battle.TeamOrderSelections.Any(x =>
                    x.SequenceNo == 1 && x.TournamentEntryId == participant.TournamentEntryId))
                return Action(TournamentMatchActionKind.SubmitTeamOrder, "提交隊員出戰順序");

            if (match.Status == TournamentMatchStatus.LineupReview &&
                participant.Status == TournamentParticipationStatus.Accepted &&
                !participant.LineupConfirmed)
                return Action(TournamentMatchActionKind.ConfirmLineup, "確認公開陣容");

            if (match.Status == TournamentMatchStatus.ReorderSelection &&
                participant.Status == TournamentParticipationStatus.Accepted &&
                match.Battle is not null)
            {
                var currentSequence = match.Battle.Lineups.Where(x => x.IsCurrent)
                    .Select(x => x.SequenceNo).DefaultIfEmpty(0).Max();
                var nextSequence = currentSequence + 1;
                if (!match.Battle.LineupSelections.Any(x =>
                        x.SequenceNo == nextSequence && x.UserId == userId))
                    return Action(TournamentMatchActionKind.SubmitReorder, "提交陀螺重排");
                if (match.Tournament.Mode == TournamentMode.Team && participant.IsMatchRepresentative &&
                    !match.Battle.TeamOrderSelections.Any(x =>
                        x.SequenceNo == nextSequence && x.TournamentEntryId == participant.TournamentEntryId))
                    return Action(TournamentMatchActionKind.SubmitTeamReorder, "提交隊員重排");
            }
        }

        if (match.Tournament.OrganizerUserId != userId) return null;
        return match.Status switch
        {
            TournamentMatchStatus.AwaitingParticipationConfirmation when
                match.Participants.Any(x => x.Status == TournamentParticipationStatus.Pending) =>
                Action(TournamentMatchActionKind.ReviewParticipation, "檢視出賽回覆／判定未到"),
            TournamentMatchStatus.LineupLocked or TournamentMatchStatus.SideSelection =>
                Action(TournamentMatchActionKind.AssignSides, "指定 B／X Side 並開賽"),
            TournamentMatchStatus.InProgress =>
                Action(TournamentMatchActionKind.RecordBattle, "裁判記錄比分"),
            TournamentMatchStatus.VictoryPendingCompletion =>
                Action(TournamentMatchActionKind.CompleteBattle, "確認對戰結束"),
            _ => null
        };

        TournamentMatchAction Action(TournamentMatchActionKind kind, string label) => new(
            match.TournamentId,
            match.Id,
            match.SequenceNumber,
            match.Status,
            kind,
            label,
            match.Tournament.OrganizerUserId == userId);
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

    public async Task<ServiceResult> SubmitIndividualLineupAsync(
        int matchId, int userId, IReadOnlyList<int> bladeIds, IReadOnlyList<int>? configurationIds = null)
        => await SubmitLineupAsync(matchId, userId, bladeIds, configurationIds);

    public async Task<ServiceResult> SubmitLineupAsync(int matchId, int userId, IReadOnlyList<int> bladeIds, IReadOnlyList<int>? configurationIds = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LineupPartRules.AcquireSubmissionLockAsync(db, matchId);
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
            return LineupVersions.Matches(existing, bladeIds, configurationIds) ? ServiceResult.Success() : ServiceResult.Failure("陣容已提交，不能再次更換。");

        var blades = await db.Beyblades.WithConfiguration().Where(x => bladeIds.Contains(x.Id) && x.UserId == userId && !x.IsDeleted).ToDictionaryAsync(x => x.Id);
        if (blades.Count != expectedCount) return ServiceResult.Failure("所選陀螺必須屬於你且尚未刪除。");
        var versions = LineupVersions.Resolve(bladeIds, configurationIds, blades);
        if (!versions.Succeeded) return ServiceResult.Failure(versions.Error!);
        var teammateIds = match.Participants
            .Where(x => x.TournamentEntryId == participant.TournamentEntryId)
            .Select(x => x.UserId)
            .ToHashSet();
        var teammateConfigurationIds = match.Battle.LineupSelections
            .Where(x => x.SequenceNo == 1 && teammateIds.Contains(x.UserId) && x.BeybladeConfigurationId.HasValue)
            .Select(x => x.BeybladeConfigurationId!.Value)
            .ToArray();
        List<BeybladeConfiguration> teammateConfigurations = teammateConfigurationIds.Length == 0
            ? []
            : await db.BeybladeConfigurations
                .Include(x => x.Parts)
                .Where(x => teammateConfigurationIds.Contains(x.Id))
                .ToListAsync();
        var partValidation = LineupPartRules.ValidateNoDuplicates(
            versions.Value!.Values.Concat(teammateConfigurations));
        if (!partValidation.Succeeded) return partValidation;
        var userName = await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < bladeIds.Count; i++)
        {
            var blade = blades[bladeIds[i]];
            match.Battle.LineupSelections.Add(new BattleLineupSelection
            {
                SequenceNo = 1, UserId = userId, PositionNo = i + 1, BeybladeId = blade.Id, BeybladeConfigurationId = versions.Value![blade.Id]?.Id,
                PlayerDisplayNameSnapshot = userName, BeybladeNameSnapshot = LineupVersions.Snapshot(blade, versions.Value![blade.Id]), SubmittedAtUtc = now
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
                BeybladeId = snapshot.BeybladeId, BeybladeConfigurationId = snapshot.BeybladeConfigurationId,
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

    public async Task<ServiceResult> DeclareNoShowAsync(
        int matchId,
        int organizerUserId,
        int absentEntryId,
        string? reason,
        bool confirmed)
    {
        var noShowReason = reason?.Trim();
        if (!confirmed)
            return ServiceResult.Failure("請先確認未到判定會讓對手直接獲勝，且本場不建立比分 Battle。");
        if (noShowReason?.Length > 490)
            return ServiceResult.Failure("未到原因不可超過 490 個字元。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await db.TournamentMatches.AsSplitQuery()
            .Include(x => x.Tournament)
            .Include(x => x.Participants)
            .Include(x => x.Battle)
            .SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        if (match.Tournament.OrganizerUserId != organizerUserId)
            return ServiceResult.Failure("只有主辦方裁判可以判定未到。");
        if (match.Status != TournamentMatchStatus.AwaitingParticipationConfirmation)
            return ServiceResult.Failure("只有等待出賽確認的對局可以判定未到。");
        if (absentEntryId != match.SideAEntryId && absentEntryId != match.SideBEntryId)
            return ServiceResult.Failure("指定的未到 Entry 不屬於這場對局。");
        if (match.Battle is not null)
            return ServiceResult.Failure("這場對局已建立 Battle，不能改用未到判定。");

        var absentParticipants = match.Participants
            .Where(x => x.TournamentEntryId == absentEntryId)
            .ToList();
        if (absentParticipants.Count == 0 ||
            absentParticipants.All(x => x.Status != TournamentParticipationStatus.Pending))
            return ServiceResult.Failure("只能對仍有必要選手未回覆的 Entry 判定未到。");

        var winnerEntryId = match.SideAEntryId == absentEntryId
            ? match.SideBEntryId
            : match.SideAEntryId;
        if (winnerEntryId is null)
            return ServiceResult.Failure("對局缺少已解析的對手 Entry。");

        var now = DateTime.UtcNow;
        foreach (var participant in absentParticipants.Where(x =>
                     x.Status == TournamentParticipationStatus.Pending))
        {
            participant.Status = TournamentParticipationStatus.NoShow;
            participant.RespondedAtUtc = now;
            participant.Version = Guid.NewGuid().ToByteArray();
        }
        foreach (var pending in match.Participants.Where(x =>
                     x.TournamentEntryId != absentEntryId &&
                     x.Status == TournamentParticipationStatus.Pending))
        {
            pending.Status = TournamentParticipationStatus.Invalidated;
            pending.Version = Guid.NewGuid().ToByteArray();
        }

        var resolutionReason = string.IsNullOrWhiteSpace(noShowReason)
            ? "NoShow"
            : $"NoShow: {noShowReason}";
        await new TournamentProgressionService(db).CompleteMatchAndAdvanceAsync(
            match,
            winnerEntryId.Value,
            absentEntryId,
            TournamentMatchStatus.Walkover,
            resolutionReason,
            now);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("對局狀態已被更新，請重新整理後再操作。");
        }
    }

    public async Task<ServiceResult> ForfeitAsync(int matchId, int userId, string? reason)
    {
        var forfeitReason = reason?.Trim();
        if (forfeitReason?.Length > 500)
            return ServiceResult.Failure("棄權原因不可超過 500 個字元。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var match = await MatchQuery().SingleOrDefaultAsync(x => x.Id == matchId);
        if (match is null) return ServiceResult.Failure("找不到對局。");
        var participant = match.Participants.SingleOrDefault(x => x.UserId == userId && x.Status == TournamentParticipationStatus.Accepted);
        if (participant is null) return ServiceResult.Failure("只有這場對局已確認出賽的參賽者可以棄權。");
        if (match.Status is not (TournamentMatchStatus.InProgress or TournamentMatchStatus.ReorderSelection) ||
            match.Battle?.Status != BattleStatus.InProgress)
            return ServiceResult.Failure("只有進行中或等待下一組重排的對局可以棄權。");

        var loserEntryId = participant.TournamentEntryId;
        if (match.SideAEntryId != loserEntryId && match.SideBEntryId != loserEntryId)
            return ServiceResult.Failure("參賽者與對局 Entry 資料不一致。");
        var winnerEntryId = match.SideAEntryId == loserEntryId ? match.SideBEntryId : match.SideAEntryId;
        if (winnerEntryId is null || match.SideAEntryId is null || match.SideBEntryId is null)
            return ServiceResult.Failure("對局缺少已解析的參賽單位。");

        var now = DateTime.UtcNow;
        var battle = match.Battle;
        foreach (var round in battle.Rounds.Where(x => x.Status == BattleRoundStatus.InProgress))
            foreach (var roundEvent in round.Events)
            {
                roundEvent.IsEffective = false;
                roundEvent.InvalidationReason = BattleRoundEventInvalidationReason.BattleTerminated;
            }

        (battle.SideAScore, battle.SideBScore) = BattleRules.CalculateScores(battle.Rounds);
        var sideAWon = winnerEntryId == match.SideAEntryId;
        battle.Status = BattleStatus.Forfeited;
        battle.WinningSide = sideAWon
            ? battle.SideADesignation
            : battle.SideADesignation is null ? null : BattleRules.Opposite(battle.SideADesignation.Value);
        battle.WinningPlayerId = match.Tournament.Mode == TournamentMode.Individual
            ? sideAWon ? battle.PlayerAId : battle.PlayerBId
            : null;
        battle.CompletedAtUtc = now;
        battle.Version = Guid.NewGuid().ToByteArray();

        foreach (var pending in match.Participants.Where(x => x.Status == TournamentParticipationStatus.Pending))
        {
            pending.Status = TournamentParticipationStatus.Invalidated;
            pending.Version = Guid.NewGuid().ToByteArray();
        }

        await new TournamentProgressionService(db).CompleteMatchAndAdvanceAsync(
            match,
            winnerEntryId.Value,
            loserEntryId,
            TournamentMatchStatus.Forfeited,
            string.IsNullOrWhiteSpace(forfeitReason) ? "ParticipantForfeit" : forfeitReason,
            now);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> VoidAndReopenAsync(
        int matchId,
        int organizerUserId,
        string? reason,
        bool confirmDownstreamReset)
    {
        var voidReason = reason?.Trim() ?? string.Empty;
        if (voidReason.Length is < 1 or > 500)
            return ServiceResult.Failure("撤銷原因須為 1 至 500 個字元。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.AsSplitQuery()
            .Include(x => x.Matches).ThenInclude(x => x.Participants)
            .Include(x => x.Matches).ThenInclude(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(x => x.Matches.Any(m => m.Id == matchId));
        if (tournament is null) return ServiceResult.Failure("找不到對局。");
        if (tournament.OrganizerUserId != organizerUserId)
            return ServiceResult.Failure("只有主辦方裁判可以撤銷並重開對局。");
        if (tournament.Status == TournamentStatus.Cancelled)
            return ServiceResult.Failure("已取消的比賽不能重開對局。");

        var match = tournament.Matches.Single(x => x.Id == matchId);
        if (match.Bracket != TournamentBracket.Playoff &&
            tournament.Matches.Any(x => x.Bracket == TournamentBracket.Playoff))
            return ServiceResult.Failure("冠軍加賽已建立，不能直接重開例行對局，以免加賽名單與排名失去一致性。");
        var battle = match.Battle;
        if (match.IsBye || battle is null || battle.SourceType == BattleSourceType.Quick)
            return ServiceResult.Failure("此對局沒有可撤銷的 Tournament Battle。");
        if (battle.Status is BattleStatus.Cancelled or BattleStatus.Voided)
            return ServiceResult.Failure("此 Battle 已取消或撤銷。");
        if (match.SideAEntryId is null || match.SideBEntryId is null)
            return ServiceResult.Failure("對局缺少已解析的參賽單位。");

        if (tournament.Format == TournamentFormat.Swiss &&
            tournament.Matches.Any(x => x.Bracket == match.Bracket && x.RoundNumber > match.RoundNumber))
            return ServiceResult.Failure("瑞士輪後續配對已建立；請先由最末輪逆序撤銷，才能重開此對局。");

        var downstream = tournament.Matches.Where(x => x.Id != match.Id &&
            ((x.SideASourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                x.SideASourceReferenceId == match.Id) ||
             (x.SideBSourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                x.SideBSourceReferenceId == match.Id)))
            .ToList();
        var blocked = downstream.FirstOrDefault(x => IsStartedOrResolvedDownstream(x.Status));
        if (blocked is not null)
            return ServiceResult.Failure($"下游對局 #{blocked.SequenceNumber} 已開始或已有正式結果；必須先由下游逆序撤銷。");
        if (!confirmDownstreamReset && downstream.Any(HasPreparedLineup))
            return ServiceResult.Failure("下游已有 Lineup；請勾選確認撤銷下游通知與陣容後再繼續。");

        var now = DateTime.UtcNow;
        foreach (var dependent in downstream)
        {
            if (dependent.Battle is { } dependentBattle)
                VoidBattle(dependentBattle, dependent, organizerUserId,
                    $"上游 Battle #{battle.Id} 已撤銷", now);
            db.TournamentMatchParticipants.RemoveRange(dependent.Participants);
            if (dependent.SideASourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                dependent.SideASourceReferenceId == match.Id)
                dependent.SideAEntryId = null;
            if (dependent.SideBSourceKind is TournamentParticipantSourceKind.MatchWinner or TournamentParticipantSourceKind.MatchLoser &&
                dependent.SideBSourceReferenceId == match.Id)
                dependent.SideBEntryId = null;
            dependent.WinnerEntryId = null;
            dependent.LoserEntryId = null;
            dependent.Status = TournamentMatchStatus.WaitingForParticipants;
            dependent.ResolutionReason = $"UpstreamBattleVoided:{battle.Id}";
            dependent.CompletedAtUtc = null;
            dependent.UpdatedAtUtc = now;
            dependent.Version = Guid.NewGuid().ToByteArray();
        }

        VoidBattle(battle, match, organizerUserId, voidReason, now);
        foreach (var participant in match.Participants)
        {
            participant.Status = TournamentParticipationStatus.Pending;
            participant.LineupConfirmed = false;
            participant.RespondedAtUtc = null;
            participant.LineupConfirmedAtUtc = null;
            participant.NotifiedAtUtc = now;
            participant.Version = Guid.NewGuid().ToByteArray();
        }
        match.WinnerEntryId = null;
        match.LoserEntryId = null;
        match.Status = TournamentMatchStatus.AwaitingParticipationConfirmation;
        match.ResolutionReason = $"ReopenedAfterVoid:{battle.Id}";
        match.CompletedAtUtc = null;
        match.UpdatedAtUtc = now;
        match.Version = Guid.NewGuid().ToByteArray();
        tournament.Status = TournamentStatus.InProgress;
        tournament.CompletedAtUtc = null;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    private IQueryable<TournamentMatch> MatchQuery() => db.TournamentMatches
        .AsSplitQuery()
        .Include(x => x.Tournament)
        .Include(x => x.SideAEntry).ThenInclude(x => x!.IndividualUser)
        .Include(x => x.SideAEntry).ThenInclude(x => x!.Members)
        .Include(x => x.SideBEntry).ThenInclude(x => x!.IndividualUser)
        .Include(x => x.SideBEntry).ThenInclude(x => x!.Members)
        .Include(x => x.WinnerEntry)
        .Include(x => x.LoserEntry)
        .Include(x => x.Participants).ThenInclude(x => x.User)
        .Include(x => x.Battle).ThenInclude(x => x!.LineupSelections)
        .Include(x => x.Battle).ThenInclude(x => x!.TeamOrderSelections)
        .Include(x => x.Battle).ThenInclude(x => x!.Lineups)
        .Include(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
        .Include(x => x.VoidedBattles).ThenInclude(x => x.VoidedByUser);

    internal static bool IsStartedOrResolvedDownstream(TournamentMatchStatus status) => status is
        TournamentMatchStatus.InProgress or TournamentMatchStatus.ReorderSelection or
        TournamentMatchStatus.VictoryPendingCompletion or TournamentMatchStatus.Completed or
        TournamentMatchStatus.Forfeited or TournamentMatchStatus.Walkover;

    internal static bool HasPreparedLineup(TournamentMatch match) => match.Battle is not null || match.Status is
        TournamentMatchStatus.LineupSelection or TournamentMatchStatus.TeamOrderSelection or
        TournamentMatchStatus.LineupReview or TournamentMatchStatus.LineupLocked or TournamentMatchStatus.SideSelection;

    internal static void VoidBattle(
        Battle battle,
        TournamentMatch ownerMatch,
        int organizerUserId,
        string reason,
        DateTime now)
    {
        battle.VoidSnapshot = JsonSerializer.Serialize(new
        {
            BattleStatus = battle.Status,
            battle.SideAScore,
            battle.SideBScore,
            battle.WinningSide,
            battle.WinningPlayerId,
            MatchStatus = ownerMatch.Status,
            ownerMatch.WinnerEntryId,
            ownerMatch.LoserEntryId,
            Events = battle.Rounds.OrderBy(x => x.RoundNo).SelectMany(round => round.Events.OrderBy(x => x.EventSequence).Select(roundEvent => new
            {
                round.RoundNo,
                roundEvent.EventSequence,
                roundEvent.EventType,
                roundEvent.ActorPlayerId,
                roundEvent.WinnerPlayerId,
                roundEvent.ResultType,
                roundEvent.ScoreAwarded,
                roundEvent.IsEffective
            }))
        });
        foreach (var roundEvent in battle.Rounds.SelectMany(x => x.Events))
        {
            roundEvent.IsEffective = false;
            roundEvent.InvalidationReason = BattleRoundEventInvalidationReason.BattleTerminated;
        }
        battle.Status = BattleStatus.Voided;
        battle.VoidedByUserId = organizerUserId;
        battle.VoidReason = reason;
        battle.VoidedAtUtc = now;
        battle.Version = Guid.NewGuid().ToByteArray();
        battle.TournamentMatchId = null;
        battle.TournamentMatch = null;
        battle.VoidedTournamentMatchId = ownerMatch.Id;
        battle.VoidedTournamentMatch = ownerMatch;
        ownerMatch.Battle = null;
    }

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
            PlayerABeybladeId = a.BeybladeId, PlayerAConfigurationId = a.BeybladeConfigurationId, PlayerABeybladeNameSnapshot = a.BeybladeNameSnapshot,
            PlayerBId = b.UserId, PlayerBDisplayNameSnapshot = b.PlayerDisplayNameSnapshot,
            PlayerBBeybladeId = b.BeybladeId, PlayerBConfigurationId = b.BeybladeConfigurationId, PlayerBBeybladeNameSnapshot = b.BeybladeNameSnapshot,
            IsCurrent = true
        });
    }
}
