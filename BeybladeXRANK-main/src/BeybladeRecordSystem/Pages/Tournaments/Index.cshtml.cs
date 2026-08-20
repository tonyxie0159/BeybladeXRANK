using System.Security.Cryptography;
using System.Text;
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
    public string PollToken { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        Result = await tournamentService.GetListAsync(User.GetRequiredUserId(), Filter, PageNumber);
        PollToken = BuildPollToken(Result);
    }

    public async Task<IActionResult> OnGetPollAsync()
    {
        var result = await tournamentService.GetListAsync(User.GetRequiredUserId(), Filter, PageNumber);
        return new JsonResult(new { token = BuildPollToken(result) });
    }

    private static string BuildPollToken(TournamentListPage result)
    {
        var source = string.Join('|',
            result.PageNumber,
            result.TotalPages,
            result.TotalCount,
            string.Join(',', result.Items.Select(x =>
                $"{x.Id}:{x.UpdatedAtUtc.Ticks}:{x.Status}:{x.RegistrationStage}:{x.HasPendingAction}:{x.ActionMatchId}:{x.PendingActionLabel}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
