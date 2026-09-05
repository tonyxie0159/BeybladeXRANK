using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class EditModel(AppDbContext db, BeybladeService beybladeService, BeybladeConfigurationService configurations) : PageModel
{
    public int Id { get; private set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public List<int> PartIds { get; set; } = [];
    public BeybladeConfiguration? Configuration { get; private set; }
    public List<BeybladeConfiguration> Versions { get; private set; } = [];
    public List<Part> AvailableParts { get; private set; } = [];
    public async Task<IActionResult> OnGetAsync(int id, int? versionId)
    {
        var beyblade = await LoadAsync(id);
        if (beyblade is null) return NotFound();
        Name = beyblade.Name;
        if (versionId is not null)
        {
            Configuration = Versions.SingleOrDefault(x => x.Id == versionId);
            if (Configuration is null) return NotFound();
        }
        PartIds = Configuration?.Parts.Select(x => x.PartId).ToList() ?? [];
        return Page();
    }
    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        if (await LoadAsync(id) is null) return NotFound();
        if (!ModelState.IsValid) return Page();
        var ids = PartIds.Where(x => x != 0).ToArray();
        var result = await configurations.RecordAsync(User.GetRequiredUserId(), id, ids, Name);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        TempData["Success"] = "已儲存。相同零件組合沿用既有版本，原版本與對戰紀錄均保留。";
        var selected = (await configurations.GetVersionsAsync(User.GetRequiredUserId(), id))
            .Single(x => x.PartsKey == string.Join(",", ids.Order()));
        return RedirectToPage(new { id, versionId = selected.Id });
    }

    public async Task<IActionResult> OnPostRenameAsync([FromRoute] int id)
    {
        if (await LoadAsync(id) is null) return NotFound();
        if (!ModelState.IsValid) return Page();
        var result = await beybladeService.RenameAsync(User.GetRequiredUserId(), id, Name);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return Page(); }
        return RedirectToPage(new { id });
    }

    private async Task<Beyblade?> LoadAsync(int id)
    {
        var userId = User.GetRequiredUserId();
        var blade = await db.Beyblades.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId && !x.IsDeleted);
        if (blade is null) return null;
        Id = id;
        Versions = await configurations.GetVersionsAsync(userId, id);
        Configuration = Versions.FirstOrDefault();
        AvailableParts = await configurations.GetActivePartsAsync();
        return blade;
    }
}
