using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.Tests;

public class SwissPairingGeneratorTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(512, 9)]
    public void RoundCount_UsesCeilingLogTwo(int entryCount, int expected) =>
        Assert.Equal(expected, SwissPairingGenerator.RoundCountFor(entryCount));

    [Fact]
    public void FirstRound_IsReproducibleAndUsesEveryEntryOnce()
    {
        var standings = Enumerable.Range(1, 8)
            .Select(entryId => NewStanding(entryId))
            .ToList();

        var first = SwissPairingGenerator.GenerateRound(standings, 1, randomSeed: 23);
        var second = SwissPairingGenerator.GenerateRound(standings, 1, randomSeed: 23);
        var used = first.Pairings.SelectMany(x => new[] { x.EntryAId, x.EntryBId }).ToList();

        Assert.Equal(first.RoundNumber, second.RoundNumber);
        Assert.Equal(first.ByeEntryId, second.ByeEntryId);
        Assert.Equal(first.Pairings, second.Pairings);
        Assert.Equal(Enumerable.Range(1, 8), used.Order());
        Assert.Null(first.ByeEntryId);
    }

    [Fact]
    public void LaterRound_AvoidsPreviousOpponentsBeforeCrossingAdjacentScoreGroups()
    {
        var standings = new[]
        {
            NewStanding(1, wins: 2, opponents: new HashSet<int> { 2 }),
            NewStanding(2, wins: 2, opponents: new HashSet<int> { 1 }),
            NewStanding(3, wins: 2, opponents: new HashSet<int> { 4 }),
            NewStanding(4, wins: 2, opponents: new HashSet<int> { 3 }),
            NewStanding(5, wins: 1, opponents: new HashSet<int> { 6 }),
            NewStanding(6, wins: 1, opponents: new HashSet<int> { 5 })
        };

        var round = SwissPairingGenerator.GenerateRound(standings, 2, randomSeed: 1);

        Assert.All(round.Pairings, pairing =>
        {
            var a = standings.Single(x => x.EntryId == pairing.EntryAId);
            var b = standings.Single(x => x.EntryId == pairing.EntryBId);
            Assert.DoesNotContain(b.EntryId, a.OpponentIds);
            Assert.InRange(Math.Abs(a.Wins - b.Wins), 0, 1);
        });
    }

    [Fact]
    public void OddRound_GivesByeToLowestScoreWithoutPreviousBye()
    {
        var standings = new[]
        {
            NewStanding(1, wins: 2),
            NewStanding(2, wins: 1),
            NewStanding(3, wins: 0, hadBye: true),
            NewStanding(4, wins: 0),
            NewStanding(5, wins: 1)
        };

        var round = SwissPairingGenerator.GenerateRound(standings, 2, randomSeed: 2);

        Assert.Equal(4, round.ByeEntryId);
        Assert.DoesNotContain(4, round.Pairings.SelectMany(x => new[] { x.EntryAId, x.EntryBId }));
    }

    [Fact]
    public void LaterRound_AllowsRematchOnlyWhenNoCompleteAlternativeExists()
    {
        var standings = new[]
        {
            NewStanding(1, wins: 1, opponents: new HashSet<int> { 2, 3 }),
            NewStanding(2, wins: 1, opponents: new HashSet<int> { 1, 3 }),
            NewStanding(3, wins: 1, opponents: new HashSet<int> { 1, 2 }),
            NewStanding(4, wins: 0)
        };

        var round = SwissPairingGenerator.GenerateRound(standings, 2, randomSeed: 1);

        Assert.Equal(2, round.Pairings.Count);
        Assert.Equal(4, round.Pairings.SelectMany(x => new[] { x.EntryAId, x.EntryBId }).Distinct().Count());
    }

    private static SwissEntryStanding NewStanding(
        int entryId,
        int wins = 0,
        IReadOnlySet<int>? opponents = null,
        bool hadBye = false) =>
        new(entryId, wins, opponents ?? new HashSet<int>(), hadBye);
}
