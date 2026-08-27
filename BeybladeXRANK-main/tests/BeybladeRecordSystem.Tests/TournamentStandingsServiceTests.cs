using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;

namespace BeybladeRecordSystem.Tests;

public class TournamentStandingsServiceTests
{
    [Fact]
    public void RoundRobin_TwoEntryTieUsesHeadToHeadBeforeOverallScoreDifference()
    {
        var tournament = CreateTournament(TournamentFormat.RoundRobin, 4);
        tournament.Matches = [
            Played(1, 2, 1, 4, 3),
            Played(1, 3, 1, 4, 3),
            Played(4, 1, 4, 4, 0),
            Played(2, 3, 2, 4, 0),
            Played(2, 4, 2, 4, 0),
            Played(3, 4, 3, 4, 0)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([1, 2, 3, 4], rows.Select(x => x.EntryId));
        Assert.True(rows[0].ScoreDifference < rows[1].ScoreDifference);
        Assert.Equal(1, rows[0].DirectEncounterWins);
        Assert.Equal(1, rows[2].DirectEncounterWins);
    }

    [Fact]
    public void RoundRobin_ThreeEntryTieUsesOnlyScoredMatchesInsideTieGroup()
    {
        var tournament = CreateTournament(TournamentFormat.RoundRobin, 3);
        tournament.Matches = [
            Played(1, 2, 1, 4, 3),
            Played(2, 3, 2, 4, 0),
            Walkover(3, 1, 3)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([2, 1, 3], rows.Select(x => x.EntryId));
        Assert.Equal([3, 1, -4], rows.Select(x => x.GroupScoreDifference));
        var walkoverWinner = rows.Single(x => x.EntryId == 3);
        Assert.Equal(1, walkoverWinner.Wins);
        Assert.Equal(0, walkoverWinner.PointsFor);
    }

    [Fact]
    public void Swiss_UsesBuchholzThenOpponentRateThenScoreAndExcludesWalkoverScore()
    {
        var tournament = CreateTournament(TournamentFormat.Swiss, 4);
        tournament.Matches = [
            Played(1, 2, 1, 4, 0, round: 1),
            Played(3, 4, 3, 4, 3, round: 1),
            Played(1, 3, 1, 4, 2, round: 2),
            Walkover(2, 4, 2, round: 2)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal(1, rows[0].EntryId);
        Assert.Equal(3, rows[1].EntryId);
        Assert.Equal(2, rows[2].EntryId);
        Assert.Equal(rows[1].Buchholz, rows[2].Buchholz);
        Assert.Equal(rows[1].OpponentWinRate, rows[2].OpponentWinRate);
        Assert.True(rows[1].ScoreDifference > rows[2].ScoreDifference);
        Assert.Equal(1, rows[2].Wins);
        Assert.Equal(0, rows[2].PointsFor);
    }

    [Fact]
    public void Swiss_ByeDoesNotCountAsWinOrInventPoints()
    {
        var tournament = CreateTournament(TournamentFormat.Swiss, 3);
        tournament.Matches = [
            Played(1, 2, 1, 4, 2),
            Bye(3)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);
        var bye = rows.Single(x => x.EntryId == 3);

        Assert.Equal(0, bye.Wins);
        Assert.Equal(0, bye.Losses);
        Assert.Equal(0, bye.Buchholz);
        Assert.Equal(0, bye.ScoreDifference);
        Assert.Equal(0, bye.PointsFor);
    }

    [Fact]
    public void RoundRobin_CompleteTieForFirstRequiresPlayoffAndPlayoffOnlyOverridesChampion()
    {
        var tournament = CreateTournament(TournamentFormat.RoundRobin, 4);
        tournament.Status = TournamentStatus.InProgress;
        tournament.Matches =
        [
            Played(1, 2, 1, 4, 0),
            Played(2, 3, 2, 4, 0),
            Played(3, 1, 3, 4, 0),
            Played(1, 4, 1, 4, 0),
            Played(2, 4, 2, 4, 0),
            Played(3, 4, 3, 4, 0)
        ];

        Assert.Equal([1, 2, 3], TournamentStandingsService.GetRequiredChampionPlayoffEntryIds(tournament));

        tournament.Matches.Add(EliminationResult(TournamentBracket.Playoff, 1, 7, 1, 2, 1));
        tournament.Matches.Add(EliminationResult(TournamentBracket.Playoff, 2, 8, 3, 1, 3));
        tournament.Status = TournamentStatus.Completed;
        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([3, 1, 2, 4], rows.Select(x => x.EntryId));
        Assert.Equal([1, 2, 2, 4], rows.Select(x => x.Rank));
        Assert.Equal(TournamentStandingPlacement.Champion, rows[0].Placement);
        Assert.Equal(2, rows[0].Wins);
        Assert.All(rows.Where(x => x.Rank == 2), x => Assert.True(x.IsTied));
    }

    [Fact]
    public void RoundRobin_CompleteTieBelowFirstRemainsTiedWithoutPlayoff()
    {
        var tournament = CreateTournament(TournamentFormat.RoundRobin, 4);
        tournament.Status = TournamentStatus.InProgress;
        tournament.Matches =
        [
            Played(1, 2, 1, 4, 0),
            Played(1, 3, 1, 4, 0),
            Played(1, 4, 1, 4, 0),
            Played(2, 3, 2, 4, 0),
            Played(3, 4, 3, 4, 0),
            Played(4, 2, 4, 4, 0)
        ];

        Assert.Empty(TournamentStandingsService.GetRequiredChampionPlayoffEntryIds(tournament));
        var rows = TournamentStandingsService.Calculate(tournament);
        Assert.Equal(1, rows[0].Rank);
        Assert.All(rows.Skip(1), x => Assert.Equal(2, x.Rank));
    }

    [Fact]
    public void Swiss_OnlyRequestsChampionPlayoffAfterAllRequiredRounds()
    {
        var tournament = CreateTournament(TournamentFormat.Swiss, 4);
        tournament.Status = TournamentStatus.InProgress;
        tournament.Matches =
        [
            Played(1, 3, 1, 4, 0, round: 1, bracket: TournamentBracket.Swiss),
            Played(2, 4, 2, 4, 0, round: 1, bracket: TournamentBracket.Swiss)
        ];

        Assert.Empty(TournamentStandingsService.GetRequiredChampionPlayoffEntryIds(tournament));

        tournament.Matches.Add(Played(1, 4, 1, 4, 0, round: 2, bracket: TournamentBracket.Swiss));
        tournament.Matches.Add(Played(2, 3, 2, 4, 0, round: 2, bracket: TournamentBracket.Swiss));
        Assert.Equal([1, 2], TournamentStandingsService.GetRequiredChampionPlayoffEntryIds(tournament));
    }

    [Fact]
    public void SingleElimination_RanksFinalistsAndTiesEntriesEliminatedInTheSameRound()
    {
        var tournament = CreateTournament(TournamentFormat.SingleElimination, 8);
        tournament.Status = TournamentStatus.Completed;
        tournament.Matches =
        [
            EliminationResult(TournamentBracket.Winners, 1, 1, 1, 8, 1, scoreA: 4, scoreB: 1),
            EliminationResult(TournamentBracket.Winners, 1, 2, 2, 7, 2, TournamentMatchStatus.Walkover),
            EliminationResult(TournamentBracket.Winners, 1, 3, 3, 6, 3),
            EliminationResult(TournamentBracket.Winners, 1, 4, 4, 5, 4),
            EliminationResult(TournamentBracket.Winners, 2, 5, 1, 4, 1),
            EliminationResult(TournamentBracket.Winners, 2, 6, 2, 3, 2),
            EliminationResult(TournamentBracket.Winners, 3, 7, 1, 2, 1)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], rows.Select(x => x.EntryId));
        Assert.Equal([1, 2, 3, 3, 5, 5, 5, 5], rows.Select(x => x.Rank));
        Assert.Equal(TournamentStandingPlacement.Champion, rows[0].Placement);
        Assert.Equal(TournamentStandingPlacement.RunnerUp, rows[1].Placement);
        Assert.All(rows.Skip(2), x => Assert.Equal(TournamentStandingPlacement.Eliminated, x.Placement));
        Assert.True(rows[2].IsTied);
        Assert.True(rows[4].IsTied);
        var walkoverLoser = rows.Single(x => x.EntryId == 7);
        Assert.Equal(0, walkoverLoser.PointsFor);
        Assert.Equal(0, walkoverLoser.ScoreDifference);
    }

    [Fact]
    public void SingleElimination_DoesNotPublishFormalPlacementsBeforeTournamentCompletion()
    {
        var tournament = CreateTournament(TournamentFormat.SingleElimination, 2);
        tournament.Status = TournamentStatus.InProgress;
        tournament.Matches = [EliminationResult(TournamentBracket.Winners, 1, 1, 1, 2, 1)];

        Assert.Empty(TournamentStandingsService.Calculate(tournament));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void DoubleElimination_UsesTheDecisiveGrandFinalForChampionAndRunnerUp(
        bool resetWasRequired,
        int expectedChampionLosses)
    {
        var tournament = CreateTournament(TournamentFormat.DoubleElimination, 4);
        tournament.Status = TournamentStatus.Completed;
        tournament.Matches =
        [
            EliminationResult(TournamentBracket.Winners, 1, 1, 1, 4, 1),
            EliminationResult(TournamentBracket.Winners, 1, 2, 2, 3, 2),
            EliminationResult(TournamentBracket.Losers, 1, 3, 3, 4, 3),
            EliminationResult(TournamentBracket.Winners, 2, 4, 1, 2, 1),
            EliminationResult(TournamentBracket.Losers, 2, 5, 2, 3, 2),
            resetWasRequired
                ? EliminationResult(TournamentBracket.GrandFinal, 1, 6, 1, 2, 2)
                : EliminationResult(TournamentBracket.GrandFinal, 1, 6, 1, 2, 1),
            resetWasRequired
                ? EliminationResult(TournamentBracket.GrandFinal, 2, 7, 1, 2, 1, isResetFinal: true)
                : EliminationResult(TournamentBracket.GrandFinal, 2, 7, 1, 2, 1,
                    TournamentMatchStatus.NotRequired, isResetFinal: true)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([1, 2, 3, 4], rows.Select(x => x.EntryId));
        Assert.Equal([1, 2, 3, 4], rows.Select(x => x.Rank));
        Assert.Equal(expectedChampionLosses, rows[0].Losses);
        Assert.Equal(TournamentStandingPlacement.Champion, rows[0].Placement);
        Assert.Equal(TournamentStandingPlacement.RunnerUp, rows[1].Placement);
        Assert.Equal(TournamentBracket.GrandFinal, rows[1].EliminationBracket);
    }

    [Fact]
    public void DoubleElimination_TiesEntriesWhoseSecondLossOccursInTheSameLosersRound()
    {
        var tournament = CreateTournament(TournamentFormat.DoubleElimination, 8);
        tournament.Status = TournamentStatus.Completed;
        tournament.Matches =
        [
            EliminationResult(TournamentBracket.Winners, 1, 1, 1, 5, 1),
            EliminationResult(TournamentBracket.Winners, 1, 2, 2, 6, 2),
            EliminationResult(TournamentBracket.Winners, 1, 3, 3, 7, 3),
            EliminationResult(TournamentBracket.Winners, 1, 4, 4, 8, 4),
            EliminationResult(TournamentBracket.Losers, 1, 5, 5, 6, 5),
            EliminationResult(TournamentBracket.Losers, 1, 6, 7, 8, 7),
            EliminationResult(TournamentBracket.Winners, 2, 7, 1, 4, 1),
            EliminationResult(TournamentBracket.Winners, 2, 8, 2, 3, 2),
            EliminationResult(TournamentBracket.Losers, 2, 9, 4, 5, 4),
            EliminationResult(TournamentBracket.Losers, 2, 10, 3, 7, 3),
            EliminationResult(TournamentBracket.Losers, 3, 11, 4, 3, 4),
            EliminationResult(TournamentBracket.Winners, 3, 12, 1, 2, 1),
            EliminationResult(TournamentBracket.Losers, 4, 13, 2, 4, 2),
            EliminationResult(TournamentBracket.GrandFinal, 1, 14, 1, 2, 1)
        ];

        var rows = TournamentStandingsService.Calculate(tournament);

        Assert.Equal([1, 2, 4, 3, 5, 7, 6, 8], rows.Select(x => x.EntryId));
        Assert.Equal([1, 2, 3, 4, 5, 5, 7, 7], rows.Select(x => x.Rank));
        Assert.Equal([4, 3, 2, 2, 1, 1], rows.Skip(2).Select(x => x.EliminationRoundNumber));
        Assert.All(rows.Where(x => x.Rank is 5 or 7), x => Assert.True(x.IsTied));
    }

    private static Tournament CreateTournament(TournamentFormat format, int entryCount)
    {
        var tournament = new Tournament { Format = format };
        for (var id = 1; id <= entryCount; id++)
            tournament.Entries.Add(new TournamentEntry { Id = id, DisplayNameSnapshot = $"Entry {id}" });
        return tournament;
    }

    private static TournamentMatch Played(
        int sideA,
        int sideB,
        int winner,
        int scoreA,
        int scoreB,
        int round = 1,
        TournamentBracket bracket = TournamentBracket.RoundRobin) => new()
    {
        Bracket = bracket,
        RoundNumber = round,
        MatchNumber = 1,
        SequenceNumber = 1,
        SideASourceKind = TournamentParticipantSourceKind.Entry,
        SideASourceReferenceId = sideA,
        SideBSourceKind = TournamentParticipantSourceKind.Entry,
        SideBSourceReferenceId = sideB,
        SideAEntryId = sideA,
        SideBEntryId = sideB,
        WinnerEntryId = winner,
        LoserEntryId = winner == sideA ? sideB : sideA,
        Status = TournamentMatchStatus.Completed,
        Battle = new Battle { Status = BattleStatus.Completed, SideAScore = scoreA, SideBScore = scoreB }
    };

    private static TournamentMatch Walkover(int sideA, int sideB, int winner, int round = 1) => new()
    {
        Bracket = TournamentBracket.RoundRobin,
        RoundNumber = round,
        MatchNumber = 1,
        SequenceNumber = 1,
        SideASourceKind = TournamentParticipantSourceKind.Entry,
        SideASourceReferenceId = sideA,
        SideBSourceKind = TournamentParticipantSourceKind.Entry,
        SideBSourceReferenceId = sideB,
        SideAEntryId = sideA,
        SideBEntryId = sideB,
        WinnerEntryId = winner,
        LoserEntryId = winner == sideA ? sideB : sideA,
        Status = TournamentMatchStatus.Walkover
    };

    private static TournamentMatch Bye(int entryId) => new()
    {
        Bracket = TournamentBracket.Swiss,
        RoundNumber = 1,
        MatchNumber = 1,
        SequenceNumber = 1,
        SideASourceKind = TournamentParticipantSourceKind.Entry,
        SideASourceReferenceId = entryId,
        SideAEntryId = entryId,
        WinnerEntryId = entryId,
        Status = TournamentMatchStatus.Completed,
        IsBye = true
    };

    private static TournamentMatch EliminationResult(
        TournamentBracket bracket,
        int round,
        int sequence,
        int sideA,
        int sideB,
        int winner,
        TournamentMatchStatus status = TournamentMatchStatus.Completed,
        bool isResetFinal = false,
        int? scoreA = null,
        int? scoreB = null) => new()
    {
        Bracket = bracket,
        RoundNumber = round,
        MatchNumber = sequence,
        SequenceNumber = sequence,
        SideASourceKind = TournamentParticipantSourceKind.Entry,
        SideASourceReferenceId = sideA,
        SideBSourceKind = TournamentParticipantSourceKind.Entry,
        SideBSourceReferenceId = sideB,
        SideAEntryId = sideA,
        SideBEntryId = sideB,
        WinnerEntryId = winner,
        LoserEntryId = winner == sideA ? sideB : sideA,
        Status = status,
        IsResetFinal = isResetFinal,
        Battle = scoreA is int actualScoreA && scoreB is int actualScoreB
            ? new Battle { Status = BattleStatus.Completed, SideAScore = actualScoreA, SideBScore = actualScoreB }
            : null
    };
}
