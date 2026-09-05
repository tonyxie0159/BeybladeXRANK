using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Statistics;

[Authorize]
public class BeybladeModel(StatisticsService statistics) : PageModel
{
    public BeybladeVersionStatistics Details { get; private set; } = null!;
    [BindProperty(SupportsGet = true)] public StatisticsSourceFilter Source { get; set; } = StatisticsSourceFilter.All;
    [BindProperty(SupportsGet = true)] public StatisticsSideFilter Side { get; set; } = StatisticsSideFilter.All;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!Enum.IsDefined(Source) || !Enum.IsDefined(Side)) return BadRequest();
        var result = await statistics.GetBeybladeVersionStatisticsAsync(User.GetRequiredUserId(), id, Source, Side);
        if (result is null) return NotFound();
        Details = result;
        return Page();
    }
}
