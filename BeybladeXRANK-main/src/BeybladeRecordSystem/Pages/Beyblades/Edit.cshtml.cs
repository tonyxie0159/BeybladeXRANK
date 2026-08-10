using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class EditModel(AppDbContext db, BeybladeService beybladeService) : PageModel
{
    [BindProperty] public int Id { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var beyblade = await db.Beyblades.FindAsync(id);
        if (beyblade is null || beyblade.UserId != User.GetRequiredUserId() || beyblade.IsDeleted) return NotFound();
        Id = beyblade.Id; Name = beyblade.Name; return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        var result = await beybladeService.RenameAsync(User.GetRequiredUserId(), Id, Name);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        return RedirectToPage("Index");
    }
}
