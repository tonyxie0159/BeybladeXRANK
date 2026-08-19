using BeybladeRecordSystem.Domain.Enums;

namespace BeybladeRecordSystem.Domain.Tournaments;

public sealed record TournamentRuleDefinition(
    TournamentRuleSet RuleSet,
    string DisplayName,
    TournamentMode Mode,
    int? TeamSize,
    int BeybladesPerPlayer,
    int ScoreToWin);

public static class TournamentRuleCatalog
{
    private static readonly IReadOnlyDictionary<TournamentRuleSet, TournamentRuleDefinition> Definitions =
        new Dictionary<TournamentRuleSet, TournamentRuleDefinition>
        {
            [TournamentRuleSet.IndividualThreeBladeFourPoints] = new(
                TournamentRuleSet.IndividualThreeBladeFourPoints, "單人賽｜每人三顆｜4 分制", TournamentMode.Individual, null, 3, 4),
            [TournamentRuleSet.DuoSixBladeEightPoints] = new(
                TournamentRuleSet.DuoSixBladeEightPoints, "雙人團體｜每人三顆｜8 分制", TournamentMode.Team, 2, 3, 8),
            [TournamentRuleSet.DuoFourBladeSixPoints] = new(
                TournamentRuleSet.DuoFourBladeSixPoints, "雙人團體｜每人兩顆｜6 分制", TournamentMode.Team, 2, 2, 6),
            [TournamentRuleSet.TrioThreeBladeFourPoints] = new(
                TournamentRuleSet.TrioThreeBladeFourPoints, "三人團體｜每人一顆｜4 分制", TournamentMode.Team, 3, 1, 4),
            [TournamentRuleSet.TrioThreeBladeFivePoints] = new(
                TournamentRuleSet.TrioThreeBladeFivePoints, "三人團體｜每人一顆｜5 分制", TournamentMode.Team, 3, 1, 5)
        };

    public static IReadOnlyCollection<TournamentRuleDefinition> All => Definitions.Values.ToArray();

    public static TournamentRuleDefinition Get(TournamentRuleSet ruleSet) =>
        Definitions.TryGetValue(ruleSet, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(ruleSet));

    public static int EntryLimit(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => 512,
        TournamentFormat.DoubleElimination => 256,
        TournamentFormat.RoundRobin => 32,
        TournamentFormat.Swiss => 512,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string BuildSnapshot(TournamentRuleDefinition rule, TournamentFormat format) =>
        $"{rule.DisplayName}；賽制：{format}；每場重新選擇陀螺；達勝分後須由裁判確認結束。";
}
