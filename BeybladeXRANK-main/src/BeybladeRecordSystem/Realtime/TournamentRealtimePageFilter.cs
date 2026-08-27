using BeybladeRecordSystem.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Realtime;

public sealed class TournamentRealtimePageFilter(AppDbContext db, IRealtimePublisher realtimePublisher) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method) ||
            !context.ActionDescriptor.ViewEnginePath.StartsWith("/Tournaments/", StringComparison.Ordinal))
            return;
        if (executed.Exception is not null && !executed.ExceptionHandled) return;

        var rawId = context.RouteData.Values["id"]?.ToString() ?? context.HttpContext.Request.Form["id"].FirstOrDefault();
        if (!int.TryParse(rawId, out var routeId)) return;
        var tournamentId = context.ActionDescriptor.ViewEnginePath == "/Tournaments/Match"
            ? await db.TournamentMatches.AsNoTracking().Where(x => x.Id == routeId).Select(x => (int?)x.TournamentId).SingleOrDefaultAsync()
            : routeId;
        if (tournamentId is null || !await db.Tournaments.AnyAsync(x => x.Id == tournamentId)) return;

        var userIds = await db.TournamentEntryMembers.AsNoTracking().Where(x => x.TournamentId == tournamentId)
            .Select(x => x.UserId).ToListAsync();
        userIds.AddRange(await db.TournamentEntries.AsNoTracking().Where(x => x.TournamentId == tournamentId && x.IndividualUserId != null)
            .Select(x => x.IndividualUserId!.Value).ToListAsync());
        userIds.AddRange(await db.TournamentInvitations.AsNoTracking().Where(x => x.TournamentId == tournamentId)
            .Select(x => x.InvitedUserId).ToListAsync());
        userIds.Add(await db.Tournaments.AsNoTracking().Where(x => x.Id == tournamentId).Select(x => x.OrganizerUserId).SingleAsync());
        await realtimePublisher.PublishUsersAsync(userIds, "tournament-state", new
        {
            tournamentId,
            targetUrl = $"/Tournaments/Details/{tournamentId}"
        });
    }
}
