using System.ComponentModel.DataAnnotations;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Infrastructure;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeybladeRecordSystem.Pages.Tournaments;

[Authorize]
public class CreateModel(TournamentService tournamentService) : PageModel
{
    [BindProperty, Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [BindProperty] public TournamentRuleSet RuleSet { get; set; } = TournamentRuleSet.IndividualThreeBladeFourPoints;
    [BindProperty] public TournamentRegistrationMode RegistrationMode { get; set; } = TournamentRegistrationMode.Individual;
    [BindProperty] public TournamentFormat Format { get; set; } = TournamentFormat.SingleElimination;
    [BindProperty, Range(2, 512)] public int TargetEntryCount { get; set; } = 8;
    [BindProperty, StringLength(1000)] public string? Notes { get; set; }
    public IReadOnlyCollection<TournamentRuleDefinition> Rules => TournamentRuleCatalog.All;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await tournamentService.CreateAsync(
            User.GetRequiredUserId(),
            new CreateTournamentRequest(Name, RuleSet, RegistrationMode, Format, TargetEntryCount, Notes));
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }
        return RedirectToPage("Details", new { id = result.Value!.Id });
    }
}
