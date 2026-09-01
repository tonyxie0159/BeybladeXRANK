using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public record QuickBattleInvitationList(
    IReadOnlyList<QuickBattleInvitation> Incoming,
    IReadOnlyList<QuickBattleInvitation> Outgoing);

public enum QuickBattleResumeTarget
{
    Setup,
    Battle,
    Reorder
}

public record QuickBattleResumeItem(
    Battle Battle,
    QuickBattleResumeTarget Target);

public record QuickBattleWorkspace(
    Battle Battle,
    IReadOnlyList<BattleLineupSelection> VisibleSelections,
    IReadOnlyList<BattleLineupSelection> CurrentPrivateSelections,
    IReadOnlyList<Beyblade> AvailableBeyblades,
    bool CurrentUserSubmitted,
    bool CurrentUserConfirmed,
    bool CurrentUserEditRequestUsed);

public record QuickBattleReorderWorkspace(
    Battle Battle,
    IReadOnlyList<BattleLineupSelection> OriginalSelections,
    IReadOnlyList<BattleLineupSelection> CurrentPrivateSelections,
    bool CurrentUserSubmitted);

public class QuickBattleFlowService(
    AppDbContext db,
    NotificationService? notificationService = null,
    IRealtimePublisher? realtimePublisher = null)
{
    public async Task<QuickBattleInvitationList> GetInvitationsAsync(int userId)
    {
        var incoming = await db.QuickBattleInvitations.AsNoTracking()
            .Include(x => x.InviterUser)
            .Where(x => x.InviteeUserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        var outgoing = await db.QuickBattleInvitations.AsNoTracking()
            .Include(x => x.InviteeUser)
            .Where(x => x.InviterUserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        return new QuickBattleInvitationList(incoming, outgoing);
    }

    public async Task<IReadOnlyList<QuickBattleResumeItem>> GetActiveBattlesAsync(int userId)
    {
        var activeStatuses = new[]
        {
            BattleStatus.LineupSelection,
            BattleStatus.LineupReview,
            BattleStatus.LineupLocked,
            BattleStatus.SideSelection,
            BattleStatus.InProgress,
            BattleStatus.ReorderSelection,
            BattleStatus.VictoryPendingCompletion
        };
        var battles = await db.Battles.AsNoTracking()
            .Include(x => x.PlayerA)
            .Include(x => x.PlayerB)
            .Where(x => x.SourceType == BattleSourceType.Quick &&
                (x.PlayerAId == userId || x.PlayerBId == userId) &&
                activeStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        return battles
            .Select(x => new QuickBattleResumeItem(x, GetResumeTarget(x.Status)))
            .ToList();
    }

    public static QuickBattleResumeTarget GetResumeTarget(BattleStatus status) => status switch
    {
        BattleStatus.LineupSelection or BattleStatus.LineupReview or
            BattleStatus.LineupLocked or BattleStatus.SideSelection => QuickBattleResumeTarget.Setup,
        BattleStatus.InProgress or BattleStatus.VictoryPendingCompletion => QuickBattleResumeTarget.Battle,
        BattleStatus.ReorderSelection => QuickBattleResumeTarget.Reorder,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不是可返回的快速對戰狀態。")
    };

    public static string GetBattleTargetUrl(int battleId, BattleStatus status) => status switch
    {
        BattleStatus.LineupSelection or BattleStatus.LineupReview or
            BattleStatus.LineupLocked or BattleStatus.SideSelection or BattleStatus.Draft => $"/Battles/Setup/{battleId}",
        BattleStatus.InProgress or BattleStatus.VictoryPendingCompletion => $"/Battles/Battle/{battleId}",
        BattleStatus.ReorderSelection => $"/Battles/Reorder/{battleId}",
        BattleStatus.Completed or BattleStatus.Forfeited or BattleStatus.Voided => $"/Battles/Details/{battleId}",
        _ => "/Battles/Invitations"
    };

    public async Task<int> GetIncomingInvitationCountAsync(int userId) =>
        await db.QuickBattleInvitations.CountAsync(x => x.InviteeUserId == userId);

    public async Task<ServiceResult<QuickBattleInvitation>> SendInvitationAsync(int inviterUserId, int inviteeUserId)
    {
        if (inviterUserId == inviteeUserId)
            return ServiceResult<QuickBattleInvitation>.Failure("不可邀請自己進行對戰。");
        var inviter = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == inviterUserId);
        var inviteeExists = await db.Users.AnyAsync(x => x.Id == inviteeUserId);
        if (inviter is null || !inviteeExists)
            return ServiceResult<QuickBattleInvitation>.Failure("找不到指定的玩家。");
        if (await db.QuickBattleInvitations.AnyAsync(x =>
                (x.InviterUserId == inviterUserId && x.InviteeUserId == inviteeUserId) ||
                (x.InviterUserId == inviteeUserId && x.InviteeUserId == inviterUserId)))
            return ServiceResult<QuickBattleInvitation>.Failure("雙方已有一筆待處理的快速對戰邀請。");

        var invitation = new QuickBattleInvitation
        {
            InviterUserId = inviterUserId,
            InviteeUserId = inviteeUserId,
            CreatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid().ToByteArray()
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.QuickBattleInvitations.Add(invitation);
        await db.SaveChangesAsync();
        UserNotification? notification = null;
        if (notificationService is not null)
        {
            notification = await notificationService.QueueAsync(new NotificationDraft(
                inviteeUserId,
                UserNotificationKind.Invitation,
                "快速對戰邀請",
                $"{inviter.DisplayName} 邀請你進行快速對戰。",
                "/Battles/Invitations",
                "QuickBattleInvitation",
                invitation.Id,
                UserNotificationActionType.AcceptQuickBattleInvitation,
                invitation.Id,
                $"quick-invitation:{invitation.Id}"));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        if (notification is not null) await notificationService!.PublishQueuedAsync(notification);
        await PublishInvitationStateAsync(inviteeUserId, invitation.Id, "Pending");
        return ServiceResult<QuickBattleInvitation>.Success(invitation);
    }

    public async Task<ServiceResult<int>> AcceptInvitationAsync(int invitationId, int inviteeUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invitation = await db.QuickBattleInvitations.Include(x => x.InviteeUser)
            .SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null)
            return ServiceResult<int>.Failure("邀請已不存在或已被處理。");
        if (invitation.InviteeUserId != inviteeUserId)
            return ServiceResult<int>.Failure("只有受邀玩家可以接受這筆邀請。");

        var battle = new Battle
        {
            SourceType = BattleSourceType.Quick,
            ScoreToWin = 4,
            PlayerAId = invitation.InviterUserId,
            PlayerBId = invitation.InviteeUserId,
            CreatedByUserId = invitation.InviterUserId,
            Status = BattleStatus.LineupSelection,
            LineupSequenceNo = 1,
            SideADesignation = null,
            CreatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid().ToByteArray()
        };
        db.Battles.Add(battle);
        db.QuickBattleInvitations.Remove(invitation);
        await db.SaveChangesAsync();
        UserNotification? outcome = null;
        if (notificationService is not null)
        {
            await notificationService.ResolveByDedupeKeyAsync(inviteeUserId, $"quick-invitation:{invitation.Id}");
            outcome = await notificationService.QueueAsync(new NotificationDraft(
                invitation.InviterUserId,
                UserNotificationKind.InvitationAccepted,
                "快速對戰邀請已接受",
                $"{invitation.InviteeUser.DisplayName} 已接受邀請，請提交陣容。",
                $"/Battles/Setup/{battle.Id}",
                "Battle",
                battle.Id,
                DedupeKey: $"quick-accepted:{invitation.Id}"));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        if (outcome is not null) await notificationService!.PublishQueuedAsync(outcome);
        await PublishInvitationStateAsync(invitation.InviterUserId, invitation.Id, "Accepted");
        await PublishBattleStateAsync(battle);
        return ServiceResult<int>.Success(battle.Id);
    }

    public async Task<ServiceResult> DeclineInvitationAsync(int invitationId, int inviteeUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invitation = await db.QuickBattleInvitations.Include(x => x.InviteeUser).SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null) return ServiceResult.Success();
        if (invitation.InviteeUserId != inviteeUserId)
            return ServiceResult.Failure("只有受邀玩家可以拒絕這筆邀請。");
        db.QuickBattleInvitations.Remove(invitation);
        await db.SaveChangesAsync();
        UserNotification? outcome = null;
        if (notificationService is not null)
        {
            await notificationService.ResolveByDedupeKeyAsync(inviteeUserId, $"quick-invitation:{invitation.Id}");
            outcome = await notificationService.QueueAsync(new NotificationDraft(
                invitation.InviterUserId,
                UserNotificationKind.InvitationDeclined,
                "快速對戰邀請被拒絕",
                $"{invitation.InviteeUser.DisplayName} 已拒絕你的快速對戰邀請。",
                "/Battles/Invitations",
                "QuickBattleInvitation",
                invitation.Id,
                DedupeKey: $"quick-declined:{invitation.Id}"));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        if (outcome is not null) await notificationService!.PublishQueuedAsync(outcome);
        await PublishInvitationStateAsync(invitation.InviterUserId, invitation.Id, "Declined");
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> WithdrawInvitationAsync(int invitationId, int inviterUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invitation = await db.QuickBattleInvitations.Include(x => x.InviterUser).SingleOrDefaultAsync(x => x.Id == invitationId);
        if (invitation is null) return ServiceResult.Success();
        if (invitation.InviterUserId != inviterUserId)
            return ServiceResult.Failure("只有邀請發起人可以撤回這筆邀請。");
        db.QuickBattleInvitations.Remove(invitation);
        await db.SaveChangesAsync();
        UserNotification? outcome = null;
        if (notificationService is not null)
        {
            await notificationService.ResolveByDedupeKeyAsync(invitation.InviteeUserId, $"quick-invitation:{invitation.Id}");
            outcome = await notificationService.QueueAsync(new NotificationDraft(
                invitation.InviteeUserId,
                UserNotificationKind.InvitationCancelled,
                "快速對戰邀請已撤回",
                $"{invitation.InviterUser.DisplayName} 已撤回快速對戰邀請。",
                "/Battles/Invitations",
                "QuickBattleInvitation",
                invitation.Id,
                DedupeKey: $"quick-withdrawn:{invitation.Id}"));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        if (outcome is not null) await notificationService!.PublishQueuedAsync(outcome);
        await PublishInvitationStateAsync(invitation.InviteeUserId, invitation.Id, "Withdrawn");
        return ServiceResult.Success();
    }

    public async Task<QuickBattleWorkspace?> GetWorkspaceAsync(int battleId, int userId)
    {
        var battle = await QuickBattleQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null || !IsParticipant(battle, userId)) return null;

        var currentSelections = battle.LineupSelections
            .Where(x => x.SequenceNo == battle.LineupSequenceNo)
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.PositionNo)
            .ToList();
        var isPrivate = battle.Status == BattleStatus.LineupSelection;
        var privateSelections = isPrivate
            ? currentSelections.Where(x => x.UserId == userId).ToList()
            : [];
        var visibleSelections = isPrivate ? [] : currentSelections;
        var available = battle.Status == BattleStatus.LineupSelection
            ? await db.Beyblades.AsNoTracking().Where(x => x.UserId == userId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync()
            : [];
        return new QuickBattleWorkspace(
            battle,
            visibleSelections,
            privateSelections,
            available,
            currentSelections.Count(x => x.UserId == userId) == 3,
            IsConfirmed(battle, userId),
            IsEditRequestUsed(battle, userId));
    }

    public async Task<ServiceResult> SubmitLineupAsync(int battleId, int userId, IReadOnlyList<int> orderedBladeIds)
    {
        if (orderedBladeIds.Count != 3 || orderedBladeIds.Any(x => x <= 0) || orderedBladeIds.Distinct().Count() != 3)
            return ServiceResult.Failure("必須依序選擇三顆不同的陀螺。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await QuickBattleQuery().SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到快速對戰。");
        if (!IsParticipant(battle, userId)) return ServiceResult.Failure("你不是這場快速對戰的玩家。");
        if (battle.Status != BattleStatus.LineupSelection)
            return ServiceResult.Failure("目前不是陣容密封提交階段。");

        var existing = battle.LineupSelections
            .Where(x => x.SequenceNo == battle.LineupSequenceNo && x.UserId == userId)
            .OrderBy(x => x.PositionNo)
            .ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.BeybladeId).SequenceEqual(orderedBladeIds)
                ? ServiceResult.Success()
                : ServiceResult.Failure("本版陣容已提交，等待雙方公開前不能更換。");

        var blades = await db.Beyblades
            .Where(x => orderedBladeIds.Contains(x.Id) && x.UserId == userId && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id);
        if (blades.Count != 3) return ServiceResult.Failure("所選陀螺必須屬於你且尚未刪除。");
        var displayName = userId == battle.PlayerAId ? battle.PlayerA.DisplayName : battle.PlayerB.DisplayName;
        var now = DateTime.UtcNow;
        for (var index = 0; index < orderedBladeIds.Count; index++)
        {
            var blade = blades[orderedBladeIds[index]];
            battle.LineupSelections.Add(new BattleLineupSelection
            {
                SequenceNo = battle.LineupSequenceNo,
                UserId = userId,
                PositionNo = index + 1,
                BeybladeId = blade.Id,
                PlayerDisplayNameSnapshot = displayName,
                BeybladeNameSnapshot = blade.Name,
                SubmittedAtUtc = now
            });
        }
        await db.SaveChangesAsync();

        var sequenceSelections = battle.LineupSelections.Where(x => x.SequenceNo == battle.LineupSequenceNo).ToList();
        if (sequenceSelections.Count(x => x.UserId == battle.PlayerAId) == 3 &&
            sequenceSelections.Count(x => x.UserId == battle.PlayerBId) == 3)
        {
            battle.Status = BattleStatus.LineupReview;
            battle.PlayerALineupConfirmed = false;
            battle.PlayerBLineupConfirmed = false;
            battle.PendingLineupEditRequestedByUserId = null;
            battle.Version = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ConfirmLineupAsync(int battleId, int userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await db.Battles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId && x.SourceType == BattleSourceType.Quick);
        if (battle is null) return ServiceResult.Failure("找不到快速對戰。");
        if (!IsParticipant(battle, userId)) return ServiceResult.Failure("你不是這場快速對戰的玩家。");
        if (battle.Status != BattleStatus.LineupReview)
            return ServiceResult.Failure("目前不能確認陣容。");
        if (battle.PendingLineupEditRequestedByUserId is not null)
            return ServiceResult.Failure("請先處理待回覆的重新編輯請求。");

        var confirmed = userId == battle.PlayerAId
            ? await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PlayerALineupConfirmed, true)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()))
            : await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PlayerBLineupConfirmed, true)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()));
        if (confirmed != 1) return ServiceResult.Failure("陣容狀態已更新，請重新整理後再操作。");

        db.ChangeTracker.Clear();
        battle = await QuickBattleQuery().SingleAsync(x => x.Id == battleId);
        if (battle.PlayerALineupConfirmed && battle.PlayerBLineupConfirmed)
        {
            var materialized = MaterializeLineup(battle);
            if (!materialized.Succeeded) return materialized;
            battle.Status = BattleStatus.LineupLocked;
            battle.Version = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RequestLineupEditAsync(int battleId, int userId)
    {
        var battle = await db.Battles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId && x.SourceType == BattleSourceType.Quick);
        if (battle is null) return ServiceResult.Failure("找不到快速對戰。");
        if (!IsParticipant(battle, userId)) return ServiceResult.Failure("你不是這場快速對戰的玩家。");
        if (battle.Status != BattleStatus.LineupReview)
            return ServiceResult.Failure("目前不能要求重新編輯陣容。");
        if (IsConfirmed(battle, userId))
            return ServiceResult.Failure("你已確認本版陣容，不能再提出重新編輯。");
        if (battle.PendingLineupEditRequestedByUserId is not null)
            return ServiceResult.Failure("已有一筆重新編輯請求等待回覆。");
        if (IsEditRequestUsed(battle, userId))
            return ServiceResult.Failure("你已使用本版唯一一次重新編輯請求。");

        var updated = userId == battle.PlayerAId
            ? await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == null && !x.PlayerALineupConfirmed && !x.PlayerAEditRequestUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PlayerAEditRequestUsed, true)
                    .SetProperty(x => x.PendingLineupEditRequestedByUserId, userId)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()))
            : await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == null && !x.PlayerBLineupConfirmed && !x.PlayerBEditRequestUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PlayerBEditRequestUsed, true)
                    .SetProperty(x => x.PendingLineupEditRequestedByUserId, userId)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()));
        if (updated != 1) return ServiceResult.Failure("陣容狀態已更新，請重新整理後再操作。");
        await PublishBattleStateByIdAsync(battleId);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RespondLineupEditAsync(int battleId, int userId, bool accept)
    {
        var battle = await db.Battles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId && x.SourceType == BattleSourceType.Quick);
        if (battle is null) return ServiceResult.Failure("找不到快速對戰。");
        if (!IsParticipant(battle, userId)) return ServiceResult.Failure("你不是這場快速對戰的玩家。");
        if (battle.Status != BattleStatus.LineupReview || battle.PendingLineupEditRequestedByUserId is null)
            return ServiceResult.Failure("目前沒有待回覆的重新編輯請求。");
        if (battle.PendingLineupEditRequestedByUserId == userId)
            return ServiceResult.Failure("請求者不能回覆自己的重新編輯請求。");

        var pendingRequester = battle.PendingLineupEditRequestedByUserId.Value;
        var updated = accept
            ? await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == pendingRequester)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LineupSequenceNo, x => x.LineupSequenceNo + 1)
                    .SetProperty(x => x.Status, BattleStatus.LineupSelection)
                    .SetProperty(x => x.PlayerALineupConfirmed, false)
                    .SetProperty(x => x.PlayerBLineupConfirmed, false)
                    .SetProperty(x => x.PlayerAEditRequestUsed, false)
                    .SetProperty(x => x.PlayerBEditRequestUsed, false)
                    .SetProperty(x => x.PendingLineupEditRequestedByUserId, (int?)null)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()))
            : await db.Battles.Where(x => x.Id == battleId && x.Status == BattleStatus.LineupReview &&
                    x.PendingLineupEditRequestedByUserId == pendingRequester)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PendingLineupEditRequestedByUserId, (int?)null)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()));
        if (updated != 1) return ServiceResult.Failure("重新編輯請求已被處理，請重新整理。");
        await PublishBattleStateByIdAsync(battleId);
        return ServiceResult.Success();
    }

    public async Task<QuickBattleReorderWorkspace?> GetReorderWorkspaceAsync(int battleId, int userId)
    {
        var battle = await QuickBattleQuery().Include(x => x.Rounds).AsNoTracking().SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null || !IsParticipant(battle, userId) || battle.Status != BattleStatus.ReorderSelection)
            return null;
        var currentSequence = battle.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().SingleOrDefault();
        var pendingSequence = currentSequence + 1;
        var originals = GetOriginalSelections(battle, userId);
        var privateSelections = battle.LineupSelections
            .Where(x => x.SequenceNo == pendingSequence && x.UserId == userId)
            .OrderBy(x => x.PositionNo)
            .ToList();
        return new QuickBattleReorderWorkspace(battle, originals, privateSelections, privateSelections.Count == 3);
    }

    public async Task<ServiceResult> SubmitReorderAsync(int battleId, int userId, IReadOnlyList<int> orderedBladeIds)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var battle = await QuickBattleQuery().Include(x => x.Rounds).SingleOrDefaultAsync(x => x.Id == battleId);
        if (battle is null) return ServiceResult.Failure("找不到快速對戰。");
        if (!IsParticipant(battle, userId)) return ServiceResult.Failure("你不是這場快速對戰的玩家。");
        if (battle.Status != BattleStatus.ReorderSelection)
            return ServiceResult.Failure("目前不是密封重排階段。");

        var currentSequence = battle.Lineups.Where(x => x.IsCurrent).Select(x => x.SequenceNo).Distinct().SingleOrDefault();
        var currentLineupIds = battle.Lineups.Where(x => x.IsCurrent).Select(x => x.Id).ToHashSet();
        var completedCurrentRounds = battle.Rounds.Count(x => currentLineupIds.Contains(x.LineupId) && x.Status == BattleRoundStatus.Completed);
        if (currentSequence <= 0 || currentLineupIds.Count != 3 || completedCurrentRounds != 3)
            return ServiceResult.Failure("目前三個順位的 Round 都完成後才能重排。");

        var originals = GetOriginalSelections(battle, userId);
        if (orderedBladeIds.Count != 3 || orderedBladeIds.Distinct().Count() != 3 ||
            !orderedBladeIds.Order().SequenceEqual(originals.Select(x => x.BeybladeId).Order()))
            return ServiceResult.Failure("重排只能使用本場最初鎖定、且屬於自己的三顆陀螺。");
        var pendingSequence = currentSequence + 1;
        var existing = battle.LineupSelections
            .Where(x => x.SequenceNo == pendingSequence && x.UserId == userId)
            .OrderBy(x => x.PositionNo)
            .ToList();
        if (existing.Count > 0)
            return existing.Select(x => x.BeybladeId).SequenceEqual(orderedBladeIds)
                ? ServiceResult.Success()
                : ServiceResult.Failure("這一組重排已密封提交，不能再次更換。");

        var now = DateTime.UtcNow;
        for (var index = 0; index < orderedBladeIds.Count; index++)
        {
            var snapshot = originals.Single(x => x.BeybladeId == orderedBladeIds[index]);
            battle.LineupSelections.Add(new BattleLineupSelection
            {
                SequenceNo = pendingSequence,
                UserId = userId,
                PositionNo = index + 1,
                BeybladeId = snapshot.BeybladeId,
                PlayerDisplayNameSnapshot = snapshot.PlayerDisplayNameSnapshot,
                BeybladeNameSnapshot = snapshot.BeybladeNameSnapshot,
                SubmittedAtUtc = now
            });
        }
        await db.SaveChangesAsync();

        var pending = await db.BattleLineupSelections
            .Where(x => x.BattleId == battleId && x.SequenceNo == pendingSequence)
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.PositionNo)
            .ToListAsync();
        if (pending.Count(x => x.UserId == battle.PlayerAId) == 3 && pending.Count(x => x.UserId == battle.PlayerBId) == 3)
        {
            var playerA = pending.Where(x => x.UserId == battle.PlayerAId).OrderBy(x => x.PositionNo).ToList();
            var playerB = pending.Where(x => x.UserId == battle.PlayerBId).OrderBy(x => x.PositionNo).ToList();
            foreach (var current in battle.Lineups.Where(x => x.IsCurrent)) current.IsCurrent = false;
            var newLineup = new List<BattleLineup>();
            for (var index = 0; index < 3; index++)
            {
                newLineup.Add(new BattleLineup
                {
                    BattleId = battle.Id,
                    SequenceNo = pendingSequence,
                    PositionNo = index + 1,
                    PlayerAId = battle.PlayerAId!.Value,
                    PlayerADisplayNameSnapshot = playerA[index].PlayerDisplayNameSnapshot,
                    PlayerABeybladeId = playerA[index].BeybladeId,
                    PlayerABeybladeNameSnapshot = playerA[index].BeybladeNameSnapshot,
                    PlayerBId = battle.PlayerBId!.Value,
                    PlayerBDisplayNameSnapshot = playerB[index].PlayerDisplayNameSnapshot,
                    PlayerBBeybladeId = playerB[index].BeybladeId,
                    PlayerBBeybladeNameSnapshot = playerB[index].BeybladeNameSnapshot,
                    IsCurrent = true
                });
            }
            db.BattleLineups.AddRange(newLineup);
            await db.SaveChangesAsync();
            db.BattleRounds.Add(CreateRound(battle, newLineup[0], battle.Rounds.Max(x => x.RoundNo) + 1));
            battle.LineupSequenceNo = pendingSequence;
            battle.Status = BattleStatus.InProgress;
            battle.Version = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        await PublishBattleStateAsync(battle);
        return ServiceResult.Success();
    }

    private async Task PublishBattleStateByIdAsync(int battleId)
    {
        if (realtimePublisher is null) return;
        var battle = await db.Battles.AsNoTracking().SingleAsync(x => x.Id == battleId);
        await PublishBattleStateAsync(battle);
    }

    private Task PublishInvitationStateAsync(int userId, int invitationId, string status) =>
        realtimePublisher?.PublishUserAsync(userId, "quick-invitation-state", new
        {
            invitationId,
            status,
            targetUrl = "/Battles/Invitations"
        }) ?? Task.CompletedTask;

    private Task PublishBattleStateAsync(Battle battle)
    {
        if (realtimePublisher is null || battle.PlayerAId is null || battle.PlayerBId is null) return Task.CompletedTask;
        var targetUrl = GetBattleTargetUrl(battle.Id, battle.Status);
        return realtimePublisher.PublishUsersAsync(
            [battle.PlayerAId.Value, battle.PlayerBId.Value],
            "battle-state",
            new { battleId = battle.Id, status = battle.Status.ToString(), targetUrl });
    }

    private IQueryable<Battle> QuickBattleQuery() => db.Battles
        .Include(x => x.PlayerA)
        .Include(x => x.PlayerB)
        .Include(x => x.LineupSelections)
        .Include(x => x.Lineups)
        .Where(x => x.SourceType == BattleSourceType.Quick);

    private static bool IsParticipant(Battle battle, int userId) =>
        battle.PlayerAId == userId || battle.PlayerBId == userId;

    private static bool IsConfirmed(Battle battle, int userId) =>
        userId == battle.PlayerAId ? battle.PlayerALineupConfirmed :
        userId == battle.PlayerBId && battle.PlayerBLineupConfirmed;

    private static bool IsEditRequestUsed(Battle battle, int userId) =>
        userId == battle.PlayerAId ? battle.PlayerAEditRequestUsed :
        userId == battle.PlayerBId && battle.PlayerBEditRequestUsed;

    private static List<BattleLineupSelection> GetOriginalSelections(Battle battle, int userId)
    {
        var initialSequence = battle.Lineups.Select(x => x.SequenceNo).DefaultIfEmpty(1).Min();
        var saved = battle.LineupSelections.Where(x => x.SequenceNo == initialSequence && x.UserId == userId).OrderBy(x => x.PositionNo).ToList();
        if (saved.Count == 3) return saved;
        var initial = battle.Lineups.Where(x => x.SequenceNo == initialSequence).OrderBy(x => x.PositionNo).ToList();
        return userId == battle.PlayerAId
            ? initial.Select(x => new BattleLineupSelection
            {
                UserId = userId, PositionNo = x.PositionNo, BeybladeId = x.PlayerABeybladeId,
                PlayerDisplayNameSnapshot = x.PlayerADisplayNameSnapshot, BeybladeNameSnapshot = x.PlayerABeybladeNameSnapshot
            }).ToList()
            : initial.Select(x => new BattleLineupSelection
            {
                UserId = userId, PositionNo = x.PositionNo, BeybladeId = x.PlayerBBeybladeId,
                PlayerDisplayNameSnapshot = x.PlayerBDisplayNameSnapshot, BeybladeNameSnapshot = x.PlayerBBeybladeNameSnapshot
            }).ToList();
    }

    private static BattleRound CreateRound(Battle battle, BattleLineup lineup, int roundNo) => new()
    {
        BattleId = battle.Id,
        LineupId = lineup.Id,
        RoundNo = roundNo,
        PositionNo = lineup.PositionNo,
        PlayerAId = lineup.PlayerAId,
        PlayerADisplayNameSnapshot = lineup.PlayerADisplayNameSnapshot,
        PlayerABeybladeId = lineup.PlayerABeybladeId,
        PlayerABeybladeNameSnapshot = lineup.PlayerABeybladeNameSnapshot,
        PlayerBId = lineup.PlayerBId,
        PlayerBDisplayNameSnapshot = lineup.PlayerBDisplayNameSnapshot,
        PlayerBBeybladeId = lineup.PlayerBBeybladeId,
        PlayerBBeybladeNameSnapshot = lineup.PlayerBBeybladeNameSnapshot,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static ServiceResult MaterializeLineup(Battle battle)
    {
        var selections = battle.LineupSelections.Where(x => x.SequenceNo == battle.LineupSequenceNo).ToList();
        var playerA = selections.Where(x => x.UserId == battle.PlayerAId).OrderBy(x => x.PositionNo).ToList();
        var playerB = selections.Where(x => x.UserId == battle.PlayerBId).OrderBy(x => x.PositionNo).ToList();
        if (playerA.Count != 3 || playerB.Count != 3)
            return ServiceResult.Failure("雙方各需三顆已提交的陀螺才能鎖定陣容。");
        if (battle.Lineups.Any(x => x.SequenceNo == battle.LineupSequenceNo))
            return ServiceResult.Success();

        foreach (var previous in battle.Lineups.Where(x => x.IsCurrent)) previous.IsCurrent = false;
        for (var index = 0; index < 3; index++)
        {
            battle.Lineups.Add(new BattleLineup
            {
                SequenceNo = battle.LineupSequenceNo,
                PositionNo = index + 1,
                PlayerAId = battle.PlayerAId!.Value,
                PlayerADisplayNameSnapshot = playerA[index].PlayerDisplayNameSnapshot,
                PlayerABeybladeId = playerA[index].BeybladeId,
                PlayerABeybladeNameSnapshot = playerA[index].BeybladeNameSnapshot,
                PlayerBId = battle.PlayerBId!.Value,
                PlayerBDisplayNameSnapshot = playerB[index].PlayerDisplayNameSnapshot,
                PlayerBBeybladeId = playerB[index].BeybladeId,
                PlayerBBeybladeNameSnapshot = playerB[index].BeybladeNameSnapshot,
                IsCurrent = true
            });
        }
        return ServiceResult.Success();
    }
}
