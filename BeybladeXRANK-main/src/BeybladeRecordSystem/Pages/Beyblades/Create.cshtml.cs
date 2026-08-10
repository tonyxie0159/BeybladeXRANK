using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class CreateModel(BeybladeService beybladeService) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    public async Task<IActionResult> OnPostAsync()
    {
        var result = await beybladeService.CreateAsync(User.GetRequiredUserId(), Name);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        return RedirectToPage("Index");
    }
}
