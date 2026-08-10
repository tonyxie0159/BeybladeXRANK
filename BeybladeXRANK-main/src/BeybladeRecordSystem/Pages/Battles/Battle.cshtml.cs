using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class BattleModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;
    public BattleRound? CurrentRound => Battle.Rounds.OrderByDescending(x => x.RoundNo).FirstOrDefault(x => x.Status == BattleRoundStatus.InProgress);
    public bool CanOperate => Battle.CreatedByUserId == User.GetRequiredUserId();
    public bool CanReorder => CanOperate && Battle.Status == BattleStatus.InProgress && CurrentRound is null && Battle.Lineups.Where(x => x.IsCurrent).Count() == 3;

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();
    public async Task<IActionResult> OnPostFaultAsync(int id, int playerId)
    {
        var loaded = await LoadAsync(id); if (!loaded || CurrentRound is null) return NotFound();
        var result = await battleService.RecordLaunchFaultAsync(id, CurrentRound.Id, User.GetRequiredUserId(), playerId);
        return RedirectWithResult(id, result.Succeeded, result.Error);
    }
    public async Task<IActionResult> OnPostResultAsync(int id, int winnerPlayerId, ResultType resultType)
    {
        var loaded = await LoadAsync(id); if (!loaded || CurrentRound is null) return NotFound();
        var result = await battleService.RecordBattleResultAsync(id, CurrentRound.Id, User.GetRequiredUserId(), winnerPlayerId, resultType);
        return RedirectWithResult(id, result);
    }
    public async Task<IActionResult> OnPostCompleteRoundAsync(int id)
    {
        var loaded = await LoadAsync(id); if (!loaded || CurrentRound is null) return NotFound();
        var result = await battleService.CompleteRoundAsync(id, CurrentRound.Id, User.GetRequiredUserId());
        return RedirectWithResult(id, result.Succeeded, result.Error);
    }
    public async Task<IActionResult> OnPostFinishAsync(int id)
    {
        var result = await battleService.FinishBattleAsync(id, User.GetRequiredUserId());
        return RedirectWithResult(id, result);
    }
    private async Task<bool> LoadAsync(int id)
    {
        var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return false; Battle = result.Value!; return true;
    }
    private IActionResult RedirectWithResult(int id, ServiceResult result) => RedirectWithResult(id, result.Succeeded, result.Error);
    private IActionResult RedirectWithResult(int id, bool succeeded, string? error)
    {
        if (!succeeded) TempData["Error"] = error;
        return RedirectToPage(new { id });
    }
}
