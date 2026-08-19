namespace BeybladeRecordSystem.Domain.Tournaments;

public static class SwissPairingGenerator
{
    public static int RoundCountFor(int entryCount)
    {
        if (entryCount < 2 || entryCount > 512)
            throw new ArgumentOutOfRangeException(nameof(entryCount), "Entry count must be between 2 and 512.");
        return (int)Math.Ceiling(Math.Log2(entryCount));
    }

    public static SwissRoundDefinition GenerateRound(
        IEnumerable<SwissEntryStanding> standings,
        int roundNumber,
        int? randomSeed = null)
    {
        ArgumentNullException.ThrowIfNull(standings);
        var entries = standings.ToList();
        if (entries.Count < 2 || entries.Count > 512)
            throw new ArgumentOutOfRangeException(nameof(standings), "Entry count must be between 2 and 512.");
        if (entries.Select(x => x.EntryId).Distinct().Count() != entries.Count)
            throw new ArgumentException("Each entry must have exactly one standing.", nameof(standings));
        if (roundNumber < 1 || roundNumber > RoundCountFor(entries.Count))
            throw new ArgumentOutOfRangeException(nameof(roundNumber));

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        int? byeEntryId = null;
        if (entries.Count % 2 == 1)
        {
            var eligible = entries.Where(x => !x.HadBye).ToList();
            if (eligible.Count == 0)
                eligible = entries;
            var lowestWins = eligible.Min(x => x.Wins);
            var candidates = eligible.Where(x => x.Wins == lowestWins).ToList();
            var selected = candidates[random.Next(candidates.Count)];
            byeEntryId = selected.EntryId;
            entries.Remove(selected);
        }

        IReadOnlyList<SwissPairing> pairings;
        if (roundNumber == 1)
        {
            Shuffle(entries, random);
            pairings = entries
                .Chunk(2)
                .Select(pair => new SwissPairing(pair[0].EntryId, pair[1].EntryId))
                .ToList();
        }
        else
        {
            var ordered = entries
                .OrderByDescending(x => x.Wins)
                .ThenByDescending(x => x.Buchholz)
                .ThenByDescending(x => x.OpponentWinRate)
                .ThenBy(x => x.EntryId)
                .ToList();
            pairings = TryPair(ordered, allowRematches: false)
                ?? TryPair(ordered, allowRematches: true)
                ?? throw new InvalidOperationException("Unable to produce a complete Swiss pairing.");
        }

        return new SwissRoundDefinition(roundNumber, pairings, byeEntryId);
    }

    private static IReadOnlyList<SwissPairing>? TryPair(
        IReadOnlyList<SwissEntryStanding> entries,
        bool allowRematches)
    {
        if (entries.Count == 0)
            return [];

        var first = entries[0];
        var candidates = entries
            .Skip(1)
            .Where(candidate => allowRematches || !first.OpponentIds.Contains(candidate.EntryId))
            .OrderBy(candidate => Math.Abs(first.Wins - candidate.Wins))
            .ThenBy(candidate => Math.Abs(first.Buchholz - candidate.Buchholz))
            .ThenBy(candidate => candidate.EntryId)
            .ToList();

        foreach (var candidate in candidates)
        {
            var remaining = entries
                .Where(x => x.EntryId != first.EntryId && x.EntryId != candidate.EntryId)
                .ToList();
            var rest = TryPair(remaining, allowRematches);
            if (rest is null)
                continue;

            return new[] { new SwissPairing(first.EntryId, candidate.EntryId) }
                .Concat(rest)
                .ToList();
        }

        return null;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapWith = random.Next(index + 1);
            (values[index], values[swapWith]) = (values[swapWith], values[index]);
        }
    }
}
