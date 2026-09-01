using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Pages.Notifications;

[Authorize]
public sealed class IndexModel(
    AppDbContext db,
    NotificationService notificationService,
    QuickBattleFlowService quickBattleFlowService,
    TournamentService tournamentService) : PageModel
{
    public IReadOnlyList<UserNotification> Notifications { get; private set; } = [];

    public async Task OnGetAsync() =>
        Notifications = await notificationService.GetLatestAsync(User.GetRequiredUserId());

    public async Task<IActionResult> OnGetUnreadAsync()
    {
        var userId = User.GetRequiredUserId();
        var items = await db.UserNotifications.AsNoTracking()
            .Where(x => x.UserId == userId && x.ReadAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Message,
                x.TargetUrl,
                actionType = x.ActionType.ToString(),
                x.ActionEntityId,
                x.CreatedAtUtc
            })
            .ToListAsync();
        return new JsonResult(new
        {
            count = await notificationService.GetUnreadCountAsync(userId),
            items
        });
    }

    public async Task<IActionResult> OnGetOpenAsync(int id)
    {
        var userId = User.GetRequiredUserId();
        var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (notification is null) return RedirectToPage();
        await notificationService.MarkReadAsync(id, userId);
        var targetUrl = await ResolveCurrentTargetUrlAsync(notification, userId);
        return LocalRedirect(IsSafeLocalUrl(targetUrl) ? targetUrl : "/Notifications");
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        var result = await notificationService.MarkReadAsync(id, User.GetRequiredUserId());
        return new JsonResult(new { succeeded = result.Succeeded, error = result.Error });
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        await notificationService.MarkAllReadAsync(User.GetRequiredUserId());
        if (Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return new JsonResult(new { succeeded = true });
        TempData["Success"] = "所有通知都已標示為已讀。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id)
    {
        var userId = User.GetRequiredUserId();
        var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (notification is null || notification.ResolvedAtUtc is not null)
            return ActionResult(false, "通知已處理或不存在。", "/Notifications");
        if (notification.ActionEntityId is not int actionEntityId)
            return ActionResult(false, "通知沒有可執行的動作。", notification.TargetUrl);

        ServiceResult result;
        var redirectUrl = notification.TargetUrl;
        switch (notification.ActionType)
        {
            case UserNotificationActionType.AcceptQuickBattleInvitation:
                var quickResult = await quickBattleFlowService.AcceptInvitationAsync(actionEntityId, userId);
                result = quickResult.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(quickResult.Error!);
                if (quickResult.Succeeded) redirectUrl = $"/Battles/Setup/{quickResult.Value}";
                break;
            case UserNotificationActionType.AcceptTournamentInvitation:
                result = await tournamentService.RespondToTournamentInvitationAsync(actionEntityId, userId, true);
                break;
            case UserNotificationActionType.AcceptTeamInvitation:
                result = await tournamentService.RespondToTeamInvitationAsync(actionEntityId, userId, true);
                break;
            case UserNotificationActionType.AcceptRepresentativeTransfer:
                result = await tournamentService.RespondToRepresentativeTransferAsync(actionEntityId, userId, true);
                break;
            default:
                result = ServiceResult.Failure("這筆通知不支援直接接受，請前往處理頁面。");
                break;
        }

        if (result.Succeeded) await notificationService.MarkReadAsync(id, userId, resolve: true);
        return ActionResult(result.Succeeded, result.Error, redirectUrl);
    }

    private IActionResult ActionResult(bool succeeded, string? error, string targetUrl)
    {
        var safeTarget = IsSafeLocalUrl(targetUrl) ? targetUrl : "/Notifications";
        if (Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return new JsonResult(new { succeeded, error, targetUrl = safeTarget });
        TempData[succeeded ? "Success" : "Error"] = succeeded ? "邀請已接受。" : error;
        return LocalRedirect(succeeded ? safeTarget : "/Notifications");
    }

    private async Task<string> ResolveCurrentTargetUrlAsync(UserNotification notification, int userId)
    {
        if (notification.EntityType == "Battle" && notification.EntityId is int battleId)
        {
            var battleStatus = await db.Battles.AsNoTracking()
                .Where(x => x.Id == battleId && (x.PlayerAId == userId || x.PlayerBId == userId))
                .Select(x => (BattleStatus?)x.Status)
                .SingleOrDefaultAsync();
            if (battleStatus is BattleStatus status)
                return QuickBattleFlowService.GetBattleTargetUrl(battleId, status);
        }

        return notification.TargetUrl;
    }

    private static bool IsSafeLocalUrl(string value) => value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal);
}
