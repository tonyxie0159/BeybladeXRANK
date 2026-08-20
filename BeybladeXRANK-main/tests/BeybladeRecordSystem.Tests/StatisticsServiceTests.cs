using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class StatisticsServiceTests
{
    [Fact]
    public async Task UserSections_KeepSourcesSeparate_AndSplitTeamResultFromActualRounds()
    {
        await using var fixture = await StatisticsFixture.CreateAsync();
        var service = new StatisticsService(fixture.Db);

        var sections = await service.GetUserStatisticsSectionsAsync(fixture.PlayerA.Id);

        Assert.Equal((1, 0, 1, 0), (
            sections.Quick.Wins,
            sections.Quick.Losses,
            sections.Quick.Score,
            sections.Quick.AgainstScore));
        Assert.Equal((1, 0, 1m), (
            sections.Quick.BSide.Wins,
            sections.Quick.BSide.Losses,
            sections.Quick.BSide.WinRate));
        Assert.Equal(0, sections.Quick.XSide.Samples);
        Assert.Equal((0, 1, 0, 2), (
            sections.TournamentIndividual.Wins,
            sections.TournamentIndividual.Losses,
            sections.TournamentIndividual.Score,
            sections.TournamentIndividual.AgainstScore));
        Assert.Equal((0, 1, 0m), (
            sections.TournamentIndividual.XSide.Wins,
            sections.TournamentIndividual.XSide.Losses,
            sections.TournamentIndividual.XSide.WinRate));
        Assert.Equal((1, 0, 3, 0), (
            sections.TournamentTeamResult.Wins,
            sections.TournamentTeamResult.Losses,
            sections.TournamentTeamResult.Score,
            sections.TournamentTeamResult.AgainstScore));
        Assert.Equal((1, 0, 3, 0), (
            sections.TournamentTeamRoundPerformance.Wins,
            sections.TournamentTeamRoundPerformance.Losses,
            sections.TournamentTeamRoundPerformance.Score,
            sections.TournamentTeamRoundPerformance.AgainstScore));
    }

    [Fact]
    public async Task BeybladeStatistics_FilterSources_CountSamples_AndKeepCancelledTournamentRounds()
    {
        await using var fixture = await StatisticsFixture.CreateAsync();
        var service = new StatisticsService(fixture.Db);

        var samples = await service.GetBeybladeSourceSamplesAsync(fixture.PlayerA.Id);
        var sideSamples = await service.GetBeybladeSideSamplesAsync(fixture.PlayerA.Id);
        var all = (await service.GetBeybladeStatisticsAsync(
            fixture.PlayerA.Id, null, StatisticsSourceFilter.All)).Single(x => x.Name == "A-Blade");
        var individual = (await service.GetBeybladeStatisticsAsync(
            fixture.PlayerA.Id, null, StatisticsSourceFilter.TournamentIndividual)).Single(x => x.Name == "A-Blade");
        var bSide = (await service.GetBeybladeStatisticsAsync(
            fixture.PlayerA.Id, "b-winrate-desc", StatisticsSourceFilter.All, StatisticsSideFilter.B)).First();
        var xSide = (await service.GetBeybladeStatisticsAsync(
            fixture.PlayerA.Id, null, StatisticsSourceFilter.All, StatisticsSideFilter.X)).Single(x => x.Name == "A-Blade");
        var personalRows = await service.GetUserStatisticsRowsAsync(
            fixture.PlayerA.Id, "b-winrate-desc", StatisticsSourceFilter.All, StatisticsSideFilter.All);
        var xSidePersonal = Assert.Single(await service.GetUserStatisticsRowsAsync(
            fixture.PlayerA.Id,
            "winrate-desc",
            StatisticsSourceFilter.TournamentIndividual,
            StatisticsSideFilter.X));

        Assert.Equal((4, 1, 2, 1), (
            samples.All,
            samples.Quick,
            samples.TournamentIndividual,
            samples.TournamentTeam));
        Assert.Equal((4, 2, 2, 0), (
            sideSamples.All,
            sideSamples.B,
            sideSamples.X,
            sideSamples.Unassigned));
        Assert.Equal((3, 1, 6, 2, 4), (
            all.Wins,
            all.Losses,
            all.Score,
            all.AgainstScore,
            all.RoundCount));
        Assert.Equal((1.5m, 0.5m), (all.AverageScore, all.AverageAgainstScore));
        Assert.Equal(1, all.ResultTypes.SpinFinishFor);
        Assert.Equal(1, all.ResultTypes.KnockOutFor);
        Assert.Equal(1, all.ResultTypes.ExtremeFor);
        Assert.Equal(1, all.ResultTypes.BurstAgainst);
        Assert.Equal((2, 0, 1m), (all.BSide.Wins, all.BSide.Losses, all.BSide.WinRate));
        Assert.Equal((1, 1, 0.5m), (all.XSide.Wins, all.XSide.Losses, all.XSide.WinRate));
        Assert.Equal((1, 1, 2, 2, 2), (
            individual.Wins,
            individual.Losses,
            individual.Score,
            individual.AgainstScore,
            individual.RoundCount));
        Assert.Equal("A-Blade", bSide.Name);
        Assert.Equal((2, 0, 4, 0, 2), (bSide.Wins, bSide.Losses, bSide.Score, bSide.AgainstScore, bSide.RoundCount));
        Assert.Equal((1, 1, 2, 2, 2), (xSide.Wins, xSide.Losses, xSide.Score, xSide.AgainstScore, xSide.RoundCount));
        Assert.Equal("錦標賽個人賽", personalRows.Last().Label);
        Assert.Equal((0, 1, 0, 2), (
            xSidePersonal.Summary.Wins,
            xSidePersonal.Summary.Losses,
            xSidePersonal.Summary.Score,
            xSidePersonal.Summary.AgainstScore));
    }

    [Fact]
    public async Task OpponentsAndHistory_UseActualTeamRoundOpponent_AndExcludeCancelledBattleResult()
    {
        await using var fixture = await StatisticsFixture.CreateAsync();
        var service = new StatisticsService(fixture.Db);

        var teamOpponents = await service.GetOpponentStatisticsAsync(
            fixture.PlayerA.Id,
            StatisticsSourceFilter.TournamentTeam);
        var individualOpponents = await service.GetOpponentStatisticsAsync(
            fixture.PlayerA.Id,
            StatisticsSourceFilter.TournamentIndividual);
        var history = await service.GetBattleHistoryAsync(fixture.PlayerA.Id, StatisticsSourceFilter.All);
        var pairings = await service.GetOpponentBeybladeStatisticsAsync(
            fixture.PlayerA.Id,
            fixture.PlayerB.Id,
            StatisticsSourceFilter.TournamentIndividual);

        var teamOpponent = Assert.Single(teamOpponents);
        Assert.Equal(fixture.PlayerC.Id, teamOpponent.OpponentId);
        Assert.Equal((1, 0, 3, 0), (
            teamOpponent.Wins,
            teamOpponent.Losses,
            teamOpponent.Score,
            teamOpponent.AgainstScore));
        Assert.Equal((0, 1), (
            Assert.Single(individualOpponents).Wins,
            Assert.Single(individualOpponents).Losses));
        Assert.Equal(3, history.Count);
        Assert.DoesNotContain(history, x => x.BattleId == fixture.CancelledBattleId);
        var teamHistory = Assert.Single(history, x => x.SourceType == BattleSourceType.TournamentTeam);
        Assert.True(teamHistory.IsTeamResult);
        Assert.Equal("Team C", teamHistory.OpponentDisplayName);
        Assert.Equal(BattleSide.B, teamHistory.Side);
        Assert.Equal(BattleSide.X, Assert.Single(history, x => x.SourceType == BattleSourceType.TournamentIndividual).Side);
        var pairing = Assert.Single(pairings);
        Assert.Equal((1, 1, 2, 2), (pairing.Wins, pairing.Losses, pairing.Score, pairing.AgainstScore));
    }

    private sealed class StatisticsFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        public AppDbContext Db { get; }
        public User PlayerA { get; private init; } = null!;
        public User PlayerB { get; private init; } = null!;
        public User PlayerC { get; private init; } = null!;
        public int CancelledBattleId { get; private set; }

        private StatisticsFixture(SqliteConnection connection, AppDbContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public static async Task<StatisticsFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var playerA = CreateUser("player-a", "A", now);
            var playerB = CreateUser("player-b", "B", now);
            var playerC = CreateUser("player-c", "C", now);
            db.Users.AddRange(playerA, playerB, playerC);
            await db.SaveChangesAsync();

            var bladeA = CreateBlade(playerA.Id, "A-Blade", now);
            var unusedBladeA = CreateBlade(playerA.Id, "A-Unused", now);
            var bladeB = CreateBlade(playerB.Id, "B-Blade", now);
            var bladeC = CreateBlade(playerC.Id, "C-Blade", now);
            db.Beyblades.AddRange(bladeA, unusedBladeA, bladeB, bladeC);
            await db.SaveChangesAsync();

            var fixture = new StatisticsFixture(connection, db)
            {
                PlayerA = playerA,
                PlayerB = playerB,
                PlayerC = playerC
            };

            await fixture.AddBattleAsync(
                BattleSourceType.Quick,
                BattleStatus.Completed,
                playerA,
                playerB,
                bladeA,
                bladeB,
                playerA.Id,
                ResultType.SpinFinish,
                1,
                0,
                null,
                BattleSide.B,
                now.AddMinutes(1));

            var individualMatch = await fixture.CreateTournamentMatchAsync(
                TournamentMode.Individual,
                playerA,
                playerB,
                false,
                TournamentMatchStatus.Completed,
                now.AddMinutes(2));
            await fixture.AddBattleAsync(
                BattleSourceType.TournamentIndividual,
                BattleStatus.Completed,
                playerA,
                playerB,
                bladeA,
                bladeB,
                playerB.Id,
                ResultType.Burst,
                0,
                2,
                individualMatch,
                BattleSide.X,
                now.AddMinutes(2));

            var teamMatch = await fixture.CreateTournamentMatchAsync(
                TournamentMode.Team,
                playerA,
                playerC,
                true,
                TournamentMatchStatus.Completed,
                now.AddMinutes(3));
            await fixture.AddBattleAsync(
                BattleSourceType.TournamentTeam,
                BattleStatus.Completed,
                playerA,
                playerC,
                bladeA,
                bladeC,
                playerA.Id,
                ResultType.Extreme,
                3,
                0,
                teamMatch,
                BattleSide.B,
                now.AddMinutes(3));

            var cancelledMatch = await fixture.CreateTournamentMatchAsync(
                TournamentMode.Individual,
                playerA,
                playerB,
                null,
                TournamentMatchStatus.Cancelled,
                now.AddMinutes(4));
            fixture.CancelledBattleId = await fixture.AddBattleAsync(
                BattleSourceType.TournamentIndividual,
                BattleStatus.Cancelled,
                playerA,
                playerB,
                bladeA,
                bladeB,
                playerA.Id,
                ResultType.KnockOut,
                2,
                0,
                cancelledMatch,
                BattleSide.X,
                now.AddMinutes(4));

            db.ChangeTracker.Clear();
            return fixture;
        }

        private async Task<TournamentMatch> CreateTournamentMatchAsync(
            TournamentMode mode,
            User sideAUser,
            User sideBUser,
            bool? sideAWins,
            TournamentMatchStatus status,
            DateTime now)
        {
            var tournament = new Tournament
            {
                Name = $"{mode}-{now.Ticks}",
                Mode = mode,
                Format = TournamentFormat.SingleElimination,
                RegistrationMode = mode == TournamentMode.Team
                    ? TournamentRegistrationMode.CompleteTeam
                    : TournamentRegistrationMode.Individual,
                RuleSet = mode == TournamentMode.Team
                    ? TournamentRuleSet.DuoFourBladeSixPoints
                    : TournamentRuleSet.IndividualThreeBladeFourPoints,
                Status = status == TournamentMatchStatus.Cancelled
                    ? TournamentStatus.Cancelled
                    : TournamentStatus.Completed,
                RegistrationStage = TournamentRegistrationStage.Closed,
                TeamSize = mode == TournamentMode.Team ? 2 : null,
                BeybladesPerPlayer = mode == TournamentMode.Team ? 2 : 3,
                ScoreToWin = mode == TournamentMode.Team ? 6 : 4,
                TargetEntryCount = 2,
                OrganizerUserId = PlayerA.Id,
                RulesSnapshot = "test",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = [1]
            };
            Db.Tournaments.Add(tournament);
            await Db.SaveChangesAsync();

            var sideA = new TournamentEntry
            {
                TournamentId = tournament.Id,
                RegistrationNumber = "A",
                SchedulePosition = 1,
                DisplayNameSnapshot = mode == TournamentMode.Team ? "Team A" : sideAUser.DisplayName,
                TeamName = mode == TournamentMode.Team ? "Team A" : null,
                IndividualUserId = mode == TournamentMode.Individual ? sideAUser.Id : null,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RegisteredAtUtc = now
            };
            var sideB = new TournamentEntry
            {
                TournamentId = tournament.Id,
                RegistrationNumber = "B",
                SchedulePosition = 2,
                DisplayNameSnapshot = mode == TournamentMode.Team ? "Team C" : sideBUser.DisplayName,
                TeamName = mode == TournamentMode.Team ? "Team C" : null,
                IndividualUserId = mode == TournamentMode.Individual ? sideBUser.Id : null,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RegisteredAtUtc = now
            };
            Db.TournamentEntries.AddRange(sideA, sideB);
            await Db.SaveChangesAsync();

            var match = new TournamentMatch
            {
                TournamentId = tournament.Id,
                Bracket = TournamentBracket.Winners,
                RoundNumber = 1,
                MatchNumber = 1,
                SequenceNumber = 1,
                Status = status,
                SideASourceKind = TournamentParticipantSourceKind.Entry,
                SideASourceReferenceId = sideA.Id,
                SideBSourceKind = TournamentParticipantSourceKind.Entry,
                SideBSourceReferenceId = sideB.Id,
                SideAEntryId = sideA.Id,
                SideBEntryId = sideB.Id,
                WinnerEntryId = sideAWins.HasValue ? (sideAWins.Value ? sideA.Id : sideB.Id) : null,
                LoserEntryId = sideAWins.HasValue ? (sideAWins.Value ? sideB.Id : sideA.Id) : null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = status == TournamentMatchStatus.Completed ? now : null,
                Version = [1]
            };
            Db.TournamentMatches.Add(match);
            await Db.SaveChangesAsync();

            Db.TournamentMatchParticipants.AddRange(
                CreateParticipant(match.Id, sideA.Id, sideAUser.Id, now),
                CreateParticipant(match.Id, sideB.Id, sideBUser.Id, now));
            await Db.SaveChangesAsync();
            return match;
        }

        private async Task<int> AddBattleAsync(
            BattleSourceType source,
            BattleStatus status,
            User playerA,
            User playerB,
            Beyblade bladeA,
            Beyblade bladeB,
            int winnerPlayerId,
            ResultType resultType,
            int sideAScore,
            int sideBScore,
            TournamentMatch? match,
            BattleSide sideADesignation,
            DateTime now)
        {
            var isTeam = source == BattleSourceType.TournamentTeam;
            var battle = new Battle
            {
                SourceType = source,
                ScoreToWin = source == BattleSourceType.TournamentTeam ? 6 : 4,
                TournamentMatchId = match?.Id,
                PlayerAId = isTeam ? null : playerA.Id,
                PlayerBId = isTeam ? null : playerB.Id,
                CreatedByUserId = PlayerA.Id,
                Status = status,
                SideAScore = sideAScore,
                SideBScore = sideBScore,
                SideADesignation = sideADesignation,
                WinningSide = sideAScore > sideBScore
                    ? sideADesignation
                    : sideADesignation == BattleSide.B ? BattleSide.X : BattleSide.B,
                WinningPlayerId = isTeam || status == BattleStatus.Cancelled ? null : winnerPlayerId,
                CreatedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = status == BattleStatus.Completed ? now : null,
                Version = [1]
            };
            var lineup = new BattleLineup
            {
                Battle = battle,
                SequenceNo = 1,
                PositionNo = 1,
                PlayerAId = playerA.Id,
                PlayerADisplayNameSnapshot = playerA.DisplayName,
                PlayerABeybladeId = bladeA.Id,
                PlayerABeybladeNameSnapshot = bladeA.Name,
                PlayerBId = playerB.Id,
                PlayerBDisplayNameSnapshot = playerB.DisplayName,
                PlayerBBeybladeId = bladeB.Id,
                PlayerBBeybladeNameSnapshot = bladeB.Name,
                IsCurrent = true
            };
            var round = new BattleRound
            {
                Battle = battle,
                Lineup = lineup,
                RoundNo = 1,
                PositionNo = 1,
                PlayerAId = playerA.Id,
                PlayerADisplayNameSnapshot = playerA.DisplayName,
                PlayerABeybladeId = bladeA.Id,
                PlayerABeybladeNameSnapshot = bladeA.Name,
                PlayerBId = playerB.Id,
                PlayerBDisplayNameSnapshot = playerB.DisplayName,
                PlayerBBeybladeId = bladeB.Id,
                PlayerBBeybladeNameSnapshot = bladeB.Name,
                Status = BattleRoundStatus.Completed,
                CreatedAtUtc = now,
                CompletedAtUtc = now
            };
            round.Events.Add(new BattleRoundEvent
            {
                EventSequence = 1,
                EventType = BattleRoundEventType.BattleResult,
                WinnerPlayerId = winnerPlayerId,
                ResultType = resultType,
                ScoreAwarded = resultType switch
                {
                    ResultType.SpinFinish => 1,
                    ResultType.KnockOut => 2,
                    ResultType.Burst => 2,
                    _ => 3
                },
                IsEffective = true,
                CreatedAtUtc = now
            });
            battle.Lineups.Add(lineup);
            battle.Rounds.Add(round);
            Db.Battles.Add(battle);
            await Db.SaveChangesAsync();
            return battle.Id;
        }

        private static TournamentMatchParticipant CreateParticipant(
            int matchId,
            int entryId,
            int userId,
            DateTime now) => new()
            {
                TournamentMatchId = matchId,
                TournamentEntryId = entryId,
                UserId = userId,
                Status = TournamentParticipationStatus.Accepted,
                NotifiedAtUtc = now,
                RespondedAtUtc = now,
                Version = [1]
            };

        private static User CreateUser(string account, string displayName, DateTime now) => new()
        {
            Account = account,
            PasswordHash = "hash",
            DisplayName = displayName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        private static Beyblade CreateBlade(int userId, string name, DateTime now) => new()
        {
            UserId = userId,
            Name = name,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
