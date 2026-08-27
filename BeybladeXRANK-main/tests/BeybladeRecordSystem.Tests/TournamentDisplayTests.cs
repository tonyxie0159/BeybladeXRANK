using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;

namespace BeybladeRecordSystem.Tests;

public sealed class TournamentDisplayTests
{
    [Fact]
    public void Formats_UseConfirmedTraditionalChineseLabels()
    {
        Assert.Equal("單淘汰", TournamentDisplay.Label(TournamentFormat.SingleElimination));
        Assert.Equal("雙敗淘汰", TournamentDisplay.Label(TournamentFormat.DoubleElimination));
        Assert.Equal("單循環", TournamentDisplay.Label(TournamentFormat.RoundRobin));
        Assert.Equal("瑞士輪", TournamentDisplay.Label(TournamentFormat.Swiss));
    }

    [Fact]
    public void EveryUserVisibleStatus_HasLocalizedLabelAndSemanticBadge()
    {
        AssertLocalized(Enum.GetValues<TournamentStatus>(), TournamentDisplay.Label, TournamentDisplay.BadgeClass);
        AssertLocalized(Enum.GetValues<TournamentRegistrationStage>(), TournamentDisplay.Label, TournamentDisplay.BadgeClass);
        AssertLocalized(Enum.GetValues<TournamentMatchStatus>(), TournamentDisplay.Label, TournamentDisplay.BadgeClass);
        AssertLocalized(Enum.GetValues<TournamentEntryStatus>(), TournamentDisplay.Label, _ => "not-applicable");
        AssertLocalized(Enum.GetValues<TournamentInvitationStatus>(), TournamentDisplay.Label, _ => "not-applicable");
        AssertLocalized(Enum.GetValues<TournamentParticipationStatus>(), TournamentDisplay.Label, _ => "not-applicable");
        AssertLocalized(Enum.GetValues<TournamentRegistrationMode>(), TournamentDisplay.Label, _ => "not-applicable");
    }

    private static void AssertLocalized<T>(
        IEnumerable<T> values,
        Func<T, string> label,
        Func<T, string> badgeClass) where T : struct, Enum
    {
        foreach (var value in values)
        {
            Assert.NotEqual(value.ToString(), label(value));
            Assert.False(string.IsNullOrWhiteSpace(label(value)));
            Assert.False(string.IsNullOrWhiteSpace(badgeClass(value)));
        }
    }
}
