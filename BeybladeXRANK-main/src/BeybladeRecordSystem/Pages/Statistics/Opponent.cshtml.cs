using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Statistics;

[Authorize]
public class OpponentModel(AppDbContext db, StatisticsService statisticsService) : PageModel
{
    public string OpponentDisplayName { get; private set; } = string.Empty;
    public List<OpponentBeybladeStatisticsViewModel> Rows { get; private set; } = [];
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (id == User.GetRequiredUserId()) return NotFound();
        var opponent = await db.Users.FindAsync(id);
        if (opponent is null) return NotFound();
        OpponentDisplayName = opponent.DisplayName;
        Rows = await statisticsService.GetOpponentBeybladeStatisticsAsync(User.GetRequiredUserId(), id);
        return Page();
    }
}
