using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public sealed record NotificationDraft(
    int UserId,
    UserNotificationKind Kind,
    string Title,
    string Message,
    string TargetUrl,
    string? EntityType = null,
    int? EntityId = null,
    UserNotificationActionType ActionType = UserNotificationActionType.None,
    int? ActionEntityId = null,
    string? DedupeKey = null);

public sealed class NotificationService(AppDbContext db, IRealtimePublisher realtimePublisher)
{
    public async Task<UserNotification> QueueAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        if (!IsLocalTarget(draft.TargetUrl)) throw new ArgumentException("通知目標必須是站內路徑。", nameof(draft));
        if (!string.IsNullOrWhiteSpace(draft.DedupeKey))
        {
            var existing = await db.UserNotifications.SingleOrDefaultAsync(x =>
                x.UserId == draft.UserId && x.DedupeKey == draft.DedupeKey && x.ResolvedAtUtc == null,
                cancellationToken);
            if (existing is not null) return existing;
        }

        var notification = new UserNotification
        {
            UserId = draft.UserId,
            Kind = draft.Kind,
            Title = draft.Title.Trim(),
            Message = draft.Message.Trim(),
            TargetUrl = draft.TargetUrl,
            EntityType = draft.EntityType,
            EntityId = draft.EntityId,
            ActionType = draft.ActionType,
            ActionEntityId = draft.ActionEntityId,
            DedupeKey = string.IsNullOrWhiteSpace(draft.DedupeKey) ? null : draft.DedupeKey.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.UserNotifications.Add(notification);
        return notification;
    }

    public async Task PublishQueuedAsync(UserNotification notification, CancellationToken cancellationToken = default) =>
        await realtimePublisher.PublishUserAsync(notification.UserId, "notification", new
        {
            notification.Id,
            notification.Title,
            notification.Message,
            notification.TargetUrl,
            actionType = notification.ActionType.ToString(),
            notification.ActionEntityId,
            notification.CreatedAtUtc
        }, cancellationToken);

    public Task<List<UserNotification>> GetLatestAsync(int userId, int take = 50, CancellationToken cancellationToken = default) =>
        db.UserNotifications.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);

    public Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default) =>
        db.UserNotifications.CountAsync(x => x.UserId == userId && x.ReadAtUtc == null, cancellationToken);

    public async Task<ServiceResult> MarkReadAsync(int notificationId, int userId, bool resolve = false)
    {
        var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
        if (notification is null) return ServiceResult.Failure("找不到通知。");
        var now = DateTime.UtcNow;
        notification.ReadAtUtc ??= now;
        if (resolve) notification.ResolvedAtUtc ??= now;
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task MarkAllReadAsync(int userId)
    {
        await db.UserNotifications.Where(x => x.UserId == userId && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReadAtUtc, DateTime.UtcNow));
    }

    public async Task ResolveByDedupeKeyAsync(int userId, string dedupeKey, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await db.UserNotifications
            .Where(x => x.UserId == userId && x.DedupeKey == dedupeKey && x.ResolvedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ReadAtUtc, x => x.ReadAtUtc ?? now)
                .SetProperty(x => x.ResolvedAtUtc, now), cancellationToken);
    }

    private static bool IsLocalTarget(string targetUrl) =>
        targetUrl.StartsWith('/') && !targetUrl.StartsWith("//", StringComparison.Ordinal);
}
