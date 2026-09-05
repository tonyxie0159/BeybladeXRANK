using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class SetupModel(QuickBattleFlowService flowService, BattleService battleService) : PageModel
{
    public QuickBattleWorkspace Workspace { get; private set; } = null!;
    [BindProperty] public List<int> BladeIds { get; set; } = [];
    [BindProperty] public List<int> ConfigurationIds { get; set; } = [];
    [BindProperty] public BattleSide SideA { get; set; } = BattleSide.B;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Workspace.Battle.Status is BattleStatus.InProgress or BattleStatus.VictoryPendingCompletion)
            return RedirectToPage("Battle", new { id });
        if (Workspace.Battle.Status is BattleStatus.Completed or BattleStatus.Forfeited or BattleStatus.Voided)
            return RedirectToPage("Details", new { id });
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitLineupAsync(int id)
    {
        var result = ModelState.IsValid
            ? await flowService.SubmitLineupAsync(id, User.GetRequiredUserId(), BladeIds, ConfigurationIds)
            : ServiceResult.Failure("請選擇有效的陀螺與版本。");
        if (result.Succeeded)
            return RedirectWith(result, id, "陣容已密封提交。");

        if (!await LoadAsync(id)) return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "無法提交陣容。");
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id) =>
        RedirectWith(await flowService.ConfirmLineupAsync(id, User.GetRequiredUserId()), id, "已確認本版陣容。");

    public async Task<IActionResult> OnPostRequestEditAsync(int id) =>
        RedirectWith(await flowService.RequestLineupEditAsync(id, User.GetRequiredUserId()), id, "重新編輯請求已送出。");

    public async Task<IActionResult> OnPostRespondEditAsync(int id, bool accept) =>
        RedirectWith(await flowService.RespondLineupEditAsync(id, User.GetRequiredUserId(), accept), id,
            accept ? "已接受請求，雙方需重新密封提交。" : "已拒絕請求，保留目前陣容。");

    public async Task<IActionResult> OnPostAssignSideAsync(int id)
        => RedirectWith(await battleService.AssignSidesAsync(id, User.GetRequiredUserId(), SideA), id, "B/X Side 已指定。");

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var result = Workspace.Battle.Status == BattleStatus.LineupLocked
            ? await battleService.AssignSidesAndStartAsync(id, User.GetRequiredUserId(), SideA)
            : await battleService.StartBattleAsync(id, User.GetRequiredUserId());
        if (result.Succeeded) return RedirectToPage("Battle", new { id });
        TempData["Error"] = result.Error;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var workspace = await flowService.GetWorkspaceAsync(id, User.GetRequiredUserId());
        if (workspace is null) return false;
        Workspace = workspace;
        return true;
    }

    private IActionResult RedirectWith(ServiceResult result, int id, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage(new { id });
    }
}
