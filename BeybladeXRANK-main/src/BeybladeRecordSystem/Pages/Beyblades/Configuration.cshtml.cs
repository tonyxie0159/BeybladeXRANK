using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Pages.Beyblades;

[Authorize]
public class ConfigurationModel(AppDbContext db, BeybladeConfigurationService configurations) : PageModel
{
    public string BeybladeName { get; private set; } = string.Empty;
    public BeybladeConfiguration? Configuration { get; private set; }
    public List<Part> AvailableParts { get; private set; } = [];
    [BindProperty] public List<int> PartIds { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var result = await configurations.RecordAsync(User.GetRequiredUserId(), id, PartIds.Where(x => x != 0).ToArray());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }
        TempData["Success"] = "零件版本已保存；相同組合沿用既有版本。";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var userId = User.GetRequiredUserId();
        var blade = await db.Beyblades.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId && !x.IsDeleted);
        if (blade is null) return false;
        BeybladeName = blade.Name;
        Configuration = await configurations.GetMineAsync(userId, id);
        AvailableParts = await configurations.GetActivePartsAsync();
        return true;
    }

    public static string CategoryLabel(PartCategory category) => category switch
    {
        PartCategory.Blade => "上蓋",
        PartCategory.Ratchet => "固鎖",
        PartCategory.Bit => "軸心",
        PartCategory.LockChip => "鎖定紋章",
        PartCategory.MainBlade => "主要戰刃",
        PartCategory.OverBlade => "超越戰刃",
        PartCategory.MetalBlade => "金屬戰刃",
        PartCategory.AssistBlade => "輔助戰刃",
        _ => category.ToString()
    };
}
