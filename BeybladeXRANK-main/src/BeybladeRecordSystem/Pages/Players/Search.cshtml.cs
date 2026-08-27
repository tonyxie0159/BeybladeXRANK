using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Pages.Players;

[Authorize]
public sealed class SearchModel(AppDbContext db) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? q, int? tournamentId)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length == 0) return new JsonResult(Array.Empty<object>());
        var currentUserId = User.GetRequiredUserId();
        var query = db.Users.AsNoTracking().Where(x =>
            x.Id != currentUserId && EF.Functions.Like(x.DisplayName, $"%{term}%"));
        if (tournamentId is int id)
        {
            query = query.Where(user =>
                !db.TournamentEntries.Any(entry => entry.TournamentId == id &&
                    entry.Status != TournamentEntryStatus.Withdrawn && entry.IndividualUserId == user.Id) &&
                !db.TournamentEntryMembers.Any(member => member.TournamentId == id &&
                    member.TournamentEntry.Status != TournamentEntryStatus.Withdrawn && member.UserId == user.Id) &&
                !db.TournamentInvitations.Any(invitation => invitation.TournamentId == id &&
                    invitation.InvitedUserId == user.Id && invitation.Status == TournamentInvitationStatus.Pending));
        }
        var results = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Take(10)
            .Select(x => new { userId = x.Id, displayName = x.DisplayName }).ToListAsync();
        return new JsonResult(results);
    }
}
