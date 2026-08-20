using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class ReorderModel(QuickBattleFlowService flowService) : PageModel
{
    public QuickBattleReorderWorkspace Workspace { get; private set; } = null!;
    [BindProperty] public List<int> BladeIds { get; set; } = [];
    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();
    public async Task<IActionResult> OnPostAsync(int id)
    {
        var result = await flowService.SubmitReorderAsync(id, User.GetRequiredUserId(), BladeIds);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage(new { id });
        }
        if (await flowService.GetReorderWorkspaceAsync(id, User.GetRequiredUserId()) is null)
            return RedirectToPage("Battle", new { id });
        TempData["Success"] = "重排已密封提交。";
        return RedirectToPage(new { id });
    }
    private async Task<bool> LoadAsync(int id)
    {
        var workspace = await flowService.GetReorderWorkspaceAsync(id, User.GetRequiredUserId());
        if (workspace is null) return false;
        Workspace = workspace;
        return true;
    }
}
