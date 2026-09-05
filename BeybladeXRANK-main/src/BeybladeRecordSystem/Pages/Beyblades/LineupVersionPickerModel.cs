using BeybladeRecordSystem.Domain.Entities;
namespace BeybladeRecordSystem.Pages.Beyblades;
public record LineupVersionPickerModel(
    int Position,
    IReadOnlyList<Beyblade> Beyblades,
    int? SelectedBeybladeId = null,
    int? SelectedConfigurationId = null,
    int? RecentBeybladeId = null,
    int? RecentConfigurationId = null);
