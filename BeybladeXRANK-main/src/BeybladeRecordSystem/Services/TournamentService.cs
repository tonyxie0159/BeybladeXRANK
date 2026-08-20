using System.Security.Cryptography;
using System.Text;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public sealed record CreateTournamentRequest(
    string Name,
    TournamentRuleSet RuleSet,
    TournamentRegistrationMode RegistrationMode,
    TournamentFormat Format,
    int TargetEntryCount,
    string? Notes);

public enum TournamentListFilter
{
    All,
    WaitingForMe,
    RegistrationOpen,
    InProgress,
    Completed,
    Cancelled,
    Participating,
    Hosted
}

public sealed record TournamentListItem(
    int Id,
    string Name,
    TournamentStatus Status,
    TournamentRegistrationStage RegistrationStage,
    string OrganizerDisplayName,
    TournamentMode Mode,
    TournamentRegistrationMode RegistrationMode,
    TournamentFormat Format,
    TournamentRuleSet RuleSet,
    string RuleSummary,
    string? Notes,
    int RegisteredEntryCount,
    int TargetEntryCount,
    bool IsParticipant,
    bool IsOrganizer,
    bool HasPendingAction,
    bool HasPendingInvitation,
    int? ActionMatchId,
    string? PendingActionLabel,
    DateTime UpdatedAtUtc);

public sealed record TournamentListPage(IReadOnlyList<TournamentListItem> Items, int PageNumber, int TotalPages, int TotalCount);

public sealed record TournamentTeamWorkspace(
    TournamentEntry? Team,
    IReadOnlyList<TournamentInvitation> PendingInvitations,
    IReadOnlyList<TournamentInvitation> PendingRepresentativeTransfers);

public class TournamentService(AppDbContext db)
{
    public async Task<ServiceResult<Tournament>> CreateAsync(int organizerUserId, CreateTournamentRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var notes = request.Notes?.Trim();
        if (name.Length is < 1 or > 120) return ServiceResult<Tournament>.Failure("比賽名稱須為 1 至 120 個字元。");
        if (notes?.Length > 1000) return ServiceResult<Tournament>.Failure("備註不可超過 1000 個字元。");
        if (!await db.Users.AnyAsync(x => x.Id == organizerUserId)) return ServiceResult<Tournament>.Failure("找不到主辦方帳號。");

        TournamentRuleDefinition rule;
        int entryLimit;
        try
        {
            rule = TournamentRuleCatalog.Get(request.RuleSet);
            entryLimit = TournamentRuleCatalog.EntryLimit(request.Format);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ServiceResult<Tournament>.Failure("不支援所選的規則或賽制。");
        }

        if (request.TargetEntryCount is < 2 || request.TargetEntryCount > entryLimit)
            return ServiceResult<Tournament>.Failure($"此賽制的參賽單位數須介於 2 與 {entryLimit} 之間。");
        if (rule.Mode == TournamentMode.Individual && request.RegistrationMode != TournamentRegistrationMode.Individual)
            return ServiceResult<Tournament>.Failure("單人賽只能使用個人報名。");
        if (rule.Mode == TournamentMode.Team && request.RegistrationMode == TournamentRegistrationMode.Individual)
            return ServiceResult<Tournament>.Failure("團體賽須選擇整隊報名或系統配隊。");

        var now = DateTime.UtcNow;
        var tournament = new Tournament
        {
            Name = name,
            Mode = rule.Mode,
            Format = request.Format,
            RegistrationMode = request.RegistrationMode,
            RuleSet = request.RuleSet,
            Status = TournamentStatus.RegistrationOpen,
            RegistrationStage = TournamentRegistrationStage.Open,
            TeamSize = rule.TeamSize,
            BeybladesPerPlayer = rule.BeybladesPerPlayer,
            ScoreToWin = rule.ScoreToWin,
            TargetEntryCount = request.TargetEntryCount,
            OrganizerUserId = organizerUserId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            RulesSnapshot = TournamentRuleCatalog.BuildSnapshot(rule, request.Format),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = Guid.NewGuid().ToByteArray()
        };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        return ServiceResult<Tournament>.Success(tournament);
    }

    public async Task<TournamentListPage> GetListAsync(int userId, TournamentListFilter filter, int pageNumber = 1)
    {
        const int pageSize = 20;
        pageNumber = Math.Max(1, pageNumber);

        var matchActions = await new TournamentMatchService(db).GetActionableForUserAsync(userId);
        var firstMatchActionByTournament = matchActions
            .GroupBy(x => x.TournamentId)
            .ToDictionary(x => x.Key, x => x.OrderBy(action => action.SequenceNumber).First());
        var matchActionTournamentIds = firstMatchActionByTournament.Keys.ToArray();

        var tournamentActionRows = await db.Tournaments.AsNoTracking()
            .Where(x =>
                x.Invitations.Any(i => i.InvitedUserId == userId && i.Status == TournamentInvitationStatus.Pending) ||
                (x.Status == TournamentStatus.RegistrationOpen &&
                 x.RegistrationMode == TournamentRegistrationMode.CompleteTeam &&
                 x.Entries.Any(e => e.Status == TournamentEntryStatus.Pending &&
                     e.Members.Any(m => m.UserId == userId && m.IsRepresentative))) ||
                (x.OrganizerUserId == userId && x.Status == TournamentStatus.RegistrationOpen &&
                 (x.RegistrationStage == TournamentRegistrationStage.CapacityReached ||
                  x.RegistrationStage == TournamentRegistrationStage.Closed ||
                  x.RegistrationStage == TournamentRegistrationStage.AwaitingTeamFormation ||
                  x.RegistrationStage == TournamentRegistrationStage.ScheduleDraftCreated)))
            .Select(x => new
            {
                x.Id,
                x.RegistrationStage,
                HasPendingInvitation = x.Invitations.Any(i =>
                    i.InvitedUserId == userId && i.Status == TournamentInvitationStatus.Pending),
                HasPendingTeamAction = x.Status == TournamentStatus.RegistrationOpen &&
                    x.RegistrationMode == TournamentRegistrationMode.CompleteTeam &&
                    x.Entries.Any(e => e.Status == TournamentEntryStatus.Pending &&
                        e.Members.Any(m => m.UserId == userId && m.IsRepresentative)),
                HasCompletePendingTeam = x.TeamSize != null && x.Entries.Any(e =>
                    e.Status == TournamentEntryStatus.Pending &&
                    e.Members.Any(m => m.UserId == userId && m.IsRepresentative) &&
                    e.Members.Count == x.TeamSize.Value)
            })
            .ToListAsync();
        var tournamentActionById = tournamentActionRows.ToDictionary(
            x => x.Id,
            x => (
                x.HasPendingInvitation,
                Label: x.HasPendingInvitation
                    ? "回覆邀請"
                    : x.HasPendingTeamAction
                        ? x.HasCompletePendingTeam ? "確認整隊報名" : "完成隊伍組建"
                        : x.RegistrationStage switch
                        {
                            TournamentRegistrationStage.CapacityReached => "關閉報名並準備賽程",
                            TournamentRegistrationStage.Closed => "產生隊伍／賽程",
                            TournamentRegistrationStage.AwaitingTeamFormation => "處理系統配隊",
                            TournamentRegistrationStage.ScheduleDraftCreated => "確認賽程並正式開始",
                            _ => "管理賽事"
                        }));
        var actionTournamentIds = matchActionTournamentIds
            .Concat(tournamentActionById.Keys)
            .Distinct()
            .ToArray();

        var query = db.Tournaments.AsNoTracking();
        query = filter switch
        {
            TournamentListFilter.WaitingForMe => query.Where(x => actionTournamentIds.Contains(x.Id)),
            TournamentListFilter.RegistrationOpen => query.Where(x => x.Status == TournamentStatus.RegistrationOpen),
            TournamentListFilter.InProgress => query.Where(x => x.Status == TournamentStatus.InProgress),
            TournamentListFilter.Completed => query.Where(x => x.Status == TournamentStatus.Completed),
            TournamentListFilter.Cancelled => query.Where(x => x.Status == TournamentStatus.Cancelled),
            TournamentListFilter.Participating => query.Where(x =>
                x.Entries.Any(e => e.Status != TournamentEntryStatus.Withdrawn &&
                    (e.IndividualUserId == userId || e.Members.Any(m => m.UserId == userId)) &&
                    (e.Status == TournamentEntryStatus.Registered || x.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam))),
            TournamentListFilter.Hosted => query.Where(x => x.OrganizerUserId == userId),
            _ => query
        };

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        pageNumber = Math.Min(pageNumber, totalPages);
        var rows = await query
            .OrderByDescending(x => matchActionTournamentIds.Contains(x.Id))
            .ThenByDescending(x => actionTournamentIds.Contains(x.Id))
            .ThenByDescending(x => x.UpdatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Status,
                x.RegistrationStage,
                OrganizerDisplayName = x.OrganizerUser.DisplayName,
                x.Mode,
                x.RegistrationMode,
                x.Format,
                x.RuleSet,
                RuleSummary = x.RulesSnapshot,
                x.Notes,
                RegisteredEntryCount = x.Entries.Count(e => e.Status == TournamentEntryStatus.Registered),
                x.TargetEntryCount,
                IsParticipant = x.Entries.Any(e => e.Status != TournamentEntryStatus.Withdrawn &&
                    (e.IndividualUserId == userId || e.Members.Any(m => m.UserId == userId)) &&
                    (e.Status == TournamentEntryStatus.Registered || x.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam)),
                IsOrganizer = x.OrganizerUserId == userId,
                x.UpdatedAtUtc
            })
            .ToListAsync();
        var items = rows.Select(x =>
        {
            firstMatchActionByTournament.TryGetValue(x.Id, out var matchAction);
            tournamentActionById.TryGetValue(x.Id, out var tournamentAction);
            var hasTournamentAction = tournamentActionById.ContainsKey(x.Id);
            return new TournamentListItem(
                x.Id,
                x.Name,
                x.Status,
                x.RegistrationStage,
                x.OrganizerDisplayName,
                x.Mode,
                x.RegistrationMode,
                x.Format,
                x.RuleSet,
                x.RuleSummary,
                x.Notes,
                x.RegisteredEntryCount,
                x.TargetEntryCount,
                x.IsParticipant,
                x.IsOrganizer,
                matchAction is not null || hasTournamentAction,
                hasTournamentAction && tournamentAction.HasPendingInvitation,
                matchAction?.MatchId,
                matchAction?.Label ?? (hasTournamentAction ? tournamentAction.Label : null),
                x.UpdatedAtUtc);
        }).ToList();
        return new TournamentListPage(items, pageNumber, totalPages, totalCount);
    }

    public Task<Tournament?> GetDetailsAsync(int tournamentId) => db.Tournaments
        .AsNoTracking()
        .Include(x => x.OrganizerUser)
        .Include(x => x.Entries.Where(e => e.Status == TournamentEntryStatus.Registered))
            .ThenInclude(x => x.IndividualUser)
        .Include(x => x.Entries.Where(e => e.Status == TournamentEntryStatus.Registered))
            .ThenInclude(x => x.Members)
        .Include(x => x.Matches)
        .SingleOrDefaultAsync(x => x.Id == tournamentId);

    public async Task<TournamentPublicDetailsViewModel?> GetPublicDetailsAsync(
        int tournamentId,
        int userId)
    {
        var tournament = await db.Tournaments.AsNoTracking().AsSplitQuery()
            .Include(x => x.OrganizerUser)
            .Include(x => x.Entries.Where(e => e.Status == TournamentEntryStatus.Registered))
                .ThenInclude(x => x.Members)
            .Include(x => x.Matches).ThenInclude(x => x.Participants)
            .Include(x => x.Matches).ThenInclude(x => x.Battle).ThenInclude(x => x!.Lineups)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return null;

        var entries = tournament.Entries
            .OrderBy(x => x.SchedulePosition ?? int.MaxValue)
            .ThenBy(x => x.RegisteredAtUtc)
            .Select(x => new TournamentPublicEntryViewModel(
                x.Id,
                x.RegistrationNumber,
                x.SchedulePosition,
                x.DisplayNameSnapshot,
                x.Members.OrderBy(m => m.MemberOrder)
                    .Select(m => m.DisplayNameSnapshot)
                    .ToList()))
            .ToList();
        var entryById = entries.ToDictionary(x => x.Id);
        var matchById = tournament.Matches.ToDictionary(x => x.Id);

        string SourceLabel(
            TournamentParticipantSourceKind? sourceKind,
            int? sourceReferenceId,
            int? resolvedEntryId,
            bool isBye)
        {
            if (isBye) return "Bye";
            if (resolvedEntryId is int entryId && entryById.TryGetValue(entryId, out var entry))
                return entry.DisplayName;
            if (sourceKind == TournamentParticipantSourceKind.Entry &&
                sourceReferenceId is int sourceEntryId && entryById.TryGetValue(sourceEntryId, out var sourceEntry))
                return sourceEntry.DisplayName;
            if (sourceReferenceId is int sourceMatchId && matchById.TryGetValue(sourceMatchId, out var sourceMatch))
                return sourceKind == TournamentParticipantSourceKind.MatchLoser
                    ? $"對局 #{sourceMatch.SequenceNumber} 敗方"
                    : $"對局 #{sourceMatch.SequenceNumber} 勝方";
            return "待賽程決定";
        }

        static string? ResolutionSummary(TournamentMatch match)
        {
            if (match.IsBye)
                return match.IsSeedQualifier ? "種子資格輪空" : "輪空";
            if (match.Status == TournamentMatchStatus.Walkover)
            {
                if (match.ResolutionReason?.StartsWith("NoShow", StringComparison.Ordinal) == true)
                {
                    var reason = match.ResolutionReason.StartsWith("NoShow: ", StringComparison.Ordinal)
                        ? match.ResolutionReason[8..]
                        : null;
                    return string.IsNullOrWhiteSpace(reason) ? "未到判定" : $"未到判定：{reason}";
                }
                return match.ResolutionReason == "ParticipationDeclined" ? "拒絕出賽" : "不戰勝";
            }
            if (match.Status == TournamentMatchStatus.Forfeited)
                return string.IsNullOrWhiteSpace(match.ResolutionReason) || match.ResolutionReason == "ParticipantForfeit"
                    ? "棄權"
                    : $"棄權：{match.ResolutionReason}";
            return match.Status switch
            {
                TournamentMatchStatus.NotRequired => "無須進行",
                TournamentMatchStatus.Cancelled => "賽事取消",
                TournamentMatchStatus.Voided => "結果已撤銷",
                _ => null
            };
        }

        var matches = tournament.Matches.OrderBy(x => x.SequenceNumber).Select(match =>
        {
            TournamentPublicBattleViewModel? publicBattle = null;
            if (match.Battle is { } battle && battle.Lineups.Count > 0)
            {
                publicBattle = new TournamentPublicBattleViewModel(
                    battle.Id,
                    battle.Status,
                    battle.ScoreToWin,
                    battle.SideAScore,
                    battle.SideBScore,
                    battle.SideADesignation,
                    battle.Lineups.OrderBy(x => x.SequenceNo).ThenBy(x => x.PositionNo)
                        .Select(x => new TournamentPublicLineupPositionViewModel(
                            x.SequenceNo,
                            x.PositionNo,
                            x.PlayerADisplayNameSnapshot,
                            x.PlayerABeybladeNameSnapshot,
                            x.PlayerBDisplayNameSnapshot,
                            x.PlayerBBeybladeNameSnapshot,
                            x.IsCurrent))
                        .ToList());
            }

            var canOpenWorkspace = !match.IsBye &&
                match.SideAEntryId is not null && match.SideBEntryId is not null &&
                (tournament.OrganizerUserId == userId || match.Participants.Any(x => x.UserId == userId));
            return new TournamentPublicMatchViewModel(
                match.Id,
                match.Bracket,
                match.RoundNumber,
                match.MatchNumber,
                match.SequenceNumber,
                match.Status,
                SourceLabel(match.SideASourceKind, match.SideASourceReferenceId, match.SideAEntryId, false),
                SourceLabel(match.SideBSourceKind, match.SideBSourceReferenceId, match.SideBEntryId, match.IsBye),
                match.WinnerEntryId is int winnerId && entryById.TryGetValue(winnerId, out var winner)
                    ? winner.DisplayName
                    : null,
                match.LoserEntryId is int loserId && entryById.TryGetValue(loserId, out var loser)
                    ? loser.DisplayName
                    : null,
                match.IsBye,
                match.IsSeedQualifier,
                match.IsResetFinal,
                match.Status >= TournamentMatchStatus.AwaitingParticipationConfirmation &&
                    match.Status < TournamentMatchStatus.Completed,
                canOpenWorkspace,
                ResolutionSummary(match),
                match.CompletedAtUtc,
                publicBattle);
        }).ToList();

        var pollSource = string.Join('|',
            Convert.ToBase64String(tournament.Version),
            string.Join(',', tournament.Matches.OrderBy(x => x.Id).Select(x =>
                $"{x.Id}:{Convert.ToBase64String(x.Version)}:{(x.Battle is null ? string.Empty : Convert.ToBase64String(x.Battle.Version))}")));
        var pollToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pollSource)));
        return new TournamentPublicDetailsViewModel(
            tournament.Id,
            tournament.Name,
            tournament.OrganizerUser.DisplayName,
            tournament.Mode,
            tournament.RegistrationMode,
            tournament.Format,
            tournament.RuleSet,
            tournament.Status,
            tournament.RegistrationStage,
            tournament.TeamSize,
            tournament.BeybladesPerPlayer,
            tournament.ScoreToWin,
            tournament.TargetEntryCount,
            tournament.RulesSnapshot,
            tournament.Notes,
            tournament.CancellationReason,
            tournament.CreatedAtUtc,
            tournament.UpdatedAtUtc,
            tournament.RegistrationClosedAtUtc,
            tournament.StartedAtUtc,
            tournament.CompletedAtUtc,
            tournament.CancelledAtUtc,
            entries,
            matches,
            pollToken);
    }

    public async Task<ServiceResult> GenerateScheduleDraftAsync(int tournamentId, int organizerUserId, int? randomSeed = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).ThenInclude(x => x.Members)
            .Include(x => x.Matches).SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以產生賽程草稿。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage is not (TournamentRegistrationStage.Closed or TournamentRegistrationStage.ScheduleDraftCreated))
            return ServiceResult.Failure("請先關閉報名並完成組隊。");
        var entries = tournament.Entries.Where(x => x.Status == TournamentEntryStatus.Registered).OrderBy(x => x.Id).ToList();
        if (entries.Count < 2) return ServiceResult.Failure("至少需要兩個正式參賽單位。");
        if (tournament.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam &&
            tournament.Entries.Any(x => x.Status == TournamentEntryStatus.Pending))
            return ServiceResult.Failure("仍有玩家等待補足隊伍，不能產生賽程。");
        if (entries.Count > TournamentRuleCatalog.EntryLimit(tournament.Format))
            return ServiceResult.Failure("正式參賽單位超過此賽制上限。");

        if (tournament.Matches.Count > 0)
            await ClearScheduleAsync(tournament);
        var seed = randomSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        var schedule = CreateInitialSchedule(tournament.Format, entries.Select(x => x.Id).ToList(), seed);
        var now = DateTime.UtcNow;
        var persistedByDefinitionId = new Dictionary<int, TournamentMatch>();
        foreach (var definition in schedule.Matches)
        {
            var match = new TournamentMatch
            {
                TournamentId = tournamentId,
                Bracket = definition.Bracket,
                RoundNumber = definition.RoundNumber,
                MatchNumber = definition.MatchNumber,
                SequenceNumber = definition.SequenceNumber,
                Status = TournamentMatchStatus.WaitingForParticipants,
                SideASourceKind = definition.SideA.Kind,
                SideASourceReferenceId = definition.SideA.ReferenceId,
                SideBSourceKind = definition.SideB.Kind,
                SideBSourceReferenceId = definition.SideB.ReferenceId,
                SideAEntryId = definition.SideA.Kind == TournamentParticipantSourceKind.Entry ? definition.SideA.ReferenceId : null,
                SideBEntryId = definition.SideB.Kind == TournamentParticipantSourceKind.Entry ? definition.SideB.ReferenceId : null,
                IsSeedQualifier = definition.IsSeedQualifier,
                IsResetFinal = definition.IsResetFinal,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            };
            persistedByDefinitionId.Add(definition.Id, match);
            db.TournamentMatches.Add(match);
        }
        await db.SaveChangesAsync();

        foreach (var definition in schedule.Matches)
        {
            var persisted = persistedByDefinitionId[definition.Id];
            ApplyPersistedSource(definition.SideA, persistedByDefinitionId, persisted, isSideA: true);
            ApplyPersistedSource(definition.SideB, persistedByDefinitionId, persisted, isSideA: false);
        }

        var maxMatchNumber = schedule.Matches.GroupBy(x => (x.Bracket, x.RoundNumber))
            .ToDictionary(x => x.Key, x => x.Max(m => m.MatchNumber));
        var byeIndex = 0;
        foreach (var bye in schedule.Byes)
        {
            var key = (bye.Bracket, bye.RoundNumber);
            var matchNumber = maxMatchNumber.GetValueOrDefault(key) + 1;
            maxMatchNumber[key] = matchNumber;
            var sourceReference = bye.Participant.Kind == TournamentParticipantSourceKind.Entry
                ? bye.Participant.ReferenceId
                : persistedByDefinitionId[bye.Participant.ReferenceId].Id;
            db.TournamentMatches.Add(new TournamentMatch
            {
                TournamentId = tournamentId,
                Bracket = bye.Bracket,
                RoundNumber = bye.RoundNumber,
                MatchNumber = matchNumber,
                SequenceNumber = schedule.Matches.Count + ++byeIndex,
                Status = TournamentMatchStatus.Completed,
                SideASourceKind = bye.Participant.Kind,
                SideASourceReferenceId = sourceReference,
                SideAEntryId = bye.Participant.Kind == TournamentParticipantSourceKind.Entry ? bye.Participant.ReferenceId : null,
                WinnerEntryId = bye.Participant.Kind == TournamentParticipantSourceKind.Entry ? bye.Participant.ReferenceId : null,
                IsBye = true,
                IsSeedQualifier = bye.IsSeedQualifierAdvancement,
                ResolutionReason = bye.IsSeedQualifierAdvancement ? "SeedQualifierBye" : "Bye",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            });
        }

        var positions = schedule.Matches.SelectMany(x => new[] { x.SideA, x.SideB }).Concat(schedule.Byes.Select(x => x.Participant))
            .Where(x => x.Kind == TournamentParticipantSourceKind.Entry).Select(x => x.ReferenceId).Distinct().ToList();
        positions.AddRange(entries.Select(x => x.Id).Where(id => !positions.Contains(id)));
        for (var index = 0; index < positions.Count; index++)
            entries.Single(x => x.Id == positions[index]).SchedulePosition = index + 1;
        tournament.RegistrationStage = TournamentRegistrationStage.ScheduleDraftCreated;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> AbandonScheduleDraftAsync(int tournamentId, int organizerUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以放棄賽程草稿。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.ScheduleDraftCreated)
            return ServiceResult.Failure("目前沒有可放棄的賽程草稿。");
        await ClearScheduleAsync(tournament);
        tournament.RegistrationStage = TournamentRegistrationStage.Closed;
        tournament.UpdatedAtUtc = DateTime.UtcNow;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ReorderScheduleEntriesAsync(int tournamentId, int organizerUserId, IReadOnlyList<int> orderedEntryIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以調整賽程位置。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.ScheduleDraftCreated)
            return ServiceResult.Failure("只有正式開始前的賽程草稿可以調整位置。");
        var entriesByPosition = tournament.Entries.Where(x => x.Status == TournamentEntryStatus.Registered)
            .OrderBy(x => x.SchedulePosition).ToList();
        if (orderedEntryIds.Count != entriesByPosition.Count || orderedEntryIds.Distinct().Count() != orderedEntryIds.Count ||
            !orderedEntryIds.Order().SequenceEqual(entriesByPosition.Select(x => x.Id).Order()))
            return ServiceResult.Failure("提交的 Entry 必須與目前正式參賽單位完全相同且不可重複。");

        var replacementByOriginalId = entriesByPosition.Select((entry, index) => (entry.Id, ReplacementId: orderedEntryIds[index]))
            .ToDictionary(x => x.Id, x => x.ReplacementId);
        foreach (var match in tournament.Matches)
        {
            if (match.SideASourceKind == TournamentParticipantSourceKind.Entry)
            {
                match.SideASourceReferenceId = replacementByOriginalId[match.SideASourceReferenceId];
                match.SideAEntryId = match.SideASourceReferenceId;
                if (match.IsBye) match.WinnerEntryId = match.SideAEntryId;
            }
            if (match.SideBSourceKind == TournamentParticipantSourceKind.Entry && match.SideBSourceReferenceId is int sideBReference)
            {
                match.SideBSourceReferenceId = replacementByOriginalId[sideBReference];
                match.SideBEntryId = match.SideBSourceReferenceId;
            }
            match.UpdatedAtUtc = DateTime.UtcNow;
            match.Version = Guid.NewGuid().ToByteArray();
        }
        for (var index = 0; index < entriesByPosition.Count; index++)
            entriesByPosition[index].SchedulePosition = -(index + 1);
        await db.SaveChangesAsync();
        for (var index = 0; index < orderedEntryIds.Count; index++)
            tournament.Entries.Single(x => x.Id == orderedEntryIds[index]).SchedulePosition = index + 1;
        tournament.UpdatedAtUtc = DateTime.UtcNow;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> StartTournamentAsync(int tournamentId, int organizerUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).ThenInclude(x => x.Members).Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以正式開始比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.ScheduleDraftCreated || tournament.Matches.Count == 0)
            return ServiceResult.Failure("請先產生完整賽程草稿。");
        if (tournament.Entries.Any(x => x.Status == TournamentEntryStatus.Pending))
            return ServiceResult.Failure("仍有未完成的參賽單位。");
        var firstReady = tournament.Matches.Where(x => !x.IsBye && x.SideAEntryId is not null && x.SideBEntryId is not null)
            .OrderBy(x => x.SequenceNumber).FirstOrDefault();
        if (firstReady is null) return ServiceResult.Failure("賽程沒有可開始的第一場對局。");

        var now = DateTime.UtcNow;
        firstReady.Status = TournamentMatchStatus.AwaitingParticipationConfirmation;
        firstReady.UpdatedAtUtc = now;
        firstReady.Version = Guid.NewGuid().ToByteArray();
        tournament.Status = TournamentStatus.InProgress;
        tournament.RegistrationStage = TournamentRegistrationStage.AwaitingStart;
        tournament.StartedAtUtc = now;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        var participantEntries = new[] { firstReady.SideAEntryId, firstReady.SideBEntryId };
        foreach (var entryId in participantEntries)
        {
            var entry = tournament.Entries.Single(x => x.Id == entryId);
            var userIds = tournament.Mode == TournamentMode.Individual
                ? new[] { entry.IndividualUserId!.Value }
                : entry.Members.Select(x => x.UserId).ToArray();
            foreach (var userId in userIds)
                db.TournamentMatchParticipants.Add(new TournamentMatchParticipant
                {
                    TournamentMatch = firstReady,
                    TournamentEntryId = entry.Id,
                    UserId = userId,
                    IsMatchRepresentative = tournament.Mode == TournamentMode.Team && entry.Members.Single(x => x.UserId == userId).IsRepresentative,
                    Status = TournamentParticipationStatus.Pending,
                    NotifiedAtUtc = now,
                    Version = Guid.NewGuid().ToByteArray()
                });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CancelTournamentAsync(int tournamentId, int organizerUserId, string? reason)
    {
        var cancellationReason = reason?.Trim();
        if (cancellationReason?.Length > 500)
            return ServiceResult.Failure("取消原因不可超過 500 個字元。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.AsSplitQuery()
            .Include(x => x.Invitations)
            .Include(x => x.Matches).ThenInclude(x => x.Participants)
            .Include(x => x.Matches).ThenInclude(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以取消整場比賽。");
        if (tournament.Status == TournamentStatus.Cancelled) return ServiceResult.Failure("比賽已經取消。");
        if (tournament.Status == TournamentStatus.Completed) return ServiceResult.Failure("已完成的比賽不能取消。");

        var now = DateTime.UtcNow;
        tournament.Status = TournamentStatus.Cancelled;
        tournament.CancellationReason = string.IsNullOrWhiteSpace(cancellationReason) ? null : cancellationReason;
        tournament.CancelledAtUtc = now;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();

        foreach (var invitation in tournament.Invitations.Where(x => x.Status == TournamentInvitationStatus.Pending))
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = now;
        }

        foreach (var match in tournament.Matches)
        {
            foreach (var participant in match.Participants.Where(x => x.Status == TournamentParticipationStatus.Pending))
            {
                participant.Status = TournamentParticipationStatus.Invalidated;
                participant.Version = Guid.NewGuid().ToByteArray();
            }

            if (match.Status is TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or
                TournamentMatchStatus.Forfeited or TournamentMatchStatus.Voided or TournamentMatchStatus.NotRequired)
                continue;

            match.Status = TournamentMatchStatus.Cancelled;
            match.ResolutionReason = "TournamentCancelled";
            match.CompletedAtUtc = now;
            match.UpdatedAtUtc = now;
            match.Version = Guid.NewGuid().ToByteArray();

            if (match.Battle is not { } battle || battle.Status is BattleStatus.Completed or BattleStatus.Forfeited or BattleStatus.Voided)
                continue;

            foreach (var round in battle.Rounds.Where(x => x.Status == BattleRoundStatus.InProgress))
                foreach (var roundEvent in round.Events)
                {
                    roundEvent.IsEffective = false;
                    roundEvent.InvalidationReason = BattleRoundEventInvalidationReason.BattleTerminated;
                }

            (battle.SideAScore, battle.SideBScore) = BattleRules.CalculateScores(battle.Rounds);
            battle.Status = BattleStatus.Cancelled;
            battle.WinningSide = null;
            battle.WinningPlayerId = null;
            battle.CompletedAtUtc = now;
            battle.Version = Guid.NewGuid().ToByteArray();
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<TournamentInvitation>> InviteParticipantAsync(
        int tournamentId,
        int organizerUserId,
        string accountOrDisplayName)
    {
        var search = accountOrDisplayName?.Trim() ?? string.Empty;
        if (search.Length == 0)
            return ServiceResult<TournamentInvitation>.Failure("請輸入 Account 或完整 DisplayName。");

        var tournament = await db.Tournaments.Include(x => x.Entries)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null)
            return ServiceResult<TournamentInvitation>.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId)
            return ServiceResult<TournamentInvitation>.Failure("只有主辦方可以邀請參賽者。");
        if (tournament.Status != TournamentStatus.RegistrationOpen ||
            tournament.RegistrationStage != TournamentRegistrationStage.Open)
            return ServiceResult<TournamentInvitation>.Failure("目前不接受新的參賽邀請。");
        if (tournament.Mode != TournamentMode.Individual ||
            tournament.RegistrationMode != TournamentRegistrationMode.Individual)
            return ServiceResult<TournamentInvitation>.Failure("主辦方參賽邀請目前只適用個人賽；團體賽請使用隊伍邀請。");
        if (tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered) >= tournament.TargetEntryCount)
            return ServiceResult<TournamentInvitation>.Failure("報名名額已滿。");

        var accountMatch = await db.Users.SingleOrDefaultAsync(x => x.Account == search);
        var displayMatches = accountMatch is null
            ? await db.Users.Where(x => x.DisplayName == search).Take(2).ToListAsync()
            : [];
        if (accountMatch is null && displayMatches.Count > 1)
            return ServiceResult<TournamentInvitation>.Failure("有多位玩家使用此 DisplayName，請改用 Account。");
        var invitedUser = accountMatch ?? displayMatches.SingleOrDefault();
        if (invitedUser is null)
            return ServiceResult<TournamentInvitation>.Failure("找不到指定玩家。");
        if (tournament.Entries.Any(x =>
                x.IndividualUserId == invitedUser.Id && x.Status == TournamentEntryStatus.Registered))
            return ServiceResult<TournamentInvitation>.Failure("該玩家已經報名這場比賽。");
        if (await db.TournamentInvitations.AnyAsync(x =>
                x.TournamentId == tournamentId &&
                x.InvitedUserId == invitedUser.Id &&
                x.Type == TournamentInvitationType.Tournament &&
                x.Status == TournamentInvitationStatus.Pending))
            return ServiceResult<TournamentInvitation>.Failure("已向該玩家發出待處理的參賽邀請。");

        var invitation = new TournamentInvitation
        {
            TournamentId = tournamentId,
            TournamentEntryId = null,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = organizerUserId,
            Type = TournamentInvitationType.Tournament,
            Status = TournamentInvitationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.TournamentInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync();
            return ServiceResult<TournamentInvitation>.Success(invitation);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<TournamentInvitation>.Failure("邀請狀態發生衝突，請重新整理後再試。");
        }
    }

    public Task<TournamentInvitation?> GetPendingParticipantInvitationAsync(int tournamentId, int userId) =>
        db.TournamentInvitations.AsNoTracking()
            .Include(x => x.InvitedByUser)
            .Where(x => x.TournamentId == tournamentId &&
                x.InvitedUserId == userId &&
                x.Type == TournamentInvitationType.Tournament &&
                x.Status == TournamentInvitationStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

    public async Task<ServiceResult> RespondToTournamentInvitationAsync(
        int invitationId,
        int invitedUserId,
        bool accept)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invitation = await db.TournamentInvitations
            .Include(x => x.InvitedUser)
            .Include(x => x.Tournament).ThenInclude(x => x.Entries)
            .SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null || invitation.InvitedUserId != invitedUserId)
            return ServiceResult.Failure("找不到你的參賽邀請。");
        if (invitation.Type != TournamentInvitationType.Tournament ||
            invitation.Status != TournamentInvitationStatus.Pending)
            return ServiceResult.Failure("邀請已處理或失效。");

        var now = DateTime.UtcNow;
        if (!accept)
        {
            invitation.Status = TournamentInvitationStatus.Declined;
            invitation.RespondedAtUtc = now;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }

        var tournament = invitation.Tournament;
        if (tournament.Status != TournamentStatus.RegistrationOpen ||
            tournament.RegistrationStage != TournamentRegistrationStage.Open ||
            tournament.Mode != TournamentMode.Individual ||
            tournament.RegistrationMode != TournamentRegistrationMode.Individual)
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = now;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Failure("這場比賽目前不接受邀請加入。");
        }

        var activeCount = tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered);
        if (activeCount >= tournament.TargetEntryCount)
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = now;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Failure("報名名額已滿，邀請已失效。");
        }

        var entry = tournament.Entries.SingleOrDefault(x => x.IndividualUserId == invitedUserId);
        if (entry?.Status == TournamentEntryStatus.Registered)
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = now;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Failure("你已經報名這場比賽，邀請已失效。");
        }

        var registrationNumber = await CreateUniqueRegistrationNumberAsync(tournament.Id);
        if (entry is null)
        {
            entry = new TournamentEntry
            {
                TournamentId = tournament.Id,
                IndividualUserId = invitedUserId,
                DisplayNameSnapshot = invitation.InvitedUser.DisplayName,
                RegistrationNumber = registrationNumber,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now
            };
            db.TournamentEntries.Add(entry);
        }
        else
        {
            entry.RegistrationNumber = registrationNumber;
            entry.WithdrawnAtUtc = null;
        }

        entry.Status = TournamentEntryStatus.Registered;
        entry.RegisteredAtUtc = now;
        entry.UpdatedAtUtc = now;
        invitation.Status = TournamentInvitationStatus.Accepted;
        invitation.RespondedAtUtc = now;
        var capacityReached = activeCount + 1 == tournament.TargetEntryCount;
        tournament.RegistrationStage = capacityReached
            ? TournamentRegistrationStage.CapacityReached
            : TournamentRegistrationStage.Open;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await InvalidatePendingTournamentInvitationsAsync(
            tournament.Id, now, invitedUserId: invitedUserId, exceptInvitationId: invitation.Id);
        if (capacityReached)
            await InvalidatePendingTournamentInvitationsAsync(
                tournament.Id, now, exceptInvitationId: invitation.Id);

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("報名狀態剛被其他操作更新，請重新整理後再試。");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("邀請接受未完成，可能是名額或 Entry 發生衝突，請重新整理後再試。");
        }
    }

    public async Task<ServiceResult> RegisterIndividualAsync(int tournamentId, int userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.Open)
            return ServiceResult.Failure("目前不接受報名。");
        if (tournament.Mode != TournamentMode.Individual || tournament.RegistrationMode != TournamentRegistrationMode.Individual)
            return ServiceResult.Failure("這不是個人報名賽事。");
        if (!await db.Users.AnyAsync(x => x.Id == userId)) return ServiceResult.Failure("找不到玩家帳號。");

        var activeCount = tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered);
        if (activeCount >= tournament.TargetEntryCount) return ServiceResult.Failure("報名名額已滿。");
        var entry = tournament.Entries.SingleOrDefault(x => x.IndividualUserId == userId);
        if (entry?.Status == TournamentEntryStatus.Registered) return ServiceResult.Failure("你已經報名這場比賽。");

        var now = DateTime.UtcNow;
        var registrationNumber = await CreateUniqueRegistrationNumberAsync(tournamentId);
        if (entry is null)
        {
            var displayName = await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleAsync();
            entry = new TournamentEntry
            {
                TournamentId = tournamentId,
                IndividualUserId = userId,
                DisplayNameSnapshot = displayName,
                RegistrationNumber = registrationNumber,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now
            };
            db.TournamentEntries.Add(entry);
        }
        else
        {
            entry.RegistrationNumber = registrationNumber;
            entry.WithdrawnAtUtc = null;
        }

        entry.Status = TournamentEntryStatus.Registered;
        entry.RegisteredAtUtc = now;
        entry.UpdatedAtUtc = now;
        tournament.UpdatedAtUtc = now;
        var capacityReached = activeCount + 1 == tournament.TargetEntryCount;
        tournament.RegistrationStage = capacityReached
            ? TournamentRegistrationStage.CapacityReached
            : TournamentRegistrationStage.Open;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await InvalidatePendingTournamentInvitationsAsync(tournamentId, now, invitedUserId: userId);
        if (capacityReached)
            await InvalidatePendingTournamentInvitationsAsync(tournamentId, now);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("報名狀態剛被其他操作更新，請重新整理後再試。");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("報名未完成，可能是名額或參賽編號發生衝突，請重新整理後再試。");
        }
    }

    public async Task<ServiceResult> WithdrawAsync(int tournamentId, int userId)
    {
        var tournament = await db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen) return ServiceResult.Failure("比賽開始後退出將使用棄權流程。");
        var entry = tournament.Entries.SingleOrDefault(x => x.IndividualUserId == userId && x.Status == TournamentEntryStatus.Registered);
        if (entry is null) return ServiceResult.Failure("找不到你的有效報名。");

        var now = DateTime.UtcNow;
        entry.Status = TournamentEntryStatus.Withdrawn;
        entry.WithdrawnAtUtc = now;
        entry.UpdatedAtUtc = now;
        if (tournament.RegistrationStage == TournamentRegistrationStage.ScheduleDraftCreated)
        {
            await ClearScheduleAsync(tournament);
            tournament.RegistrationStage = TournamentRegistrationStage.Closed;
        }
        else if (tournament.RegistrationStage == TournamentRegistrationStage.CapacityReached)
            tournament.RegistrationStage = TournamentRegistrationStage.Open;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> CloseRegistrationAsync(int tournamentId, int organizerUserId)
    {
        var tournament = await db.Tournaments.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以關閉報名。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage is not (TournamentRegistrationStage.Open or TournamentRegistrationStage.CapacityReached))
            return ServiceResult.Failure("目前狀態不能關閉報名。");
        if (tournament.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam)
        {
            var registeredPlayers = await db.TournamentEntryMembers.CountAsync(x => x.TournamentId == tournamentId &&
                x.TournamentEntry.Status != TournamentEntryStatus.Withdrawn);
            if (registeredPlayers < tournament.TeamSize * 2)
                return ServiceResult.Failure($"至少需要 {tournament.TeamSize * 2} 位玩家才能關閉報名並配成兩隊。");
        }
        else if (tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered) < 2)
        {
            return ServiceResult.Failure("至少需要兩個有效參賽單位才能關閉報名。");
        }

        var now = DateTime.UtcNow;
        tournament.RegistrationStage = TournamentRegistrationStage.Closed;
        tournament.RegistrationClosedAtUtc = now;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await InvalidatePendingInvitationsAsync(tournamentId, now);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ReopenRegistrationAsync(int tournamentId, int organizerUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments
            .Include(x => x.Entries).ThenInclude(x => x.Members)
            .Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null)
            return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId)
            return ServiceResult.Failure("只有主辦方可以重新開放報名。");
        if (tournament.Status != TournamentStatus.RegistrationOpen)
            return ServiceResult.Failure("比賽正式開始、完成或取消後不能重新開放報名。");
        if (tournament.RegistrationStage is TournamentRegistrationStage.AwaitingStart)
            return ServiceResult.Failure("比賽已進入正式開始流程，不能重新開放報名。");
        if (tournament.RegistrationStage is TournamentRegistrationStage.Open or TournamentRegistrationStage.CapacityReached)
            return ServiceResult.Success();
        if (tournament.RegistrationStage is not (
                TournamentRegistrationStage.Closed or
                TournamentRegistrationStage.AwaitingTeamFormation or
                TournamentRegistrationStage.ScheduleDraftCreated))
            return ServiceResult.Failure("目前狀態不能重新開放報名。");

        if (tournament.Matches.Count > 0 || tournament.Entries.Any(x => x.SchedulePosition is not null))
        {
            try
            {
                await ClearScheduleAsync(tournament);
            }
            catch (InvalidOperationException)
            {
                return ServiceResult.Failure("賽程已有正式 Battle，不能重新開放報名。");
            }
        }

        var now = DateTime.UtcNow;
        tournament.RegistrationStage = GetReopenedRegistrationStage(tournament);
        tournament.RegistrationClosedAtUtc = null;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("報名狀態剛被其他操作更新，請重新整理後再試。");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("重新開放報名時資料發生衝突，請重新整理後再試。");
        }
    }

    public async Task<ServiceResult> RegisterForSystemPairingAsync(int tournamentId, int userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).ThenInclude(x => x.Members).Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.Open)
            return ServiceResult.Failure("目前不接受配隊登記。");
        if (tournament.Mode != TournamentMode.Team || tournament.RegistrationMode != TournamentRegistrationMode.SystemAssignedTeam)
            return ServiceResult.Failure("這場比賽不使用系統配隊。");
        if (tournament.TeamSize is null) return ServiceResult.Failure("隊伍人數設定無效。");
        if (tournament.Entries.Where(x => x.Status != TournamentEntryStatus.Withdrawn).SelectMany(x => x.Members).Any(x => x.UserId == userId))
            return ServiceResult.Failure("你已登記這場系統配隊比賽。");
        var activePlayerCount = tournament.Entries.Where(x => x.Status != TournamentEntryStatus.Withdrawn).Sum(x => x.Members.Count);
        var playerCapacity = tournament.TargetEntryCount * tournament.TeamSize.Value;
        if (activePlayerCount >= playerCapacity) return ServiceResult.Failure("配隊登記名額已滿。");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId);
        if (user is null) return ServiceResult.Failure("找不到玩家帳號。");

        var now = DateTime.UtcNow;
        tournament.Entries.Add(new TournamentEntry
        {
            DisplayNameSnapshot = user.DisplayName,
            Status = TournamentEntryStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Members =
            [
                new TournamentEntryMember
                {
                    TournamentId = tournamentId,
                    UserId = userId,
                    MemberOrder = 1,
                    IsRepresentative = true,
                    DisplayNameSnapshot = user.DisplayName,
                    JoinedAtUtc = now
                }
            ]
        });
        tournament.RegistrationStage = activePlayerCount + 1 == playerCapacity
            ? TournamentRegistrationStage.CapacityReached
            : TournamentRegistrationStage.Open;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("配隊登記發生衝突，請重新整理後再試。");
        }
    }

    public async Task<ServiceResult> WithdrawFromSystemPairingAsync(int tournamentId, int userId)
    {
        var tournament = await db.Tournaments.Include(x => x.Entries).ThenInclude(x => x.Members).Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationMode != TournamentRegistrationMode.SystemAssignedTeam)
            return ServiceResult.Failure("目前不能取消配隊登記。");
        var entry = tournament.Entries.SingleOrDefault(x => x.Status != TournamentEntryStatus.Withdrawn && x.Members.Any(m => m.UserId == userId));
        if (entry is null) return ServiceResult.Failure("找不到你的配隊登記。");
        var member = entry.Members.Single(x => x.UserId == userId);
        var now = DateTime.UtcNow;
        db.TournamentEntryMembers.Remove(member);
        if (entry.Members.Count == 1)
            entry.Status = TournamentEntryStatus.Withdrawn;
        else
        {
            entry.Status = TournamentEntryStatus.Pending;
            entry.RegistrationNumber = null;
            entry.RegisteredAtUtc = null;
            entry.DisplayNameSnapshot = string.Join(" / ", entry.Members.Where(x => x.Id != member.Id).OrderBy(x => x.MemberOrder).Select(x => x.DisplayNameSnapshot));
        }
        if (tournament.RegistrationStage == TournamentRegistrationStage.ScheduleDraftCreated)
        {
            await ClearScheduleAsync(tournament);
            tournament.RegistrationStage = TournamentRegistrationStage.AwaitingTeamFormation;
        }
        else tournament.RegistrationStage = tournament.RegistrationStage == TournamentRegistrationStage.CapacityReached
            ? TournamentRegistrationStage.Open
            : tournament.RegistrationStage is TournamentRegistrationStage.Closed or TournamentRegistrationStage.AwaitingStart
                ? TournamentRegistrationStage.AwaitingTeamFormation
                : tournament.RegistrationStage;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> GenerateSystemAssignedTeamsAsync(int tournamentId, int organizerUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.Include(x => x.Entries).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以執行系統配隊。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationMode != TournamentRegistrationMode.SystemAssignedTeam ||
            tournament.RegistrationStage is not (TournamentRegistrationStage.Closed or TournamentRegistrationStage.AwaitingTeamFormation))
            return ServiceResult.Failure("目前狀態不能執行系統配隊。");
        if (tournament.TeamSize is not (2 or 3)) return ServiceResult.Failure("隊伍人數設定無效。");

        var sourceEntries = tournament.Entries.Where(x => x.Status != TournamentEntryStatus.Withdrawn).ToList();
        var members = sourceEntries.SelectMany(x => x.Members).ToArray();
        if (members.Length < tournament.TeamSize * 2) return ServiceResult.Failure("至少需要能組成兩支完整隊伍的玩家。");
        for (var index = members.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (members[index], members[swapIndex]) = (members[swapIndex], members[index]);
        }

        foreach (var oldEntry in sourceEntries)
        {
            oldEntry.Status = TournamentEntryStatus.Withdrawn;
            oldEntry.RegistrationNumber = null;
            oldEntry.RegisteredAtUtc = null;
        }
        var now = DateTime.UtcNow;
        var completeTeamCount = members.Length / tournament.TeamSize.Value;
        var leftoverCount = members.Length % tournament.TeamSize.Value;
        for (var teamIndex = 0; teamIndex < completeTeamCount; teamIndex++)
        {
            var teamMembers = members.Skip(teamIndex * tournament.TeamSize.Value).Take(tournament.TeamSize.Value).ToArray();
            var entry = new TournamentEntry
            {
                TournamentId = tournamentId,
                RegistrationNumber = await CreateUniqueRegistrationNumberAsync(tournamentId),
                DisplayNameSnapshot = string.Join(" / ", teamMembers.Select(x => x.DisplayNameSnapshot)),
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RegisteredAtUtc = now
            };
            for (var memberIndex = 0; memberIndex < teamMembers.Length; memberIndex++)
            {
                var member = teamMembers[memberIndex];
                member.TournamentEntry = entry;
                member.MemberOrder = memberIndex + 1;
                member.IsRepresentative = memberIndex == 0;
                entry.Members.Add(member);
            }
            db.TournamentEntries.Add(entry);
        }
        for (var leftoverIndex = 0; leftoverIndex < leftoverCount; leftoverIndex++)
        {
            var member = members[completeTeamCount * tournament.TeamSize.Value + leftoverIndex];
            var entry = new TournamentEntry
            {
                TournamentId = tournamentId,
                DisplayNameSnapshot = member.DisplayNameSnapshot,
                Status = TournamentEntryStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            member.TournamentEntry = entry;
            member.MemberOrder = 1;
            member.IsRepresentative = true;
            entry.Members.Add(member);
            db.TournamentEntries.Add(entry);
        }
        tournament.RegistrationStage = leftoverCount == 0
            ? TournamentRegistrationStage.Closed
            : TournamentRegistrationStage.AwaitingTeamFormation;
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SwapSystemAssignedMembersAsync(int tournamentId, int organizerUserId, int firstMemberId, int secondMemberId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await db.Tournaments.SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult.Failure("找不到比賽。");
        if (tournament.OrganizerUserId != organizerUserId) return ServiceResult.Failure("只有主辦方可以交換隊員。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationMode != TournamentRegistrationMode.SystemAssignedTeam ||
            tournament.RegistrationStage != TournamentRegistrationStage.Closed)
            return ServiceResult.Failure("目前狀態不能交換隊員。");
        var members = await db.TournamentEntryMembers.Include(x => x.TournamentEntry).Where(x => x.TournamentId == tournamentId &&
            (x.Id == firstMemberId || x.Id == secondMemberId)).ToListAsync();
        if (members.Count != 2) return ServiceResult.Failure("找不到要交換的兩位隊員。");
        var first = members.Single(x => x.Id == firstMemberId);
        var second = members.Single(x => x.Id == secondMemberId);
        if (first.TournamentEntryId == second.TournamentEntryId || first.TournamentEntry.Status != TournamentEntryStatus.Registered || second.TournamentEntry.Status != TournamentEntryStatus.Registered)
            return ServiceResult.Failure("只能交換兩支正式隊伍中的不同隊員。");
        var firstEntry = first.TournamentEntry;
        var secondEntry = second.TournamentEntry;
        var firstEntryId = first.TournamentEntryId;
        var secondEntryId = second.TournamentEntryId;
        var firstOrder = first.MemberOrder;
        var secondOrder = second.MemberOrder;
        first.MemberOrder = 1000 + first.Id;
        await db.SaveChangesAsync();
        second.TournamentEntryId = firstEntryId;
        second.MemberOrder = firstOrder;
        await db.SaveChangesAsync();
        first.TournamentEntryId = secondEntryId;
        first.MemberOrder = secondOrder;
        await db.SaveChangesAsync();

        var affectedEntries = await db.TournamentEntries.Include(x => x.Members).Where(x => x.Id == firstEntryId || x.Id == secondEntryId).ToListAsync();
        foreach (var entry in affectedEntries)
        {
            foreach (var member in entry.Members) member.IsRepresentative = false;
            entry.Members.MinBy(x => x.MemberOrder)!.IsRepresentative = true;
            entry.DisplayNameSnapshot = string.Join(" / ", entry.Members.OrderBy(x => x.MemberOrder).Select(x => x.DisplayNameSnapshot));
            entry.UpdatedAtUtc = DateTime.UtcNow;
        }
        tournament.UpdatedAtUtc = DateTime.UtcNow;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<TournamentTeamWorkspace> GetTeamWorkspaceAsync(int tournamentId, int userId)
    {
        var team = await db.TournamentEntries.AsNoTracking()
            .Include(x => x.Members).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.TournamentId == tournamentId &&
                x.Status != TournamentEntryStatus.Withdrawn && x.Members.Any(m => m.UserId == userId));
        var invitations = await db.TournamentInvitations.AsNoTracking()
            .Include(x => x.InvitedByUser)
            .Include(x => x.TournamentEntry).ThenInclude(x => x!.Members).ThenInclude(x => x.User)
            .Where(x => x.TournamentId == tournamentId && x.InvitedUserId == userId &&
                x.Type == TournamentInvitationType.Team && x.Status == TournamentInvitationStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        var transfers = await db.TournamentInvitations.AsNoTracking()
            .Include(x => x.InvitedByUser)
            .Include(x => x.TournamentEntry).ThenInclude(x => x!.Members).ThenInclude(x => x.User)
            .Where(x => x.TournamentId == tournamentId && x.InvitedUserId == userId &&
                x.Type == TournamentInvitationType.RepresentativeTransfer && x.Status == TournamentInvitationStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        return new TournamentTeamWorkspace(team, invitations, transfers);
    }

    public async Task<IReadOnlyList<TournamentEntry>> GetSystemPairingEntriesForOrganizerAsync(int tournamentId, int organizerUserId)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId && x.OrganizerUserId == organizerUserId &&
            x.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam))
            return [];
        return await db.TournamentEntries.AsNoTracking().Include(x => x.Members).ThenInclude(x => x.User)
            .Where(x => x.TournamentId == tournamentId && x.Status != TournamentEntryStatus.Withdrawn)
            .OrderBy(x => x.Status).ThenBy(x => x.Id).ToListAsync();
    }

    public async Task<ServiceResult<TournamentEntry>> CreateTemporaryTeamAsync(int tournamentId, int representativeUserId, string? teamName)
    {
        var tournament = await db.Tournaments.SingleOrDefaultAsync(x => x.Id == tournamentId);
        if (tournament is null) return ServiceResult<TournamentEntry>.Failure("找不到比賽。");
        if (tournament.Status != TournamentStatus.RegistrationOpen || tournament.RegistrationStage != TournamentRegistrationStage.Open)
            return ServiceResult<TournamentEntry>.Failure("目前不能建立隊伍。");
        if (tournament.Mode != TournamentMode.Team || tournament.RegistrationMode != TournamentRegistrationMode.CompleteTeam)
            return ServiceResult<TournamentEntry>.Failure("這場比賽不使用整隊報名。");
        if (await db.TournamentEntryMembers.AnyAsync(x => x.TournamentId == tournamentId &&
            x.TournamentEntry.Status != TournamentEntryStatus.Withdrawn && x.UserId == representativeUserId))
            return ServiceResult<TournamentEntry>.Failure("你已經屬於這場比賽的另一支隊伍。");
        var normalizedName = teamName?.Trim();
        if (normalizedName?.Length > 100) return ServiceResult<TournamentEntry>.Failure("隊名不可超過 100 個字元。");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == representativeUserId);
        if (user is null) return ServiceResult<TournamentEntry>.Failure("找不到玩家帳號。");

        var now = DateTime.UtcNow;
        var entry = new TournamentEntry
        {
            TournamentId = tournamentId,
            TeamName = string.IsNullOrWhiteSpace(normalizedName) ? null : normalizedName,
            DisplayNameSnapshot = string.IsNullOrWhiteSpace(normalizedName) ? user.DisplayName : normalizedName,
            Status = TournamentEntryStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Members =
            [
                new TournamentEntryMember
                {
                    TournamentId = tournamentId,
                    UserId = representativeUserId,
                    MemberOrder = 1,
                    IsRepresentative = true,
                    DisplayNameSnapshot = user.DisplayName,
                    JoinedAtUtc = now
                }
            ]
        };
        db.TournamentEntries.Add(entry);
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult<TournamentEntry>.Success(entry);
    }

    public async Task<ServiceResult> InviteTeamMemberAsync(int tournamentId, int entryId, int representativeUserId, string accountOrDisplayName)
    {
        var search = accountOrDisplayName?.Trim() ?? string.Empty;
        if (search.Length == 0) return ServiceResult.Failure("請輸入 Account 或完整 DisplayName。");
        var entry = await db.TournamentEntries.Include(x => x.Tournament).Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.TournamentId == tournamentId);
        if (entry is null) return ServiceResult.Failure("找不到隊伍。");
        if (entry.Tournament.Status != TournamentStatus.RegistrationOpen || entry.Status != TournamentEntryStatus.Pending)
            return ServiceResult.Failure("目前不能邀請隊員。");
        if (!entry.Members.Any(x => x.UserId == representativeUserId && x.IsRepresentative))
            return ServiceResult.Failure("只有隊伍代表人可以邀請隊員。");
        if (entry.Members.Count >= entry.Tournament.TeamSize) return ServiceResult.Failure("隊伍人數已滿。");

        var accountMatch = await db.Users.SingleOrDefaultAsync(x => x.Account == search);
        var displayMatches = accountMatch is null
            ? await db.Users.Where(x => x.DisplayName == search).Take(2).ToListAsync()
            : [];
        if (accountMatch is null && displayMatches.Count > 1) return ServiceResult.Failure("有多位玩家使用此 DisplayName，請改用 Account。");
        var invitedUser = accountMatch ?? displayMatches.SingleOrDefault();
        if (invitedUser is null) return ServiceResult.Failure("找不到指定玩家。");
        if (entry.Members.Any(x => x.UserId == invitedUser.Id)) return ServiceResult.Failure("該玩家已在隊伍中。");
        if (await db.TournamentEntryMembers.AnyAsync(x => x.TournamentId == tournamentId &&
            x.TournamentEntry.Status != TournamentEntryStatus.Withdrawn && x.UserId == invitedUser.Id))
            return ServiceResult.Failure("該玩家已屬於這場比賽的其他隊伍。");
        if (await db.TournamentInvitations.AnyAsync(x => x.TournamentEntryId == entryId && x.InvitedUserId == invitedUser.Id && x.Status == TournamentInvitationStatus.Pending))
            return ServiceResult.Failure("已向該玩家發出待處理邀請。");

        db.TournamentInvitations.Add(new TournamentInvitation
        {
            TournamentId = tournamentId,
            TournamentEntryId = entryId,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = representativeUserId,
            Type = TournamentInvitationType.Team,
            Status = TournamentInvitationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RespondToTeamInvitationAsync(int invitationId, int invitedUserId, bool accept)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invitation = await db.TournamentInvitations
            .Include(x => x.Tournament)
            .Include(x => x.TournamentEntry).ThenInclude(x => x!.Members)
            .SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null || invitation.InvitedUserId != invitedUserId) return ServiceResult.Failure("找不到你的邀請。");
        if (invitation.Type != TournamentInvitationType.Team || invitation.Status != TournamentInvitationStatus.Pending)
            return ServiceResult.Failure("邀請已處理或失效。");
        if (invitation.Tournament.Status != TournamentStatus.RegistrationOpen || invitation.TournamentEntry is null || invitation.TournamentEntry.Status != TournamentEntryStatus.Pending)
            return ServiceResult.Failure("隊伍目前不接受邀請回應。");

        var now = DateTime.UtcNow;
        invitation.Status = accept ? TournamentInvitationStatus.Accepted : TournamentInvitationStatus.Declined;
        invitation.RespondedAtUtc = now;
        if (!accept)
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        if (invitation.TournamentEntry.Members.Count >= invitation.Tournament.TeamSize)
            return ServiceResult.Failure("隊伍人數已滿。");
        if (await db.TournamentEntryMembers.AnyAsync(x => x.TournamentId == invitation.TournamentId &&
            x.TournamentEntry.Status != TournamentEntryStatus.Withdrawn && x.UserId == invitedUserId))
            return ServiceResult.Failure("你已經屬於這場比賽的另一支隊伍。");
        var user = await db.Users.SingleAsync(x => x.Id == invitedUserId);
        invitation.TournamentEntry.Members.Add(new TournamentEntryMember
        {
            TournamentId = invitation.TournamentId,
            UserId = invitedUserId,
            MemberOrder = invitation.TournamentEntry.Members.Max(x => x.MemberOrder) + 1,
            DisplayNameSnapshot = user.DisplayName,
            JoinedAtUtc = now
        });
        var otherInvitations = await db.TournamentInvitations.Where(x => x.Id != invitationId &&
            x.TournamentId == invitation.TournamentId && x.InvitedUserId == invitedUserId &&
            x.Type == TournamentInvitationType.Team && x.Status == TournamentInvitationStatus.Pending).ToListAsync();
        foreach (var other in otherInvitations)
        {
            other.Status = TournamentInvitationStatus.Invalidated;
            other.InvalidatedAtUtc = now;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RegisterCompleteTeamAsync(int tournamentId, int entryId, int representativeUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var entry = await db.TournamentEntries.Include(x => x.Tournament).ThenInclude(x => x.Entries).Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.TournamentId == tournamentId);
        if (entry is null) return ServiceResult.Failure("找不到隊伍。");
        if (!entry.Members.Any(x => x.UserId == representativeUserId && x.IsRepresentative)) return ServiceResult.Failure("只有隊伍代表人可以正式報名。");
        if (entry.Tournament.Status != TournamentStatus.RegistrationOpen || entry.Tournament.RegistrationStage != TournamentRegistrationStage.Open || entry.Status != TournamentEntryStatus.Pending)
            return ServiceResult.Failure("目前不能正式報名。");
        if (entry.Members.Count != entry.Tournament.TeamSize) return ServiceResult.Failure("所有隊員接受邀請後才能正式報名。");
        var activeCount = entry.Tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered);
        if (activeCount >= entry.Tournament.TargetEntryCount) return ServiceResult.Failure("報名名額已滿。");

        var now = DateTime.UtcNow;
        entry.RegistrationNumber = await CreateUniqueRegistrationNumberAsync(tournamentId);
        entry.DisplayNameSnapshot = !string.IsNullOrWhiteSpace(entry.TeamName)
            ? entry.TeamName
            : string.Join(" / ", entry.Members.OrderBy(x => x.MemberOrder).Select(x => x.DisplayNameSnapshot));
        entry.Status = TournamentEntryStatus.Registered;
        entry.RegisteredAtUtc = now;
        entry.UpdatedAtUtc = now;
        var capacityReached = activeCount + 1 == entry.Tournament.TargetEntryCount;
        entry.Tournament.RegistrationStage = capacityReached
            ? TournamentRegistrationStage.CapacityReached
            : TournamentRegistrationStage.Open;
        entry.Tournament.UpdatedAtUtc = now;
        entry.Tournament.Version = Guid.NewGuid().ToByteArray();
        if (capacityReached)
            await InvalidatePendingInvitationsAsync(tournamentId, now);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("報名狀態剛被其他操作更新，請重新整理後再試。");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("整隊報名發生衝突，請重新整理後再試。");
        }
    }

    public async Task<ServiceResult> TransferRepresentativeAsync(int tournamentId, int entryId, int currentRepresentativeId, int newRepresentativeId)
    {
        var entry = await db.TournamentEntries.Include(x => x.Tournament).Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.TournamentId == tournamentId && x.Status != TournamentEntryStatus.Withdrawn);
        if (entry is null) return ServiceResult.Failure("找不到隊伍。");
        if (entry.Tournament.Status != TournamentStatus.RegistrationOpen) return ServiceResult.Failure("比賽開始後不能轉讓代表人。");
        var current = entry.Members.SingleOrDefault(x => x.UserId == currentRepresentativeId && x.IsRepresentative);
        var replacement = entry.Members.SingleOrDefault(x => x.UserId == newRepresentativeId);
        if (current is null) return ServiceResult.Failure("只有目前代表人可以轉讓。");
        if (replacement is null || replacement.UserId == current.UserId) return ServiceResult.Failure("新代表人必須是現有其他隊員。");
        if (await db.TournamentInvitations.AnyAsync(x => x.TournamentEntryId == entryId &&
            x.Type == TournamentInvitationType.RepresentativeTransfer && x.Status == TournamentInvitationStatus.Pending))
            return ServiceResult.Failure("已有待處理的代表人轉讓。");
        db.TournamentInvitations.Add(new TournamentInvitation
        {
            TournamentId = tournamentId,
            TournamentEntryId = entryId,
            InvitedUserId = replacement.UserId,
            InvitedByUserId = current.UserId,
            Type = TournamentInvitationType.RepresentativeTransfer,
            Status = TournamentInvitationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RespondToRepresentativeTransferAsync(int invitationId, int invitedUserId, bool accept)
    {
        var invitation = await db.TournamentInvitations.Include(x => x.Tournament)
            .Include(x => x.TournamentEntry).ThenInclude(x => x!.Members)
            .SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null || invitation.InvitedUserId != invitedUserId) return ServiceResult.Failure("找不到你的代表人轉讓邀請。");
        if (invitation.Type != TournamentInvitationType.RepresentativeTransfer || invitation.Status != TournamentInvitationStatus.Pending)
            return ServiceResult.Failure("代表人轉讓已處理或失效。");
        if (invitation.Tournament.Status != TournamentStatus.RegistrationOpen || invitation.TournamentEntry is null)
            return ServiceResult.Failure("目前不能處理代表人轉讓。");
        var current = invitation.TournamentEntry.Members.SingleOrDefault(x => x.UserId == invitation.InvitedByUserId && x.IsRepresentative);
        var replacement = invitation.TournamentEntry.Members.SingleOrDefault(x => x.UserId == invitedUserId);
        if (current is null || replacement is null) return ServiceResult.Failure("隊伍成員狀態已改變，無法完成轉讓。");

        invitation.Status = accept ? TournamentInvitationStatus.Accepted : TournamentInvitationStatus.Declined;
        invitation.RespondedAtUtc = DateTime.UtcNow;
        if (accept)
        {
            current.IsRepresentative = false;
            replacement.IsRepresentative = true;
            invitation.TournamentEntry.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> LeaveTeamAsync(int tournamentId, int userId)
    {
        var entry = await db.TournamentEntries.Include(x => x.Tournament).ThenInclude(x => x.Entries).Include(x => x.Tournament).ThenInclude(x => x.Matches).Include(x => x.Members)
            .Include(x => x.Tournament).ThenInclude(x => x.Invitations)
            .SingleOrDefaultAsync(x => x.TournamentId == tournamentId && x.Status != TournamentEntryStatus.Withdrawn && x.Members.Any(m => m.UserId == userId));
        if (entry is null) return ServiceResult.Failure("找不到你的隊伍。");
        if (entry.Tournament.Status != TournamentStatus.RegistrationOpen) return ServiceResult.Failure("比賽開始後退出將使用棄權流程。");
        var member = entry.Members.Single(x => x.UserId == userId);
        if (member.IsRepresentative && entry.Members.Count > 1) return ServiceResult.Failure("代表人須先轉讓給其他隊員才能退出。");

        var now = DateTime.UtcNow;
        db.TournamentEntryMembers.Remove(member);
        if (entry.Tournament.RegistrationStage == TournamentRegistrationStage.ScheduleDraftCreated)
        {
            await ClearScheduleAsync(entry.Tournament);
            entry.Tournament.RegistrationStage = TournamentRegistrationStage.Closed;
        }
        if (entry.Members.Count == 1)
        {
            entry.Status = TournamentEntryStatus.Withdrawn;
            foreach (var invitation in entry.Tournament.Invitations.Where(x => x.TournamentEntryId == entry.Id && x.Status == TournamentInvitationStatus.Pending))
            {
                invitation.Status = TournamentInvitationStatus.Invalidated;
                invitation.InvalidatedAtUtc = now;
            }
        }
        else if (entry.Status == TournamentEntryStatus.Registered)
        {
            entry.Status = TournamentEntryStatus.Pending;
            entry.RegistrationNumber = null;
            entry.RegisteredAtUtc = null;
            if (entry.Tournament.RegistrationStage == TournamentRegistrationStage.CapacityReached)
                entry.Tournament.RegistrationStage = TournamentRegistrationStage.Open;
        }
        entry.UpdatedAtUtc = now;
        entry.Tournament.UpdatedAtUtc = now;
        entry.Tournament.Version = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    private static TournamentSchedule CreateInitialSchedule(TournamentFormat format, IReadOnlyList<int> entryIds, int randomSeed)
    {
        if (format != TournamentFormat.Swiss)
            return TournamentScheduleGenerator.Generate(format, entryIds, randomSeed);
        var standings = entryIds.Select(id => new SwissEntryStanding(id, 0, new HashSet<int>(), false)).ToList();
        var firstRound = SwissPairingGenerator.GenerateRound(standings, 1, randomSeed);
        var matches = firstRound.Pairings.Select((pair, index) => new TournamentMatchDefinition(
            index + 1,
            TournamentBracket.Swiss,
            1,
            index + 1,
            index + 1,
            TournamentParticipantSource.Entry(pair.EntryAId),
            TournamentParticipantSource.Entry(pair.EntryBId))).ToList();
        var byes = firstRound.ByeEntryId is int byeEntryId
            ? new[] { new TournamentByeDefinition(TournamentBracket.Swiss, 1, 1, TournamentParticipantSource.Entry(byeEntryId)) }
            : [];
        return new TournamentSchedule(format, entryIds, matches, byes);
    }

    private static void ApplyPersistedSource(
        TournamentParticipantSource source,
        IReadOnlyDictionary<int, TournamentMatch> persistedByDefinitionId,
        TournamentMatch destination,
        bool isSideA)
    {
        if (source.Kind == TournamentParticipantSourceKind.Entry) return;
        var sourceMatch = persistedByDefinitionId[source.ReferenceId];
        if (isSideA)
            destination.SideASourceReferenceId = sourceMatch.Id;
        else
            destination.SideBSourceReferenceId = sourceMatch.Id;
        if (source.Kind == TournamentParticipantSourceKind.MatchWinner)
            sourceMatch.WinnerToMatchId = destination.Id;
        else
            sourceMatch.LoserToMatchId = destination.Id;
    }

    private async Task ClearScheduleAsync(Tournament tournament)
    {
        var matchIds = tournament.Matches.Select(x => x.Id).ToList();
        if (await db.Battles.AnyAsync(x => x.TournamentMatchId.HasValue && matchIds.Contains(x.TournamentMatchId.Value)))
            throw new InvalidOperationException("已建立 Battle 的賽程不能作為草稿清除。");
        foreach (var match in tournament.Matches)
        {
            match.WinnerToMatchId = null;
            match.LoserToMatchId = null;
        }
        await db.SaveChangesAsync();
        db.TournamentMatches.RemoveRange(tournament.Matches);
        foreach (var entry in tournament.Entries) entry.SchedulePosition = null;
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateUniqueRegistrationNumberAsync(int tournamentId)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            if (!await db.TournamentEntries.AnyAsync(x => x.TournamentId == tournamentId && x.RegistrationNumber == candidate))
                return candidate;
        }
        throw new InvalidOperationException("無法產生唯一參賽編號。");
    }

    private async Task InvalidatePendingTournamentInvitationsAsync(
        int tournamentId,
        DateTime invalidatedAtUtc,
        int? invitedUserId = null,
        int? exceptInvitationId = null)
    {
        var query = db.TournamentInvitations.Where(x =>
            x.TournamentId == tournamentId &&
            x.Type == TournamentInvitationType.Tournament &&
            x.Status == TournamentInvitationStatus.Pending);
        if (invitedUserId is int userId)
            query = query.Where(x => x.InvitedUserId == userId);
        if (exceptInvitationId is int invitationId)
            query = query.Where(x => x.Id != invitationId);
        foreach (var invitation in await query.ToListAsync())
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = invalidatedAtUtc;
        }
    }

    private async Task InvalidatePendingInvitationsAsync(int tournamentId, DateTime invalidatedAtUtc)
    {
        var invitations = await db.TournamentInvitations.Where(x =>
            x.TournamentId == tournamentId &&
            x.Status == TournamentInvitationStatus.Pending).ToListAsync();
        foreach (var invitation in invitations)
        {
            invitation.Status = TournamentInvitationStatus.Invalidated;
            invitation.InvalidatedAtUtc = invalidatedAtUtc;
        }
    }

    private static TournamentRegistrationStage GetReopenedRegistrationStage(Tournament tournament)
    {
        if (tournament.RegistrationMode == TournamentRegistrationMode.SystemAssignedTeam)
        {
            var activePlayers = tournament.Entries
                .Where(x => x.Status != TournamentEntryStatus.Withdrawn)
                .Sum(x => x.Members.Count);
            var playerCapacity = tournament.TargetEntryCount * tournament.TeamSize!.Value;
            return activePlayers >= playerCapacity
                ? TournamentRegistrationStage.CapacityReached
                : TournamentRegistrationStage.Open;
        }

        var activeEntries = tournament.Entries.Count(x => x.Status == TournamentEntryStatus.Registered);
        return activeEntries >= tournament.TargetEntryCount
            ? TournamentRegistrationStage.CapacityReached
            : TournamentRegistrationStage.Open;
    }
}
