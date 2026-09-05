using BeybladeRecordSystem.Domain.Entities;
namespace BeybladeRecordSystem.Pages.Beyblades;
public record LineupVersionPickerModel(int Position, IReadOnlyList<Beyblade> Beyblades);
