using System.Net;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeybladeRecordSystem.Tests;

public sealed partial class AccountWebTests
{
    [Fact]
    public async Task RepeatedCompleteRoundPosts_DoNotDuplicateScoreOrCreateExtraRounds()
    {
        using var operatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var opponentClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var operatorAccount = $"round-operator-{suffix}";
        var opponentAccount = $"round-opponent-{suffix}";
        const string password = "phase seven round password";
        await RegisterAsync(operatorClient, operatorAccount, password, "Round Operator");
        await LoginAsync(operatorClient, operatorAccount, password);
        await RegisterAsync(opponentClient, opponentAccount, password, "Round Opponent");

        int battleId;
        int firstRoundId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operatorUser = await db.Users.SingleAsync(x => x.Account == operatorAccount);
            var opponent = await db.Users.SingleAsync(x => x.Account == opponentAccount);
            var now = DateTime.UtcNow;
            var operatorBlades = Enumerable.Range(1, 3)
                .Select(index => new Beyblade
                {
                    UserId = operatorUser.Id,
                    Name = $"Round A{index}-{suffix}",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                })
                .ToList();
            var opponentBlades = Enumerable.Range(1, 3)
                .Select(index => new Beyblade
                {
                    UserId = opponent.Id,
                    Name = $"Round B{index}-{suffix}",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                })
                .ToList();
            db.Beyblades.AddRange(operatorBlades.Concat(opponentBlades));
            await db.SaveChangesAsync();

            var flow = new QuickBattleFlowService(db);
            var invitation = (await flow.SendInvitationAsync(operatorUser.Id, opponent.Id)).Value!;
            battleId = (await flow.AcceptInvitationAsync(invitation.Id, opponent.Id)).Value;
            Assert.True((await flow.SubmitLineupAsync(
                battleId, operatorUser.Id, operatorBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.SubmitLineupAsync(
                battleId, opponent.Id, opponentBlades.Select(x => x.Id).ToList())).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, operatorUser.Id)).Succeeded);
            Assert.True((await flow.ConfirmLineupAsync(battleId, opponent.Id)).Succeeded);

            var battleService = new BattleService(db);
            Assert.True((await battleService.AssignSidesAsync(battleId, operatorUser.Id, BattleSide.B)).Succeeded);
            firstRoundId = (await battleService.StartBattleAsync(battleId, operatorUser.Id)).Value!.Id;
            Assert.True((await battleService.RecordBattleResultAsync(
                battleId, firstRoundId, operatorUser.Id, operatorUser.Id, ResultType.SpinFinish)).Succeeded);
        }

        var token = await GetAntiforgeryTokenAsync(operatorClient, $"/Battles/Battle/{battleId}");
        using var firstResponse = await operatorClient.PostAsync(
            $"/Battles/Battle/{battleId}?handler=CompleteRound",
            Form(("__RequestVerificationToken", token), ("id", battleId.ToString())));
        using var repeatedResponse = await operatorClient.PostAsync(
            $"/Battles/Battle/{battleId}?handler=CompleteRound",
            Form(("__RequestVerificationToken", token), ("id", battleId.ToString())));

        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, repeatedResponse.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedBattle = await verificationDb.Battles
            .Include(x => x.Rounds).ThenInclude(x => x.Events)
            .SingleAsync(x => x.Id == battleId);
        Assert.Equal(1, savedBattle.SideAScore);
        Assert.Equal(0, savedBattle.SideBScore);
        Assert.Equal(2, savedBattle.Rounds.Count);
        Assert.Single(savedBattle.Rounds, x => x.Id == firstRoundId && x.Status == BattleRoundStatus.Completed);
        Assert.Single(savedBattle.Rounds, x => x.Status == BattleRoundStatus.InProgress);
        Assert.Single(savedBattle.Rounds.SelectMany(x => x.Events),
            x => x.EventType == BattleRoundEventType.BattleResult && x.IsEffective);
    }

    [Fact]
    public async Task RepeatedFinishPosts_CompleteBattleAndMatchAndNotifyNextMatchExactlyOnce()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var organizerAccount = $"finish-organizer-{suffix}";
        const string password = "phase seven finish password";
        await RegisterAsync(client, organizerAccount, password, "Finish Organizer");
        await LoginAsync(client, organizerAccount, password);

        int battleId;
        int completedMatchId;
        int finalMatchId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizer = await db.Users.SingleAsync(x => x.Account == organizerAccount);
            var now = DateTime.UtcNow;
            var players = Enumerable.Range(1, 4)
                .Select(index => NewPhaseSevenUser($"finish-player-{index}-{suffix}", now))
                .ToList();
            db.Users.AddRange(players);
            await db.SaveChangesAsync();

            var tournament = NewPhaseSevenTournament(
                $"Finish Progression {suffix}", organizer.Id, now, TournamentStatus.InProgress);
            tournament.TargetEntryCount = 4;
            tournament.RegistrationStage = TournamentRegistrationStage.AwaitingStart;
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
            var entries = players.Select((player, index) => new TournamentEntry
            {
                TournamentId = tournament.Id,
                IndividualUserId = player.Id,
                RegistrationNumber = $"F-{index + 1}",
                SchedulePosition = index + 1,
                DisplayNameSnapshot = player.DisplayName,
                Status = TournamentEntryStatus.Registered,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RegisteredAtUtc = now
            }).ToList();
            db.TournamentEntries.AddRange(entries);
            await db.SaveChangesAsync();

            var firstSemifinal = NewPhaseSevenMatch(tournament.Id, 1, 1, 1, entries[0], entries[1], now);
            firstSemifinal.Status = TournamentMatchStatus.VictoryPendingCompletion;
            var secondSemifinal = NewPhaseSevenMatch(tournament.Id, 1, 2, 2, entries[2], entries[3], now);
            secondSemifinal.Status = TournamentMatchStatus.Completed;
            secondSemifinal.WinnerEntryId = entries[2].Id;
            secondSemifinal.LoserEntryId = entries[3].Id;
            secondSemifinal.CompletedAtUtc = now;
            db.TournamentMatches.AddRange(firstSemifinal, secondSemifinal);
            await db.SaveChangesAsync();

            var final = new TournamentMatch
            {
                TournamentId = tournament.Id,
                Bracket = TournamentBracket.Winners,
                RoundNumber = 2,
                MatchNumber = 1,
                SequenceNumber = 3,
                Status = TournamentMatchStatus.WaitingForParticipants,
                SideASourceKind = TournamentParticipantSourceKind.MatchWinner,
                SideASourceReferenceId = firstSemifinal.Id,
                SideBSourceKind = TournamentParticipantSourceKind.MatchWinner,
                SideBSourceReferenceId = secondSemifinal.Id,
                SideBEntryId = entries[2].Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            };
            db.TournamentMatches.Add(final);
            await db.SaveChangesAsync();
            firstSemifinal.WinnerToMatchId = final.Id;
            secondSemifinal.WinnerToMatchId = final.Id;
            db.TournamentMatchParticipants.AddRange(
                NewPhaseSevenParticipant(firstSemifinal, entries[0], players[0], now),
                NewPhaseSevenParticipant(firstSemifinal, entries[1], players[1], now),
                NewPhaseSevenParticipant(secondSemifinal, entries[2], players[2], now),
                NewPhaseSevenParticipant(secondSemifinal, entries[3], players[3], now));

            var battle = new Battle
            {
                SourceType = BattleSourceType.TournamentIndividual,
                ScoreToWin = 4,
                TournamentMatchId = firstSemifinal.Id,
                PlayerAId = players[0].Id,
                PlayerBId = players[1].Id,
                CreatedByUserId = organizer.Id,
                Status = BattleStatus.VictoryPendingCompletion,
                SideAScore = 4,
                SideBScore = 0,
                SideADesignation = BattleSide.B,
                CreatedAtUtc = now,
                StartedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            };
            db.Battles.Add(battle);
            await db.SaveChangesAsync();
            battleId = battle.Id;
            completedMatchId = firstSemifinal.Id;
            finalMatchId = final.Id;
        }

        var token = await GetAntiforgeryTokenAsync(client, $"/Battles/Battle/{battleId}");
        using var firstResponse = await client.PostAsync(
            $"/Battles/Battle/{battleId}?handler=Finish",
            Form(("__RequestVerificationToken", token), ("id", battleId.ToString())));
        using var repeatedResponse = await client.PostAsync(
            $"/Battles/Battle/{battleId}?handler=Finish",
            Form(("__RequestVerificationToken", token), ("id", battleId.ToString())));

        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, repeatedResponse.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedBattle = await verificationDb.Battles.SingleAsync(x => x.Id == battleId);
        var completedMatch = await verificationDb.TournamentMatches.SingleAsync(x => x.Id == completedMatchId);
        var finalMatch = await verificationDb.TournamentMatches
            .Include(x => x.Participants)
            .SingleAsync(x => x.Id == finalMatchId);
        Assert.Equal(BattleStatus.Completed, savedBattle.Status);
        Assert.Equal(TournamentMatchStatus.Completed, completedMatch.Status);
        Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, finalMatch.Status);
        Assert.Equal(2, finalMatch.Participants.Count);
        Assert.Equal(2, finalMatch.Participants.Select(x => x.UserId).Distinct().Count());
        Assert.All(finalMatch.Participants, participant => Assert.NotEqual(default, participant.NotifiedAtUtc));
    }

    [Fact]
    public async Task RepeatedPollRequests_AreReadOnlyAndReturnStableTokens()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var suffix = Guid.NewGuid().ToString("N");
        var organizerAccount = $"poll-load-{suffix}";
        const string password = "phase seven polling password";
        await RegisterAsync(client, organizerAccount, password, "Polling Organizer");
        await LoginAsync(client, organizerAccount, password);

        int tournamentId;
        int matchId;
        DateTime tournamentUpdatedAt;
        byte[] tournamentVersion;
        DateTime matchUpdatedAt;
        byte[] matchVersion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizer = await db.Users.SingleAsync(x => x.Account == organizerAccount);
            var now = DateTime.UtcNow;
            var opponent = NewPhaseSevenUser($"poll-opponent-{suffix}", now);
            db.Users.Add(opponent);
            await db.SaveChangesAsync();
            var tournament = NewPhaseSevenTournament(
                $"Polling Load {suffix}", organizer.Id, now, TournamentStatus.InProgress);
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
            var organizerEntry = NewPhaseSevenEntry(tournament.Id, organizer, "P-1", 1, now);
            var opponentEntry = NewPhaseSevenEntry(tournament.Id, opponent, "P-2", 2, now);
            db.TournamentEntries.AddRange(organizerEntry, opponentEntry);
            await db.SaveChangesAsync();
            var match = NewPhaseSevenMatch(tournament.Id, 1, 1, 1, organizerEntry, opponentEntry, now);
            match.Status = TournamentMatchStatus.AwaitingParticipationConfirmation;
            db.TournamentMatches.Add(match);
            await db.SaveChangesAsync();
            db.TournamentMatchParticipants.AddRange(
                NewPhaseSevenParticipant(match, organizerEntry, organizer, now),
                NewPhaseSevenParticipant(match, opponentEntry, opponent, now));
            await db.SaveChangesAsync();
            tournamentId = tournament.Id;
            matchId = match.Id;
            tournamentUpdatedAt = tournament.UpdatedAtUtc;
            tournamentVersion = tournament.Version.ToArray();
            matchUpdatedAt = match.UpdatedAtUtc;
            matchVersion = match.Version.ToArray();
        }

        var pollPaths = new[]
        {
            "/Tournaments?handler=Poll",
            $"/Tournaments/Details/{tournamentId}?handler=Poll",
            $"/Tournaments/Match/{matchId}?handler=Poll"
        };
        var firstBodies = new Dictionary<string, string>();
        for (var iteration = 0; iteration < 30; iteration++)
        {
            foreach (var path in pollPaths)
            {
                using var response = await client.GetAsync(path);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                if (iteration == 0)
                    firstBodies[path] = body;
                else
                    Assert.Equal(firstBodies[path], body);
            }
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedTournament = await verificationDb.Tournaments.SingleAsync(x => x.Id == tournamentId);
        var savedMatch = await verificationDb.TournamentMatches.SingleAsync(x => x.Id == matchId);
        Assert.Equal(tournamentUpdatedAt, savedTournament.UpdatedAtUtc);
        Assert.Equal(tournamentVersion, savedTournament.Version);
        Assert.Equal(matchUpdatedAt, savedMatch.UpdatedAtUtc);
        Assert.Equal(matchVersion, savedMatch.Version);
        Assert.Equal(2, await verificationDb.TournamentMatchParticipants.CountAsync(
            x => x.TournamentMatchId == matchId));
    }

    private static User NewPhaseSevenUser(string account, DateTime now) => new()
    {
        Account = account,
        PasswordHash = "test-only",
        DisplayName = account,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static Tournament NewPhaseSevenTournament(
        string name,
        int organizerUserId,
        DateTime now,
        TournamentStatus status) => new()
    {
        Name = name,
        Mode = TournamentMode.Individual,
        Format = TournamentFormat.SingleElimination,
        RegistrationMode = TournamentRegistrationMode.Individual,
        RuleSet = TournamentRuleSet.IndividualThreeBladeFourPoints,
        Status = status,
        RegistrationStage = TournamentRegistrationStage.AwaitingStart,
        BeybladesPerPlayer = 3,
        ScoreToWin = 4,
        TargetEntryCount = 2,
        OrganizerUserId = organizerUserId,
        RulesSnapshot = "phase-seven-test",
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        StartedAtUtc = status == TournamentStatus.InProgress ? now : null,
        Version = Guid.NewGuid().ToByteArray()
    };

    private static TournamentEntry NewPhaseSevenEntry(
        int tournamentId,
        User user,
        string registrationNumber,
        int schedulePosition,
        DateTime now) => new()
    {
        TournamentId = tournamentId,
        IndividualUserId = user.Id,
        RegistrationNumber = registrationNumber,
        SchedulePosition = schedulePosition,
        DisplayNameSnapshot = user.DisplayName,
        Status = TournamentEntryStatus.Registered,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        RegisteredAtUtc = now
    };

    private static TournamentMatch NewPhaseSevenMatch(
        int tournamentId,
        int roundNumber,
        int matchNumber,
        int sequenceNumber,
        TournamentEntry sideA,
        TournamentEntry sideB,
        DateTime now) => new()
    {
        TournamentId = tournamentId,
        Bracket = TournamentBracket.Winners,
        RoundNumber = roundNumber,
        MatchNumber = matchNumber,
        SequenceNumber = sequenceNumber,
        Status = TournamentMatchStatus.WaitingForParticipants,
        SideASourceKind = TournamentParticipantSourceKind.Entry,
        SideASourceReferenceId = sideA.Id,
        SideBSourceKind = TournamentParticipantSourceKind.Entry,
        SideBSourceReferenceId = sideB.Id,
        SideAEntryId = sideA.Id,
        SideBEntryId = sideB.Id,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Version = Guid.NewGuid().ToByteArray()
    };

    private static TournamentMatchParticipant NewPhaseSevenParticipant(
        TournamentMatch match,
        TournamentEntry entry,
        User user,
        DateTime now) => new()
    {
        TournamentMatchId = match.Id,
        TournamentEntryId = entry.Id,
        UserId = user.Id,
        Status = TournamentParticipationStatus.Pending,
        NotifiedAtUtc = now,
        Version = Guid.NewGuid().ToByteArray()
    };
}
