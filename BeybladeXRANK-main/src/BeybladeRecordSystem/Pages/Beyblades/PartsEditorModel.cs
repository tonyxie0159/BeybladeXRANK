using BeybladeRecordSystem.Domain.Entities;
namespace BeybladeRecordSystem.Pages.Beyblades;
public record PartsEditorModel(IReadOnlyList<Part> AvailableParts, IReadOnlyList<int> PartIds);
