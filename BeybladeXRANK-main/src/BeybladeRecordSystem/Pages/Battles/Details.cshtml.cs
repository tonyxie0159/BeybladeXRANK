using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Battles;

[Authorize]
public class DetailsModel(BattleService battleService) : PageModel
{
    public Battle Battle { get; private set; } = null!;
    private TournamentMatch? SourceMatch => Battle.TournamentMatch ?? Battle.VoidedTournamentMatch;
    private bool SideAIsB => Battle.SideADesignation == BattleSide.B;
    private string SideALabel => Battle.SourceType == BattleSourceType.TournamentTeam
        ? SourceMatch?.SideAEntry?.DisplayNameSnapshot ?? "A 方隊伍"
        : Battle.PlayerA?.DisplayName ?? "A 方玩家";
    private string SideBLabel => Battle.SourceType == BattleSourceType.TournamentTeam
        ? SourceMatch?.SideBEntry?.DisplayNameSnapshot ?? "B 方隊伍"
        : Battle.PlayerB?.DisplayName ?? "B 方玩家";

    public string BLabel => SideAIsB ? SideALabel : SideBLabel;
    public string XLabel => SideAIsB ? SideBLabel : SideALabel;
    public int BScore => SideAIsB ? Battle.SideAScore : Battle.SideBScore;
    public int XScore => SideAIsB ? Battle.SideBScore : Battle.SideAScore;
    public string WinnerTitle => Battle.SourceType == BattleSourceType.TournamentTeam ? "勝利隊伍" : "勝利玩家";
    public string WinnerLabel => Battle.WinningSide switch
    {
        BattleSide.B => BLabel,
        BattleSide.X => XLabel,
        _ when Battle.WinningPlayerId == Battle.PlayerAId => SideALabel,
        _ when Battle.WinningPlayerId == Battle.PlayerBId => SideBLabel,
        _ => "尚未記錄"
    };
    public int? TournamentId => SourceMatch?.TournamentId;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var result = await battleService.GetBattleAsync(id, User.GetRequiredUserId());
        if (!result.Succeeded) return NotFound();
        Battle = result.Value!;

        if (Battle.Status is BattleStatus.Completed or BattleStatus.Forfeited)
            return Page();

        if (Battle.SourceType != BattleSourceType.Quick && SourceMatch is not null)
            return RedirectToPage("/Tournaments/Match", new { id = SourceMatch.Id });

        return Battle.Status switch
        {
            BattleStatus.LineupSelection or BattleStatus.LineupReview or BattleStatus.LineupLocked or BattleStatus.SideSelection
                => RedirectToPage("Setup", new { id }),
            BattleStatus.ReorderSelection => RedirectToPage("Reorder", new { id }),
            _ => RedirectToPage("Battle", new { id })
        };
    }
}
