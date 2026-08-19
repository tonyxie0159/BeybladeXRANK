namespace BeybladeRecordSystem.Domain.Tournaments;

public enum TournamentFormat
{
    SingleElimination,
    DoubleElimination,
    RoundRobin,
    Swiss
}

public enum TournamentBracket
{
    Winners,
    Losers,
    GrandFinal,
    RoundRobin,
    Swiss
}

public enum TournamentParticipantSourceKind
{
    Entry,
    MatchWinner,
    MatchLoser
}

public sealed record TournamentParticipantSource(
    TournamentParticipantSourceKind Kind,
    int ReferenceId)
{
    public static TournamentParticipantSource Entry(int entryId) =>
        new(TournamentParticipantSourceKind.Entry, entryId);

    public static TournamentParticipantSource WinnerOf(int matchId) =>
        new(TournamentParticipantSourceKind.MatchWinner, matchId);

    public static TournamentParticipantSource LoserOf(int matchId) =>
        new(TournamentParticipantSourceKind.MatchLoser, matchId);
}

public sealed record TournamentMatchDefinition(
    int Id,
    TournamentBracket Bracket,
    int RoundNumber,
    int MatchNumber,
    int SequenceNumber,
    TournamentParticipantSource SideA,
    TournamentParticipantSource SideB,
    bool IsSeedQualifier = false,
    bool IsResetFinal = false);

public sealed record TournamentByeDefinition(
    TournamentBracket Bracket,
    int RoundNumber,
    int PositionNumber,
    TournamentParticipantSource Participant,
    bool IsSeedQualifierAdvancement = false);

public sealed record TournamentSchedule(
    TournamentFormat Format,
    IReadOnlyList<int> EntryIds,
    IReadOnlyList<TournamentMatchDefinition> Matches,
    IReadOnlyList<TournamentByeDefinition> Byes);

public sealed record SwissEntryStanding(
    int EntryId,
    int Wins,
    IReadOnlySet<int> OpponentIds,
    bool HadBye,
    int Buchholz = 0,
    decimal OpponentWinRate = 0,
    int ScoreDifference = 0,
    int PointsFor = 0);

public sealed record SwissPairing(int EntryAId, int EntryBId);

public sealed record SwissRoundDefinition(
    int RoundNumber,
    IReadOnlyList<SwissPairing> Pairings,
    int? ByeEntryId);
