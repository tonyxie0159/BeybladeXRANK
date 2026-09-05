using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class CreateModel(BeybladeService beybladeService, BeybladeConfigurationService configurations) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public List<int> PartIds { get; set; } = [];
    public List<Part> AvailableParts { get; private set; } = [];
    public async Task OnGetAsync() => AvailableParts = await configurations.GetActivePartsAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        AvailableParts = await configurations.GetActivePartsAsync();
        if (!ModelState.IsValid) return Page();
        var result = await beybladeService.CreateAsync(User.GetRequiredUserId(), Name, PartIds.Where(x => x != 0).ToArray());
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)) return LocalRedirect(ReturnUrl);
        return RedirectToPage("Index");
    }
}
