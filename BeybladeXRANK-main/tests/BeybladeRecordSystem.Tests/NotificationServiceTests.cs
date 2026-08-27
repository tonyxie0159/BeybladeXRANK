using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Realtime;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task Queue_DeduplicatesUnresolvedNotification_AndPreservesOwnershipBoundary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = await AddUserAsync(db, "notify-owner");
        var outsider = await AddUserAsync(db, "notify-outsider");
        var publisher = new RecordingRealtimePublisher();
        var service = new NotificationService(db, publisher);
        var draft = new NotificationDraft(
            owner.Id,
            UserNotificationKind.Invitation,
            "收到邀請",
            "請處理邀請",
            "/Notifications",
            DedupeKey: "invitation:42");

        var first = await service.QueueAsync(draft);
        await db.SaveChangesAsync();
        var repeated = await service.QueueAsync(draft);
        await service.PublishQueuedAsync(first);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(1, await db.UserNotifications.CountAsync());
        Assert.Equal(1, await service.GetUnreadCountAsync(owner.Id));
        Assert.False((await service.MarkReadAsync(first.Id, outsider.Id, true)).Succeeded);
        Assert.Equal(1, await service.GetUnreadCountAsync(owner.Id));
        Assert.True((await service.MarkReadAsync(first.Id, owner.Id, true)).Succeeded);
        Assert.Equal(0, await service.GetUnreadCountAsync(owner.Id));
        Assert.Single(publisher.Events);
        Assert.Equal(owner.Id, publisher.Events[0].UserId);
    }

    [Fact]
    public async Task Queue_RejectsExternalOrProtocolRelativeTargets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new NotificationService(db, new RecordingRealtimePublisher());

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueueAsync(new NotificationDraft(
            1, UserNotificationKind.Information, "標題", "訊息", "https://example.com")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueueAsync(new NotificationDraft(
            1, UserNotificationKind.Information, "標題", "訊息", "//example.com")));
    }

    [Fact]
    public async Task CancellingTournament_ResolvesInvitations_AndNotifiesInviterOncePerInvitation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var organizer = await AddUserAsync(db, "cancel-organizer");
        var firstInvitee = await AddUserAsync(db, "cancel-invitee-a");
        var secondInvitee = await AddUserAsync(db, "cancel-invitee-b");
        var publisher = new RecordingRealtimePublisher();
        var notificationService = new NotificationService(db, publisher);
        var tournamentService = new TournamentService(db, notificationService, publisher);
        var created = await tournamentService.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "通知測試賽事",
            TournamentRuleSet.IndividualThreeBladeFourPoints,
            TournamentRegistrationMode.Individual,
            TournamentFormat.SingleElimination,
            4,
            null));
        Assert.True(created.Succeeded);
        var tournamentId = created.Value!.Id;
        Assert.True((await tournamentService.InviteParticipantAsync(tournamentId, organizer.Id, firstInvitee.Id)).Succeeded);
        Assert.True((await tournamentService.InviteParticipantAsync(tournamentId, organizer.Id, secondInvitee.Id)).Succeeded);

        Assert.True((await tournamentService.CancelTournamentAsync(tournamentId, organizer.Id, "測試取消")).Succeeded);
        Assert.False((await tournamentService.CancelTournamentAsync(tournamentId, organizer.Id, "重複取消")).Succeeded);

        var invitations = await db.TournamentInvitations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, invitations.Count);
        Assert.All(invitations, x => Assert.Equal(TournamentInvitationStatus.Invalidated, x.Status));
        var inviteeNotifications = await db.UserNotifications.AsNoTracking()
            .Where(x => x.UserId == firstInvitee.Id || x.UserId == secondInvitee.Id).ToListAsync();
        Assert.Equal(2, inviteeNotifications.Count);
        Assert.All(inviteeNotifications, x =>
        {
            Assert.NotNull(x.ReadAtUtc);
            Assert.NotNull(x.ResolvedAtUtc);
        });
        var inviterNotifications = await db.UserNotifications.AsNoTracking()
            .Where(x => x.UserId == organizer.Id && x.Kind == UserNotificationKind.InvitationInvalidated)
            .ToListAsync();
        Assert.Equal(2, inviterNotifications.Count);
        Assert.Equal(4, publisher.Events.Count(x => x.EventType == "notification"));
    }

    private static async Task<User> AddUserAsync(AppDbContext db, string account)
    {
        var user = new User
        {
            Account = account,
            PasswordHash = "hash",
            DisplayName = account,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class RecordingRealtimePublisher : IRealtimePublisher
    {
        public List<(int UserId, string EventType, object Payload)> Events { get; } = [];

        public Task PublishUserAsync(int userId, string eventType, object payload, CancellationToken cancellationToken = default)
        {
            Events.Add((userId, eventType, payload));
            return Task.CompletedTask;
        }

        public Task PublishUsersAsync(IEnumerable<int> userIds, string eventType, object payload, CancellationToken cancellationToken = default)
        {
            Events.AddRange(userIds.Distinct().Select(userId => (userId, eventType, payload)));
            return Task.CompletedTask;
        }
    }
}
