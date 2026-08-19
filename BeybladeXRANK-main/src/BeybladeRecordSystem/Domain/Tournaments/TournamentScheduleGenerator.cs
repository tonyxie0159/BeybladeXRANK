namespace BeybladeRecordSystem.Domain.Tournaments;

public static class TournamentScheduleGenerator
{
    public static TournamentSchedule Generate(
        TournamentFormat format,
        IEnumerable<int> entryIds,
        int? randomSeed = null) => format switch
        {
            TournamentFormat.SingleElimination => GenerateSingleElimination(entryIds, randomSeed),
            TournamentFormat.DoubleElimination => GenerateDoubleElimination(entryIds, randomSeed),
            TournamentFormat.RoundRobin => GenerateRoundRobin(entryIds),
            TournamentFormat.Swiss => throw new ArgumentException(
                "Swiss pairings depend on current standings; use SwissPairingGenerator.GenerateRound.",
                nameof(format)),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    public static TournamentSchedule GenerateSingleElimination(
        IEnumerable<int> entryIds,
        int? randomSeed = null)
    {
        var entries = ValidateEntries(entryIds, 512);
        var random = CreateRandom(randomSeed);
        var current = entries
            .Select(TournamentParticipantSource.Entry)
            .ToList();
        Shuffle(current, random);

        var matches = new List<TournamentMatchDefinition>();
        var byes = new List<TournamentByeDefinition>();
        TournamentParticipantSource? earnedBye = null;
        var matchId = 1;
        var roundNumber = 1;

        while (current.Count > 1)
        {
            TournamentParticipantSource? roundBye = null;
            if (current.Count % 2 == 1)
            {
                var byeIndex = earnedBye is null ? random.Next(current.Count) : current.IndexOf(earnedBye);
                if (byeIndex < 0)
                    throw new InvalidOperationException("The earned seed bye is not present in the next stage.");

                roundBye = current[byeIndex];
                current.RemoveAt(byeIndex);
                byes.Add(new TournamentByeDefinition(
                    TournamentBracket.Winners,
                    roundNumber,
                    byes.Count(x => x.RoundNumber == roundNumber) + 1,
                    roundBye,
                    earnedBye is not null));
                earnedBye = null;
            }

            var next = new List<TournamentParticipantSource>();
            var roundMatchIndexes = new List<int>();
            for (var index = 0; index < current.Count; index += 2)
            {
                var match = new TournamentMatchDefinition(
                    matchId++,
                    TournamentBracket.Winners,
                    roundNumber,
                    roundMatchIndexes.Count + 1,
                    matches.Count + 1,
                    current[index],
                    current[index + 1]);
                roundMatchIndexes.Add(matches.Count);
                matches.Add(match);
                next.Add(TournamentParticipantSource.WinnerOf(match.Id));
            }

            if (roundBye is not null)
                next.Insert(0, roundBye);

            if (next.Count > 1 && next.Count % 2 == 1 && roundBye is null)
            {
                var qualifierOffset = random.Next(roundMatchIndexes.Count);
                var qualifierIndex = roundMatchIndexes[qualifierOffset];
                matches[qualifierIndex] = matches[qualifierIndex] with { IsSeedQualifier = true };
                earnedBye = TournamentParticipantSource.WinnerOf(matches[qualifierIndex].Id);
            }

            current = next;
            roundNumber++;
        }

        return new TournamentSchedule(TournamentFormat.SingleElimination, entries, matches, byes);
    }

    public static TournamentSchedule GenerateDoubleElimination(
        IEnumerable<int> entryIds,
        int? randomSeed = null)
    {
        var entries = ValidateEntries(entryIds, 256);
        var random = CreateRandom(randomSeed);
        var bracketSize = NextPowerOfTwo(entries.Count);
        var rounds = (int)Math.Log2(bracketSize);
        var slots = entries
            .Select<int, TournamentParticipantSource?>(TournamentParticipantSource.Entry)
            .Concat(Enumerable.Repeat<TournamentParticipantSource?>(null, bracketSize - entries.Count))
            .ToList();
        Shuffle(slots, random);

        var builder = new DoubleEliminationBuilder();
        var winnersRoundLosers = new List<List<TournamentParticipantSource?>>();
        var current = slots;

        for (var round = 1; round <= rounds; round++)
        {
            var next = new List<TournamentParticipantSource?>();
            var losers = new List<TournamentParticipantSource?>();
            for (var index = 0; index < current.Count; index += 2)
            {
                var result = builder.CreateOrAdvance(
                    TournamentBracket.Winners,
                    round,
                    (index / 2) + 1,
                    current[index],
                    current[index + 1]);
                next.Add(result.Winner);
                losers.Add(result.Loser);
            }

            winnersRoundLosers.Add(losers);
            current = next;
        }

        var winnersChampion = current.Single()
            ?? throw new InvalidOperationException("The winners bracket did not produce a champion.");

        TournamentParticipantSource? losersChampion;
        if (rounds == 1)
        {
            losersChampion = winnersRoundLosers[0].Single();
        }
        else
        {
            var firstRoundLosers = winnersRoundLosers[0];
            var lower = new List<TournamentParticipantSource?>();
            for (var index = 0; index < firstRoundLosers.Count / 2; index++)
            {
                lower.Add(builder.CreateOrAdvance(
                    TournamentBracket.Losers,
                    1,
                    index + 1,
                    firstRoundLosers[index],
                    firstRoundLosers[firstRoundLosers.Count - 1 - index]).Winner);
            }

            for (var winnersRound = 2; winnersRound <= rounds; winnersRound++)
            {
                var incomingLosers = winnersRoundLosers[winnersRound - 1];
                var minorRound = (2 * winnersRound) - 2;
                var minorWinners = new List<TournamentParticipantSource?>();
                for (var index = 0; index < lower.Count; index++)
                {
                    minorWinners.Add(builder.CreateOrAdvance(
                        TournamentBracket.Losers,
                        minorRound,
                        index + 1,
                        lower[index],
                        incomingLosers[incomingLosers.Count - 1 - index]).Winner);
                }

                if (winnersRound == rounds)
                {
                    lower = minorWinners;
                    continue;
                }

                var majorRound = minorRound + 1;
                lower = new List<TournamentParticipantSource?>();
                for (var index = 0; index < minorWinners.Count; index += 2)
                {
                    lower.Add(builder.CreateOrAdvance(
                        TournamentBracket.Losers,
                        majorRound,
                        (index / 2) + 1,
                        minorWinners[index],
                        minorWinners[index + 1]).Winner);
                }
            }

            losersChampion = lower.Single();
        }

        if (losersChampion is null)
            throw new InvalidOperationException("The losers bracket did not produce a champion.");

        var grandFinal = builder.CreateRequiredMatch(
            TournamentBracket.GrandFinal,
            1,
            1,
            winnersChampion,
            losersChampion);
        builder.CreateRequiredMatch(
            TournamentBracket.GrandFinal,
            2,
            1,
            TournamentParticipantSource.WinnerOf(grandFinal.Id),
            TournamentParticipantSource.LoserOf(grandFinal.Id),
            isResetFinal: true);

        return builder.Build(entries, rounds);
    }

    public static TournamentSchedule GenerateRoundRobin(IEnumerable<int> entryIds)
    {
        var entries = ValidateEntries(entryIds, 32);
        var rotation = entries.Cast<int?>().ToList();
        if (rotation.Count % 2 == 1)
            rotation.Add(null);

        var matches = new List<TournamentMatchDefinition>();
        var byes = new List<TournamentByeDefinition>();
        var matchId = 1;
        var sequence = 1;

        for (var round = 1; round < rotation.Count; round++)
        {
            var roundMatch = 1;
            for (var index = 0; index < rotation.Count / 2; index++)
            {
                var a = rotation[index];
                var b = rotation[rotation.Count - 1 - index];
                if (a is null || b is null)
                {
                    byes.Add(new TournamentByeDefinition(
                        TournamentBracket.RoundRobin,
                        round,
                        1,
                        TournamentParticipantSource.Entry((a ?? b)!.Value)));
                    continue;
                }

                matches.Add(new TournamentMatchDefinition(
                    matchId++,
                    TournamentBracket.RoundRobin,
                    round,
                    roundMatch++,
                    sequence++,
                    TournamentParticipantSource.Entry(a.Value),
                    TournamentParticipantSource.Entry(b.Value)));
            }

            var last = rotation[^1];
            rotation.RemoveAt(rotation.Count - 1);
            rotation.Insert(1, last);
        }

        return new TournamentSchedule(TournamentFormat.RoundRobin, entries, matches, byes);
    }

    private static List<int> ValidateEntries(IEnumerable<int> entryIds, int maximum)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var entries = entryIds.ToList();
        if (entries.Count < 2 || entries.Count > maximum)
            throw new ArgumentOutOfRangeException(nameof(entryIds), $"Entry count must be between 2 and {maximum}.");
        if (entries.Any(x => x <= 0))
            throw new ArgumentException("Entry identifiers must be positive.", nameof(entryIds));
        if (entries.Distinct().Count() != entries.Count)
            throw new ArgumentException("Entry identifiers must be unique.", nameof(entryIds));
        return entries;
    }

    private static Random CreateRandom(int? seed) => seed.HasValue ? new Random(seed.Value) : new Random();

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result *= 2;
        return result;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapWith = random.Next(index + 1);
            (values[index], values[swapWith]) = (values[swapWith], values[index]);
        }
    }

    private sealed class DoubleEliminationBuilder
    {
        private readonly List<TournamentMatchDefinition> _matches = [];
        private readonly List<TournamentByeDefinition> _byes = [];
        private int _nextMatchId = 1;

        public (TournamentParticipantSource? Winner, TournamentParticipantSource? Loser) CreateOrAdvance(
            TournamentBracket bracket,
            int round,
            int position,
            TournamentParticipantSource? sideA,
            TournamentParticipantSource? sideB)
        {
            if (sideA is null && sideB is null)
                return (null, null);

            if (sideA is null || sideB is null)
            {
                var participant = sideA ?? sideB!;
                _byes.Add(new TournamentByeDefinition(bracket, round, position, participant));
                return (participant, null);
            }

            var match = CreateRequiredMatch(bracket, round, position, sideA, sideB);
            return (TournamentParticipantSource.WinnerOf(match.Id), TournamentParticipantSource.LoserOf(match.Id));
        }

        public TournamentMatchDefinition CreateRequiredMatch(
            TournamentBracket bracket,
            int round,
            int position,
            TournamentParticipantSource sideA,
            TournamentParticipantSource sideB,
            bool isResetFinal = false)
        {
            var match = new TournamentMatchDefinition(
                _nextMatchId++,
                bracket,
                round,
                position,
                0,
                sideA,
                sideB,
                IsResetFinal: isResetFinal);
            _matches.Add(match);
            return match;
        }

        public TournamentSchedule Build(IReadOnlyList<int> entries, int winnersRounds)
        {
            var ordered = _matches
                .OrderBy(match => StageOrder(match, winnersRounds))
                .ThenBy(match => match.MatchNumber)
                .Select((match, index) => match with { SequenceNumber = index + 1 })
                .ToList();
            return new TournamentSchedule(TournamentFormat.DoubleElimination, entries, ordered, _byes);
        }

        private static int StageOrder(TournamentMatchDefinition match, int winnersRounds)
        {
            if (match.Bracket == TournamentBracket.GrandFinal)
                return (3 * winnersRounds) - 2 + match.RoundNumber;
            if (match.Bracket == TournamentBracket.Winners)
                return match.RoundNumber == 1 ? 1 : (3 * match.RoundNumber) - 3;
            if (match.RoundNumber == 1)
                return 2;
            return match.RoundNumber % 2 == 0
                ? (3 * ((match.RoundNumber + 2) / 2)) - 2
                : (3 * ((match.RoundNumber + 1) / 2)) - 1;
        }
    }
}
