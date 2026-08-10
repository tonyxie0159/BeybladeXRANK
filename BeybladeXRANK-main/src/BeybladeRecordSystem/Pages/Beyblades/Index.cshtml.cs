using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class IndexModel(BeybladeService beybladeService) : PageModel
{
    public List<Beyblade> Beyblades { get; private set; } = [];
    [BindProperty] public string NewName { get; set; } = string.Empty;
    public async Task OnGetAsync() => Beyblades = await beybladeService.GetMyBeybladesAsync(User.GetRequiredUserId());
    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await beybladeService.CreateAsync(User.GetRequiredUserId(), NewName);
        if (!result.Succeeded) { TempData["Error"] = result.Error; } else { TempData["Success"] = "陀螺已新增。"; }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await beybladeService.DeleteAsync(User.GetRequiredUserId(), id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "陀螺已刪除。" : result.Error;
        return RedirectToPage();
    }
}
