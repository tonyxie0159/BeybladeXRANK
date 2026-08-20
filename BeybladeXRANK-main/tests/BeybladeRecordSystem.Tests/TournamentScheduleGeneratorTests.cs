using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.Tests;

public class TournamentScheduleGeneratorTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(16)]
    public void SingleElimination_AlwaysContainsEntryCountMinusOneMatches(int entryCount)
    {
        var schedule = TournamentScheduleGenerator.GenerateSingleElimination(
            Enumerable.Range(1, entryCount),
            randomSeed: 19);

        Assert.Equal(entryCount - 1, schedule.Matches.Count);
        Assert.Equal(Enumerable.Range(1, schedule.Matches.Count), schedule.Matches.Select(x => x.SequenceNumber));
        Assert.All(schedule.Matches, match =>
        {
            Assert.NotEqual(match.SideA, match.SideB);
            Assert.True(match.SideA.ReferenceId > 0);
            Assert.True(match.SideB.ReferenceId > 0);
        });
    }

    [Fact]
    public void SixEntrySingleElimination_MarksQualifierAndAdvancesItsWinnerWithBye()
    {
        var schedule = TournamentScheduleGenerator.GenerateSingleElimination(
            Enumerable.Range(1, 6),
            randomSeed: 7);

        var qualifier = Assert.Single(schedule.Matches, x => x.IsSeedQualifier);
        var earnedBye = Assert.Single(schedule.Byes, x => x.IsSeedQualifierAdvancement);
        Assert.Equal(2, earnedBye.RoundNumber);
        Assert.Equal(TournamentParticipantSource.WinnerOf(qualifier.Id), earnedBye.Participant);
        Assert.Equal(new[] { 3, 1, 1 }, schedule.Matches
            .GroupBy(x => x.RoundNumber)
            .OrderBy(x => x.Key)
            .Select(x => x.Count()));
    }

    [Fact]
    public void FiveEntrySingleElimination_UsesExplicitByesAndFourMatches()
    {
        var schedule = TournamentScheduleGenerator.GenerateSingleElimination(
            Enumerable.Range(1, 5),
            randomSeed: 5);

        Assert.Equal(4, schedule.Matches.Count);
        Assert.Equal(2, schedule.Byes.Count);
        Assert.DoesNotContain(schedule.Matches, x => x.IsSeedQualifier);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void ChampionPlayoff_UsesBalancedTopologicalSingleElimination(int entryCount)
    {
        var matches = TournamentScheduleGenerator.GenerateChampionPlayoff(
            Enumerable.Range(1, entryCount),
            randomSeed: 17);
        var sequenceByMatchId = matches.ToDictionary(x => x.Id, x => x.SequenceNumber);

        Assert.Equal(entryCount - 1, matches.Count);
        Assert.Equal(Enumerable.Range(1, matches.Count), matches.Select(x => x.SequenceNumber));
        Assert.All(matches, x => Assert.Equal(TournamentBracket.Playoff, x.Bracket));
        Assert.Equal(Enumerable.Range(1, entryCount), matches
            .SelectMany(x => new[] { x.SideA, x.SideB })
            .Where(x => x.Kind == TournamentParticipantSourceKind.Entry)
            .Select(x => x.ReferenceId).Order());
        Assert.All(matches, match =>
        {
            AssertSourcePrecedesMatch(match.SideA, match.SequenceNumber, sequenceByMatchId);
            AssertSourcePrecedesMatch(match.SideB, match.SequenceNumber, sequenceByMatchId);
        });
    }

    [Fact]
    public void FourEntryDoubleElimination_HasFixedLowerBracketAndConditionalResetFinal()
    {
        var schedule = TournamentScheduleGenerator.GenerateDoubleElimination(
            Enumerable.Range(1, 4),
            randomSeed: 11);

        Assert.Equal(3, schedule.Matches.Count(x => x.Bracket == TournamentBracket.Winners));
        Assert.Equal(2, schedule.Matches.Count(x => x.Bracket == TournamentBracket.Losers));
        Assert.Equal(2, schedule.Matches.Count(x => x.Bracket == TournamentBracket.GrandFinal));
        var reset = Assert.Single(schedule.Matches, x => x.IsResetFinal);
        var firstFinal = Assert.Single(schedule.Matches, x =>
            x.Bracket == TournamentBracket.GrandFinal && !x.IsResetFinal);
        Assert.Equal(TournamentParticipantSource.WinnerOf(firstFinal.Id), reset.SideA);
        Assert.Equal(TournamentParticipantSource.LoserOf(firstFinal.Id), reset.SideB);
        Assert.Equal(Enumerable.Range(1, schedule.Matches.Count), schedule.Matches.Select(x => x.SequenceNumber));
    }

    [Fact]
    public void NonPowerOfTwoDoubleElimination_UsesByesWithoutCreatingEmptyMatches()
    {
        var schedule = TournamentScheduleGenerator.GenerateDoubleElimination(
            Enumerable.Range(1, 5),
            randomSeed: 3);

        Assert.NotEmpty(schedule.Byes);
        Assert.All(schedule.Matches, match =>
        {
            Assert.NotNull(match.SideA);
            Assert.NotNull(match.SideB);
        });
        Assert.Single(schedule.Matches, x => x.IsResetFinal);
    }

    [Fact]
    public void DoubleElimination_AllSupportedEntryCountsHaveCompleteTopologicalBrackets()
    {
        for (var entryCount = 2; entryCount <= 256; entryCount++)
        {
            var schedule = TournamentScheduleGenerator.GenerateDoubleElimination(
                Enumerable.Range(1, entryCount),
                randomSeed: entryCount);
            var sequenceByMatchId = schedule.Matches.ToDictionary(x => x.Id, x => x.SequenceNumber);

            Assert.Equal((2 * entryCount) - 1, schedule.Matches.Count);
            Assert.Single(schedule.Matches, x => x.IsResetFinal);
            Assert.Equal(Enumerable.Range(1, schedule.Matches.Count), schedule.Matches.Select(x => x.SequenceNumber));
            Assert.All(schedule.Matches, match =>
            {
                AssertSourcePrecedesMatch(match.SideA, match.SequenceNumber, sequenceByMatchId);
                AssertSourcePrecedesMatch(match.SideB, match.SequenceNumber, sequenceByMatchId);
            });
        }
    }

    [Theory]
    [InlineData(4, 6, 3, 0)]
    [InlineData(5, 10, 5, 5)]
    public void RoundRobin_PairsEveryEntryExactlyOnce(
        int entryCount,
        int expectedMatches,
        int expectedRounds,
        int expectedByes)
    {
        var schedule = TournamentScheduleGenerator.GenerateRoundRobin(Enumerable.Range(1, entryCount));
        var pairs = schedule.Matches
            .Select(match => string.Join('-', new[] { match.SideA.ReferenceId, match.SideB.ReferenceId }.Order()))
            .ToList();

        Assert.Equal(expectedMatches, schedule.Matches.Count);
        Assert.Equal(expectedRounds, schedule.Matches.Max(x => x.RoundNumber));
        Assert.Equal(expectedByes, schedule.Byes.Count);
        Assert.Equal(expectedMatches, pairs.Distinct().Count());
    }

    [Fact]
    public void Generator_RejectsDuplicateEntries()
    {
        Assert.Throws<ArgumentException>(() =>
            TournamentScheduleGenerator.GenerateSingleElimination([1, 1, 2], randomSeed: 1));
    }

    private static void AssertSourcePrecedesMatch(
        TournamentParticipantSource source,
        int sequenceNumber,
        IReadOnlyDictionary<int, int> sequenceByMatchId)
    {
        if (source.Kind == TournamentParticipantSourceKind.Entry)
            return;

        Assert.True(sequenceByMatchId.TryGetValue(source.ReferenceId, out var sourceSequence));
        Assert.True(sourceSequence < sequenceNumber,
            $"Match sequence {sequenceNumber} depends on later sequence {sourceSequence}.");
    }
}
