using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Tournaments;

[Authorize]
public class IndexModel(TournamentService tournamentService) : PageModel
{
    [BindProperty(SupportsGet = true)] public TournamentListFilter Filter { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public TournamentListPage Result { get; private set; } = new([], 1, 1, 0);

    public async Task OnGetAsync() =>
        Result = await tournamentService.GetListAsync(User.GetRequiredUserId(), Filter, PageNumber);
}
