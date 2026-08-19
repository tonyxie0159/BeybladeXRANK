using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class TournamentServiceTests
{
    [Fact]
    public async Task Create_DerivesProtectedRulesOnServer()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");

        var result = await fixture.Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "  Duo Cup  ",
            TournamentRuleSet.DuoFourBladeSixPoints,
            TournamentRegistrationMode.CompleteTeam,
            TournamentFormat.DoubleElimination,
            16,
            "  Note  "));

        Assert.True(result.Succeeded);
        var tournament = result.Value!;
        Assert.Equal("Duo Cup", tournament.Name);
        Assert.Equal(TournamentMode.Team, tournament.Mode);
        Assert.Equal(2, tournament.TeamSize);
        Assert.Equal(2, tournament.BeybladesPerPlayer);
        Assert.Equal(6, tournament.ScoreToWin);
        Assert.Equal("Note", tournament.Notes);
        Assert.Contains("6 分制", tournament.RulesSnapshot);
    }

    [Fact]
    public async Task Create_RejectsInvalidRegistrationModeAndFormatLimit()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");

        var wrongRegistration = await fixture.Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "Invalid", TournamentRuleSet.IndividualThreeBladeFourPoints,
            TournamentRegistrationMode.CompleteTeam, TournamentFormat.SingleElimination, 8, null));
        var overRoundRobinLimit = await fixture.Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "Too Large", TournamentRuleSet.IndividualThreeBladeFourPoints,
            TournamentRegistrationMode.Individual, TournamentFormat.RoundRobin, 33, null));

        Assert.False(wrongRegistration.Succeeded);
        Assert.False(overRoundRobinLimit.Succeeded);
        Assert.Empty(await fixture.Db.Tournaments.ToListAsync());
    }

    [Fact]
    public async Task IndividualRegistration_RejectsDuplicateAndStopsAtCapacity()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var first = await fixture.AddUserAsync("first");
        var second = await fixture.AddUserAsync("second");
        var third = await fixture.AddUserAsync("third");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 2);

        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, first.Id)).Succeeded);
        Assert.False((await fixture.Service.RegisterIndividualAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, second.Id)).Succeeded);
        Assert.False((await fixture.Service.RegisterIndividualAsync(tournament.Id, third.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.Include(x => x.Entries).SingleAsync();
        Assert.Equal(TournamentRegistrationStage.CapacityReached, saved.RegistrationStage);
        Assert.Equal(2, saved.Entries.Count(x => x.Status == TournamentEntryStatus.Registered));
        Assert.Equal(2, saved.Entries.Select(x => x.RegistrationNumber).Distinct().Count());
    }

    [Fact]
    public async Task Withdraw_ReleasesCapacityAndAllowsSamePlayerToRegisterAgain()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var player = await fixture.AddUserAsync("player");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 2);

        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, player.Id)).Succeeded);
        Assert.True((await fixture.Service.WithdrawAsync(tournament.Id, player.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, player.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var entries = await fixture.Db.TournamentEntries.Where(x => x.TournamentId == tournament.Id).ToListAsync();
        Assert.Single(entries);
        Assert.Equal(TournamentEntryStatus.Registered, entries[0].Status);
        Assert.Null(entries[0].WithdrawnAtUtc);
    }

    [Fact]
    public async Task CloseRegistration_RequiresOrganizerAndTwoActiveEntries()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var first = await fixture.AddUserAsync("first");
        var second = await fixture.AddUserAsync("second");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 8);

        Assert.False((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, second.Id)).Succeeded);
        Assert.False((await fixture.Service.CloseRegistrationAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.False((await fixture.Service.RegisterIndividualAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.SingleAsync();
        Assert.Equal(TournamentRegistrationStage.Closed, saved.RegistrationStage);
        Assert.NotNull(saved.RegistrationClosedAtUtc);
    }

    [Fact]
    public async Task ListFilters_RespectParticipantAndOrganizerBoundaries()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var player = await fixture.AddUserAsync("player");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 8);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, player.Id)).Succeeded);

        var participating = await fixture.Service.GetListAsync(player.Id, TournamentListFilter.Participating);
        var hostedByPlayer = await fixture.Service.GetListAsync(player.Id, TournamentListFilter.Hosted);
        var hostedByOrganizer = await fixture.Service.GetListAsync(organizer.Id, TournamentListFilter.Hosted);

        Assert.Single(participating.Items);
        Assert.True(participating.Items[0].IsParticipant);
        Assert.Empty(hostedByPlayer.Items);
        Assert.Single(hostedByOrganizer.Items);
        Assert.True(hostedByOrganizer.Items[0].IsOrganizer);
    }

    [Fact]
    public async Task CompleteTeam_IsPrivateUntilAllMembersAcceptAndRepresentativeRegisters()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var representative = await fixture.AddUserAsync("representative");
        var teammate = await fixture.AddUserAsync("teammate");
        var tournament = await fixture.CreateTeamTournamentAsync(organizer.Id);

        var team = (await fixture.Service.CreateTemporaryTeamAsync(tournament.Id, representative.Id, "Team X")).Value!;
        Assert.Empty((await fixture.Service.GetDetailsAsync(tournament.Id))!.Entries);
        Assert.True((await fixture.Service.InviteTeamMemberAsync(tournament.Id, team.Id, representative.Id, teammate.Account)).Succeeded);
        var invitation = await fixture.Db.TournamentInvitations.SingleAsync();
        Assert.True((await fixture.Service.RespondToTeamInvitationAsync(invitation.Id, teammate.Id, true)).Succeeded);
        Assert.Empty((await fixture.Service.GetDetailsAsync(tournament.Id))!.Entries);
        Assert.True((await fixture.Service.RegisterCompleteTeamAsync(tournament.Id, team.Id, representative.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var details = await fixture.Service.GetDetailsAsync(tournament.Id);
        var registered = Assert.Single(details!.Entries);
        Assert.Equal(TournamentEntryStatus.Registered, registered.Status);
        Assert.Equal("Team X", registered.DisplayNameSnapshot);
        Assert.NotNull(registered.RegistrationNumber);
        Assert.Equal(2, registered.Members.Count);
    }

    [Fact]
    public async Task AcceptingOneTeamInvitation_InvalidatesOtherInvitationsInTournament()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var firstRepresentative = await fixture.AddUserAsync("first-rep");
        var secondRepresentative = await fixture.AddUserAsync("second-rep");
        var invited = await fixture.AddUserAsync("invited");
        var tournament = await fixture.CreateTeamTournamentAsync(organizer.Id);
        var firstTeam = (await fixture.Service.CreateTemporaryTeamAsync(tournament.Id, firstRepresentative.Id, "First")).Value!;
        var secondTeam = (await fixture.Service.CreateTemporaryTeamAsync(tournament.Id, secondRepresentative.Id, "Second")).Value!;
        Assert.True((await fixture.Service.InviteTeamMemberAsync(tournament.Id, firstTeam.Id, firstRepresentative.Id, invited.Account)).Succeeded);
        Assert.True((await fixture.Service.InviteTeamMemberAsync(tournament.Id, secondTeam.Id, secondRepresentative.Id, invited.Account)).Succeeded);
        var invitations = await fixture.Db.TournamentInvitations.OrderBy(x => x.Id).ToListAsync();

        Assert.True((await fixture.Service.RespondToTeamInvitationAsync(invitations[0].Id, invited.Id, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        invitations = await fixture.Db.TournamentInvitations.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(TournamentInvitationStatus.Accepted, invitations[0].Status);
        Assert.Equal(TournamentInvitationStatus.Invalidated, invitations[1].Status);
        Assert.Single(await fixture.Db.TournamentEntryMembers.Where(x => x.UserId == invited.Id).ToListAsync());
    }

    [Fact]
    public async Task RepresentativeMustTransferBeforeLeaving_AndMemberExitInvalidatesRegistration()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var representative = await fixture.AddUserAsync("representative");
        var teammate = await fixture.AddUserAsync("teammate");
        var tournament = await fixture.CreateTeamTournamentAsync(organizer.Id);
        var team = (await fixture.Service.CreateTemporaryTeamAsync(tournament.Id, representative.Id, null)).Value!;
        Assert.True((await fixture.Service.InviteTeamMemberAsync(tournament.Id, team.Id, representative.Id, teammate.Account)).Succeeded);
        var invitation = await fixture.Db.TournamentInvitations.SingleAsync();
        Assert.True((await fixture.Service.RespondToTeamInvitationAsync(invitation.Id, teammate.Id, true)).Succeeded);
        Assert.True((await fixture.Service.RegisterCompleteTeamAsync(tournament.Id, team.Id, representative.Id)).Succeeded);

        Assert.False((await fixture.Service.LeaveTeamAsync(tournament.Id, representative.Id)).Succeeded);
        Assert.True((await fixture.Service.TransferRepresentativeAsync(tournament.Id, team.Id, representative.Id, teammate.Id)).Succeeded);
        var transfer = await fixture.Db.TournamentInvitations.SingleAsync(x => x.Type == TournamentInvitationType.RepresentativeTransfer);
        Assert.True((await fixture.Service.RespondToRepresentativeTransferAsync(transfer.Id, teammate.Id, true)).Succeeded);
        Assert.True((await fixture.Service.LeaveTeamAsync(tournament.Id, representative.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.TournamentEntries.Include(x => x.Members).SingleAsync(x => x.Id == team.Id);
        Assert.Equal(TournamentEntryStatus.Pending, saved.Status);
        Assert.Null(saved.RegistrationNumber);
        Assert.Single(saved.Members);
        Assert.True(saved.Members.Single().IsRepresentative);
    }

    [Fact]
    public async Task SystemPairing_CreatesOnlyCompleteTeamsAndEnforcesPlayerCapacity()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var players = new List<User>();
        for (var index = 1; index <= 5; index++) players.Add(await fixture.AddUserAsync($"player-{index}"));
        var tournament = await fixture.CreateSystemPairingTournamentAsync(organizer.Id, 2);

        foreach (var player in players.Take(4))
            Assert.True((await fixture.Service.RegisterForSystemPairingAsync(tournament.Id, player.Id)).Succeeded);
        Assert.False((await fixture.Service.RegisterForSystemPairingAsync(tournament.Id, players[4].Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.False((await fixture.Service.GenerateSystemAssignedTeamsAsync(tournament.Id, players[0].Id)).Succeeded);
        Assert.True((await fixture.Service.GenerateSystemAssignedTeamsAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var entries = await fixture.Db.TournamentEntries.Include(x => x.Members)
            .Where(x => x.TournamentId == tournament.Id && x.Status != TournamentEntryStatus.Withdrawn).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(TournamentEntryStatus.Registered, entry.Status);
            Assert.Equal(2, entry.Members.Count);
            Assert.Single(entry.Members, x => x.IsRepresentative);
            Assert.NotNull(entry.RegistrationNumber);
        });
        Assert.Equal(4, entries.SelectMany(x => x.Members).Select(x => x.UserId).Distinct().Count());
    }

    [Fact]
    public async Task SystemPairing_LeftoverWaitsForSupplementAndCanReopenRegistration()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var players = new List<User>();
        for (var index = 1; index <= 5; index++) players.Add(await fixture.AddUserAsync($"player-{index}"));
        var tournament = await fixture.CreateSystemPairingTournamentAsync(organizer.Id, 3);
        foreach (var player in players)
            Assert.True((await fixture.Service.RegisterForSystemPairingAsync(tournament.Id, player.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.GenerateSystemAssignedTeamsAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.SingleAsync(x => x.Id == tournament.Id);
        var entries = await fixture.Db.TournamentEntries.Include(x => x.Members)
            .Where(x => x.TournamentId == tournament.Id && x.Status != TournamentEntryStatus.Withdrawn).ToListAsync();
        Assert.Equal(TournamentRegistrationStage.AwaitingTeamFormation, saved.RegistrationStage);
        Assert.Equal(2, entries.Count(x => x.Status == TournamentEntryStatus.Registered));
        Assert.Single(entries, x => x.Status == TournamentEntryStatus.Pending && x.Members.Count == 1);
        Assert.True((await fixture.Service.ReopenSystemPairingRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.Equal(TournamentRegistrationStage.Open, (await fixture.Db.Tournaments.FindAsync(tournament.Id))!.RegistrationStage);
    }

    [Fact]
    public async Task OrganizerSwap_PreservesTeamSizesAndUniqueMembership()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var players = new List<User>();
        for (var index = 1; index <= 4; index++) players.Add(await fixture.AddUserAsync($"player-{index}"));
        var tournament = await fixture.CreateSystemPairingTournamentAsync(organizer.Id, 2);
        foreach (var player in players)
            Assert.True((await fixture.Service.RegisterForSystemPairingAsync(tournament.Id, player.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.GenerateSystemAssignedTeamsAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var before = await fixture.Db.TournamentEntries.Include(x => x.Members)
            .Where(x => x.TournamentId == tournament.Id && x.Status == TournamentEntryStatus.Registered).OrderBy(x => x.Id).ToListAsync();
        var first = before[0].Members.First();
        var second = before[1].Members.First();

        Assert.True((await fixture.Service.SwapSystemAssignedMembersAsync(tournament.Id, organizer.Id, first.Id, second.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var after = await fixture.Db.TournamentEntries.Include(x => x.Members)
            .Where(x => x.TournamentId == tournament.Id && x.Status == TournamentEntryStatus.Registered).ToListAsync();
        Assert.All(after, entry =>
        {
            Assert.Equal(2, entry.Members.Count);
            Assert.Single(entry.Members, x => x.IsRepresentative);
        });
        Assert.Equal(4, after.SelectMany(x => x.Members).Select(x => x.UserId).Distinct().Count());
        Assert.Contains(after.Single(x => x.Id == before[0].Id).Members, x => x.Id == second.Id);
        Assert.Contains(after.Single(x => x.Id == before[1].Id).Members, x => x.Id == first.Id);
    }

    [Fact]
    public async Task ScheduleDraft_PersistsSingleEliminationLinksByDatabaseMatchId()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 6, TournamentFormat.SingleElimination);

        Assert.False((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id + 999, 7)).Succeeded);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 7)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        var matches = saved.Matches.Where(x => !x.IsBye).ToList();
        Assert.Equal(5, matches.Count);
        Assert.Contains(saved.Matches, x => x.IsBye);
        Assert.Equal(TournamentRegistrationStage.ScheduleDraftCreated, saved.RegistrationStage);
        Assert.Equal(6, saved.Entries.Select(x => x.SchedulePosition).Distinct().Count());
        foreach (var match in matches)
        {
            if (match.SideASourceKind != TournamentParticipantSourceKind.Entry)
                Assert.Contains(matches, x => x.Id == match.SideASourceReferenceId);
            if (match.SideBSourceKind != TournamentParticipantSourceKind.Entry)
                Assert.Contains(matches, x => x.Id == match.SideBSourceReferenceId);
        }
        Assert.All(matches.Where(x => x.WinnerToMatchId is not null), source =>
            Assert.Contains(matches, target => target.Id == source.WinnerToMatchId));
    }

    [Fact]
    public async Task AbandonDraft_RemovesMatchesAndSchedulePositions()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 4, TournamentFormat.DoubleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 11)).Succeeded);

        Assert.True((await fixture.Service.AbandonScheduleDraftAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentRegistrationStage.Closed, saved.RegistrationStage);
        Assert.Empty(saved.Matches);
        Assert.All(saved.Entries, entry => Assert.Null(entry.SchedulePosition));
    }

    [Fact]
    public async Task ReorderSchedule_ReplacesEntriesWithoutChangingMatchTopology()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 6, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 9)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var before = await fixture.Db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        var orderedBefore = before.Entries.OrderBy(x => x.SchedulePosition).Select(x => x.Id).ToList();
        var topology = before.Matches.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.Bracket, x.RoundNumber, x.MatchNumber, x.SequenceNumber,
            x.SideASourceKind, x.SideBSourceKind, x.WinnerToMatchId, x.LoserToMatchId,
            x.IsBye, x.IsSeedQualifier, x.IsResetFinal
        }).ToList();

        Assert.True((await fixture.Service.ReorderScheduleEntriesAsync(tournament.Id, organizer.Id, orderedBefore.AsEnumerable().Reverse().ToList())).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var after = await fixture.Db.Tournaments.Include(x => x.Entries).Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(orderedBefore.AsEnumerable().Reverse(), after.Entries.OrderBy(x => x.SchedulePosition).Select(x => x.Id));
        Assert.Equal(topology, after.Matches.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.Bracket, x.RoundNumber, x.MatchNumber, x.SequenceNumber,
            x.SideASourceKind, x.SideBSourceKind, x.WinnerToMatchId, x.LoserToMatchId,
            x.IsBye, x.IsSeedQualifier, x.IsResetFinal
        }).ToList());
    }

    [Fact]
    public async Task WithdrawalAfterDraft_InvalidatesDraftBeforeTournamentStarts()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 4, TournamentFormat.RoundRobin);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 3)).Succeeded);

        Assert.True((await fixture.Service.WithdrawAsync(tournament.Id, players[0].Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentRegistrationStage.Closed, saved.RegistrationStage);
        Assert.Empty(saved.Matches);
    }

    [Theory]
    [InlineData(TournamentFormat.SingleElimination, 5)]
    [InlineData(TournamentFormat.Swiss, 5)]
    public async Task StartTournament_LocksScheduleAndActivatesOnlyFirstReadyMatch(TournamentFormat format, int entryCount)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, entryCount, format);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 17)).Succeeded);

        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.False((await fixture.Service.WithdrawAsync(tournament.Id, players[0].Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.InProgress, saved.Status);
        Assert.NotNull(saved.StartedAtUtc);
        Assert.Single(saved.Matches, x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        var activeMatchId = saved.Matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation).Id;
        Assert.Equal(2, await fixture.Db.TournamentMatchParticipants.CountAsync(x => x.TournamentMatchId == activeMatchId));
        Assert.All(saved.Matches.Where(x => !x.IsBye && x.Status != TournamentMatchStatus.AwaitingParticipationConfirmation),
            x => Assert.Equal(TournamentMatchStatus.WaitingForParticipants, x.Status));
    }

    [Fact]
    public async Task IndividualMatch_PreservesPrivateLineupsUntilBothSubmit_ThenStartsFirstRound()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("match-organizer");
        var outsider = await fixture.AddUserAsync("match-outsider");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 2, TournamentFormat.SingleElimination);
        var firstBlades = await fixture.AddBladesAsync(players[0].Id, "A");
        var secondBlades = await fixture.AddBladesAsync(players[1].Id, "B");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 7)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.SingleAsync(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);

        Assert.Null(await fixture.MatchService.GetWorkspaceAsync(match.Id, outsider.Id));
        Assert.False((await fixture.MatchService.RespondParticipationAsync(match.Id, outsider.Id, true)).Succeeded);
        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, players[0].Id, true)).Succeeded);
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, players[1].Id, true)).Succeeded);
        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, players[1].Id, true)).Succeeded);
        Assert.Single(await fixture.Db.Battles.ToListAsync());

        Assert.True((await fixture.MatchService.SubmitIndividualLineupAsync(match.Id, players[0].Id, firstBlades)).Succeeded);
        Assert.True((await fixture.MatchService.SubmitIndividualLineupAsync(match.Id, players[0].Id, firstBlades)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var opponentWorkspace = await fixture.MatchService.GetWorkspaceAsync(match.Id, players[1].Id);
        Assert.NotNull(opponentWorkspace);
        Assert.Empty(opponentWorkspace.VisibleSelections);
        Assert.True((await fixture.MatchService.SubmitIndividualLineupAsync(match.Id, players[1].Id, secondBlades)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var publicWorkspace = await fixture.MatchService.GetWorkspaceAsync(match.Id, players[0].Id);
        Assert.Equal(TournamentMatchStatus.LineupReview, publicWorkspace!.Match.Status);
        Assert.Equal(6, publicWorkspace.VisibleSelections.Count);
        Assert.Equal(3, publicWorkspace.Match.Battle!.Lineups.Count);
        Assert.True((await fixture.MatchService.ConfirmLineupAsync(match.Id, players[0].Id)).Succeeded);
        Assert.True((await fixture.MatchService.ConfirmLineupAsync(match.Id, players[1].Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.MatchService.AssignSidesAndStartAsync(match.Id, organizer.Id, BattleSide.X);
        Assert.True(started.Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var resumed = await fixture.MatchService.GetWorkspaceAsync(match.Id, players[1].Id);
        Assert.Equal(TournamentMatchStatus.InProgress, resumed!.Match.Status);
        Assert.Equal(BattleStatus.InProgress, resumed.Match.Battle!.Status);
        Assert.Equal(BattleSide.X, resumed.Match.Battle.SideADesignation);
        Assert.Single(resumed.Match.Battle.Rounds);
        var battleService = new BattleService(fixture.Db);
        var firstRound = resumed.Match.Battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(resumed.Match.Battle.Id, firstRound.Id, organizer.Id, resumed.Match.Battle.PlayerAId!.Value, ResultType.Extreme)).Succeeded);
        var nextRound = (await battleService.CompleteRoundAsync(resumed.Match.Battle.Id, firstRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(resumed.Match.Battle.Id, nextRound.Id, organizer.Id, resumed.Match.Battle.PlayerAId.Value, ResultType.SpinFinish)).Succeeded);
        Assert.True((await battleService.FinishBattleAsync(resumed.Match.Battle.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var completedTournament = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Completed, completedTournament.Status);
        Assert.Equal(TournamentMatchStatus.Completed, completedTournament.Matches.Single().Status);
        Assert.NotNull(completedTournament.Matches.Single().WinnerEntryId);
        Assert.All(await fixture.Db.BattleRounds.Where(x => x.BattleId == resumed.Match.Battle.Id).ToListAsync(), x => Assert.Equal(BattleRoundStatus.Completed, x.Status));
    }

    [Fact]
    public async Task CompletingTournamentBattle_ResolvesDownstreamSlotAndNotifiesNextReadyMatch()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("advance-organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 4, TournamentFormat.SingleElimination);
        var blades = new Dictionary<int, List<int>>();
        foreach (var player in players) blades[player.Id] = await fixture.AddBladesAsync(player.Id, $"advance-{player.Id}");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 19)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var active = await fixture.Db.TournamentMatches.Include(x => x.Participants).SingleAsync(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        foreach (var participant in active.Participants)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(active.Id, participant.UserId, true)).Succeeded);
        foreach (var participant in active.Participants)
            Assert.True((await fixture.MatchService.SubmitLineupAsync(active.Id, participant.UserId, blades[participant.UserId])).Succeeded);
        foreach (var participant in active.Participants)
            Assert.True((await fixture.MatchService.ConfirmLineupAsync(active.Id, participant.UserId)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.MatchService.AssignSidesAndStartAsync(active.Id, organizer.Id, BattleSide.B);
        Assert.True(started.Succeeded);
        var battleService = new BattleService(fixture.Db);
        var battle = (await battleService.GetBattleAsync(started.Value, organizer.Id)).Value!;
        var winnerUserId = battle.PlayerAId!.Value;
        var first = battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(battle.Id, first.Id, organizer.Id, winnerUserId, ResultType.Extreme)).Succeeded);
        var second = (await battleService.CompleteRoundAsync(battle.Id, first.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(battle.Id, second.Id, organizer.Id, winnerUserId, ResultType.SpinFinish)).Succeeded);
        Assert.True((await battleService.FinishBattleAsync(battle.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var matches = await fixture.Db.TournamentMatches.Include(x => x.Participants).Where(x => x.TournamentId == tournament.Id).ToListAsync();
        var completed = matches.Single(x => x.Id == active.Id);
        var winnerEntryId = completed.SideAEntryId!.Value;
        Assert.Equal(winnerEntryId, completed.WinnerEntryId);
        var downstream = matches.Single(x => x.Id == completed.WinnerToMatchId);
        Assert.True(downstream.SideAEntryId == winnerEntryId || downstream.SideBEntryId == winnerEntryId);
        var next = matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        Assert.NotEqual(completed.Id, next.Id);
        Assert.Equal(2, next.Participants.Count);
        Assert.All(next.Participants, x => Assert.Equal(TournamentParticipationStatus.Pending, x.Status));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DoubleElimination_FirstGrandFinalConditionallySkipsOrActivatesReset(bool undefeatedChampionWins)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync($"reset-organizer-{undefeatedChampionWins}");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 2, TournamentFormat.DoubleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 23)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        var winnersFinal = tournament.Matches.Single(x => x.Bracket == TournamentBracket.Winners);
        var grandFinal = tournament.Matches.Single(x => x.Bracket == TournamentBracket.GrandFinal && !x.IsResetFinal);
        var resetFinal = tournament.Matches.Single(x => x.IsResetFinal);
        var undefeatedEntryId = winnersFinal.SideAEntryId!.Value;
        var challengerEntryId = winnersFinal.SideBEntryId!.Value;
        winnersFinal.WinnerEntryId = undefeatedEntryId;
        winnersFinal.LoserEntryId = challengerEntryId;
        winnersFinal.Status = TournamentMatchStatus.Completed;
        grandFinal.SideAEntryId = undefeatedEntryId;
        grandFinal.SideBEntryId = challengerEntryId;
        grandFinal.Status = TournamentMatchStatus.VictoryPendingCompletion;
        tournament.Status = TournamentStatus.InProgress;
        await fixture.Db.SaveChangesAsync();

        var winnerEntryId = undefeatedChampionWins ? undefeatedEntryId : challengerEntryId;
        var loserEntryId = undefeatedChampionWins ? challengerEntryId : undefeatedEntryId;
        await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
            grandFinal, winnerEntryId, loserEntryId, TournamentMatchStatus.Completed, "BattleCompleted", DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants).SingleAsync(x => x.Id == tournament.Id);
        resetFinal = tournament.Matches.Single(x => x.IsResetFinal);
        if (undefeatedChampionWins)
        {
            Assert.Equal(TournamentMatchStatus.NotRequired, resetFinal.Status);
            Assert.Equal("ResetFinalNotRequired", resetFinal.ResolutionReason);
            Assert.Empty(resetFinal.Participants);
            Assert.Equal(TournamentStatus.Completed, tournament.Status);
        }
        else
        {
            Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, resetFinal.Status);
            Assert.Equal(challengerEntryId, resetFinal.SideAEntryId);
            Assert.Equal(undefeatedEntryId, resetFinal.SideBEntryId);
            Assert.Equal(2, resetFinal.Participants.Count);
            Assert.Equal(TournamentStatus.InProgress, tournament.Status);
        }
    }

    [Fact]
    public async Task DoubleElimination_WinnersMatchUsesFixedWinnerAndLoserDestinations()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("double-map-organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 4, TournamentFormat.DoubleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 29)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        tournament.Status = TournamentStatus.InProgress;
        var source = tournament.Matches.Where(x => x.Bracket == TournamentBracket.Winners && x.RoundNumber == 1).OrderBy(x => x.SequenceNumber).First();
        source.Status = TournamentMatchStatus.VictoryPendingCompletion;
        var winnerEntryId = source.SideAEntryId!.Value;
        var loserEntryId = source.SideBEntryId!.Value;
        await fixture.Db.SaveChangesAsync();

        await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
            source, winnerEntryId, loserEntryId, TournamentMatchStatus.Completed, "BattleCompleted", DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.ChangeTracker.Clear();
        var matches = await fixture.Db.TournamentMatches.Where(x => x.TournamentId == tournament.Id).ToListAsync();
        var winnerDestination = matches.Single(x => x.Id == source.WinnerToMatchId);
        var loserDestination = matches.Single(x => x.Id == source.LoserToMatchId);
        Assert.True(winnerDestination.SideAEntryId == winnerEntryId || winnerDestination.SideBEntryId == winnerEntryId);
        Assert.True(loserDestination.SideAEntryId == loserEntryId || loserDestination.SideBEntryId == loserEntryId);
        Assert.NotEqual(winnerDestination.Id, loserDestination.Id);
    }

    [Fact]
    public async Task SwissTournament_GeneratesEveryRoundWithoutRepeatOpponentOrRepeatBye()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("swiss-progress-organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 5, TournamentFormat.Swiss);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 31)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        var historicalUser = await fixture.AddUserAsync("swiss-withdrawn-history");
        var historicalEntry = new TournamentEntry
        {
            TournamentId = tournament.Id,
            IndividualUserId = historicalUser.Id,
            DisplayNameSnapshot = historicalUser.DisplayName,
            Status = TournamentEntryStatus.Withdrawn,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            WithdrawnAtUtc = DateTime.UtcNow
        };
        fixture.Db.TournamentEntries.Add(historicalEntry);
        await fixture.Db.SaveChangesAsync();

        for (var completedMatchCount = 0; completedMatchCount < 10; completedMatchCount++)
        {
            fixture.Db.ChangeTracker.Clear();
            tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants).SingleAsync(x => x.Id == tournament.Id);
            if (tournament.Status == TournamentStatus.Completed) break;
            var active = tournament.Matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
                active, active.SideAEntryId!.Value, active.SideBEntryId!.Value,
                TournamentMatchStatus.Completed, "TestResult", DateTime.UtcNow);
            await fixture.Db.SaveChangesAsync();
        }

        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Completed, tournament.Status);
        Assert.Equal(SwissPairingGenerator.RoundCountFor(5), tournament.Matches.Max(x => x.RoundNumber));
        Assert.All(tournament.Matches.GroupBy(x => x.RoundNumber), round =>
        {
            Assert.Equal(2, round.Count(x => !x.IsBye));
            Assert.Single(round, x => x.IsBye);
        });
        Assert.Equal(3, tournament.Matches.Where(x => x.IsBye).Select(x => x.WinnerEntryId).Distinct().Count());
        Assert.DoesNotContain(tournament.Matches, x => x.SideAEntryId == historicalEntry.Id || x.SideBEntryId == historicalEntry.Id);
        var opponents = new HashSet<(int, int)>();
        foreach (var played in tournament.Matches.Where(x => !x.IsBye))
        {
            var pair = played.SideAEntryId < played.SideBEntryId
                ? (played.SideAEntryId!.Value, played.SideBEntryId!.Value)
                : (played.SideBEntryId!.Value, played.SideAEntryId!.Value);
            Assert.True(opponents.Add(pair), $"重複配對：{pair}");
        }
        Assert.Empty(await fixture.Db.Battles.Where(x => x.TournamentMatch!.TournamentId == tournament.Id).ToListAsync());
    }

    [Theory]
    [InlineData(TournamentRuleSet.DuoSixBladeEightPoints, 2, 3, 6)]
    [InlineData(TournamentRuleSet.DuoFourBladeSixPoints, 2, 2, 4)]
    [InlineData(TournamentRuleSet.TrioThreeBladeFourPoints, 3, 1, 3)]
    public async Task TeamMatch_MaterializesActualPlayerBladePairingsForEveryRule(
        TournamentRuleSet ruleSet, int teamSize, int bladesPerPlayer, int expectedRounds)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (tournament, organizer, match) = await fixture.CreateStartedTeamMatchAsync(ruleSet);
        var participants = match.Participants.OrderBy(x => x.TournamentEntryId).ThenBy(x => x.UserId).ToList();
        Assert.Equal(teamSize * 2, participants.Count);
        Assert.Equal(2, participants.Count(x => x.IsMatchRepresentative));
        foreach (var participant in participants)
        {
            Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, participant.UserId, true)).Succeeded);
        }
        fixture.Db.ChangeTracker.Clear();
        var accepted = await fixture.MatchService.GetWorkspaceAsync(match.Id, participants[0].UserId);
        Assert.Equal(BattleSourceType.TournamentTeam, accepted!.Match.Battle!.SourceType);
        Assert.Null(accepted.Match.Battle.PlayerAId);
        Assert.Null(accepted.Match.Battle.PlayerBId);
        var originalRepresentative = participants.First(x => x.IsMatchRepresentative);
        var replacementRepresentative = participants.First(x => x.TournamentEntryId == originalRepresentative.TournamentEntryId && !x.IsMatchRepresentative);
        Assert.False((await fixture.MatchService.AssignMatchRepresentativeAsync(match.Id, replacementRepresentative.UserId, replacementRepresentative.UserId)).Succeeded);
        Assert.True((await fixture.MatchService.AssignMatchRepresentativeAsync(match.Id, originalRepresentative.UserId, replacementRepresentative.UserId)).Succeeded);

        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var participant in participants)
        {
            var blades = (await fixture.AddBladesAsync(participant.UserId, $"T{participant.UserId}")).Take(bladesPerPlayer).ToList();
            bladesByUser[participant.UserId] = blades;
        }
        foreach (var participant in participants)
        {
            Assert.True((await fixture.MatchService.SubmitLineupAsync(match.Id, participant.UserId, bladesByUser[participant.UserId])).Succeeded);
            if (participant != participants[^1])
            {
                fixture.Db.ChangeTracker.Clear();
                var hidden = await fixture.MatchService.GetWorkspaceAsync(match.Id, participants[^1].UserId);
                Assert.DoesNotContain(hidden!.VisibleSelections, x => x.UserId == participant.UserId);
            }
        }

        fixture.Db.ChangeTracker.Clear();
        var orderWorkspace = await fixture.MatchService.GetWorkspaceAsync(match.Id, participants[0].UserId);
        Assert.Equal(TournamentMatchStatus.TeamOrderSelection, orderWorkspace!.Match.Status);
        var sideA = orderWorkspace.Match.Participants.Where(x => x.TournamentEntryId == orderWorkspace.Match.SideAEntryId).OrderByDescending(x => x.UserId).ToList();
        var sideB = orderWorkspace.Match.Participants.Where(x => x.TournamentEntryId == orderWorkspace.Match.SideBEntryId).OrderBy(x => x.UserId).ToList();
        var repA = sideA.Single(x => x.IsMatchRepresentative);
        var repB = sideB.Single(x => x.IsMatchRepresentative);
        Assert.True((await fixture.MatchService.SubmitTeamOrderAsync(match.Id, repA.UserId, sideA.Select(x => x.UserId).ToList())).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var hiddenOrder = await fixture.MatchService.GetWorkspaceAsync(match.Id, repB.UserId);
        Assert.Empty(hiddenOrder!.VisibleTeamOrder);
        Assert.True((await fixture.MatchService.SubmitTeamOrderAsync(match.Id, repB.UserId, sideB.Select(x => x.UserId).ToList())).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var review = await fixture.MatchService.GetWorkspaceAsync(match.Id, repA.UserId);
        Assert.Equal(TournamentMatchStatus.LineupReview, review!.Match.Status);
        Assert.Equal(expectedRounds, review.Match.Battle!.Lineups.Count);
        var lineup = review.Match.Battle.Lineups.OrderBy(x => x.PositionNo).ToList();
        for (var bladeIndex = 0; bladeIndex < bladesPerPlayer; bladeIndex++)
        for (var memberIndex = 0; memberIndex < teamSize; memberIndex++)
        {
            var row = lineup[bladeIndex * teamSize + memberIndex];
            Assert.Equal(sideA[memberIndex].UserId, row.PlayerAId);
            Assert.Equal(bladesByUser[sideA[memberIndex].UserId][bladeIndex], row.PlayerABeybladeId);
            Assert.Equal(sideB[memberIndex].UserId, row.PlayerBId);
            Assert.Equal(bladesByUser[sideB[memberIndex].UserId][bladeIndex], row.PlayerBBeybladeId);
        }
        foreach (var participant in participants)
            Assert.True((await fixture.MatchService.ConfirmLineupAsync(match.Id, participant.UserId)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.MatchService.AssignSidesAndStartAsync(match.Id, organizer.Id, BattleSide.B);
        Assert.True(started.Succeeded);
        var battle = await fixture.Db.Battles.Include(x => x.Rounds).SingleAsync(x => x.Id == started.Value);
        Assert.Equal(BattleStatus.InProgress, battle.Status);
        Assert.Single(battle.Rounds);
        Assert.Equal(lineup[0].PlayerAId, battle.Rounds.Single().PlayerAId);
        Assert.Equal(lineup[0].PlayerBId, battle.Rounds.Single().PlayerBId);
        Assert.Equal(tournament.ScoreToWin, battle.ScoreToWin);
        var battleService = new BattleService(fixture.Db);
        Assert.True((await battleService.GetBattleAsync(battle.Id, participants[0].UserId)).Succeeded);
        var firstRound = battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(battle.Id, firstRound.Id, organizer.Id, firstRound.PlayerAId!.Value, ResultType.SpinFinish)).Succeeded);
        var completedRound = await battleService.CompleteRoundAsync(battle.Id, firstRound.Id, organizer.Id);
        Assert.True(completedRound.Succeeded);
        Assert.NotNull(completedRound.Value);
        var nextRound = completedRound.Value;
        while (nextRound is not null)
        {
            Assert.True((await battleService.RecordBattleResultAsync(battle.Id, nextRound.Id, organizer.Id, nextRound.PlayerAId!.Value, ResultType.SpinFinish)).Succeeded);
            var completed = await battleService.CompleteRoundAsync(battle.Id, nextRound.Id, organizer.Id);
            Assert.True(completed.Succeeded);
            nextRound = completed.Value;
        }
        fixture.Db.ChangeTracker.Clear();
        var reorder = await fixture.MatchService.GetWorkspaceAsync(match.Id, participants[0].UserId);
        Assert.Equal(TournamentMatchStatus.ReorderSelection, reorder!.Match.Status);
        Assert.Equal(expectedRounds, reorder.Match.Battle!.SideAScore);
        Assert.Equal(0, reorder.Match.Battle.SideBScore);

        var reorderParticipants = reorder.Match.Participants.OrderBy(x => x.UserId).ToList();
        foreach (var participant in reorderParticipants)
        {
            Assert.True((await fixture.MatchService.SubmitReorderAsync(match.Id, participant.UserId, bladesByUser[participant.UserId].AsEnumerable().Reverse().ToList())).Succeeded);
        }
        fixture.Db.ChangeTracker.Clear();
        reorder = await fixture.MatchService.GetWorkspaceAsync(match.Id, reorderParticipants[0].UserId);
        var reorderSideA = reorder!.Match.Participants.Where(x => x.TournamentEntryId == reorder.Match.SideAEntryId).OrderByDescending(x => x.UserId).ToList();
        var reorderSideB = reorder.Match.Participants.Where(x => x.TournamentEntryId == reorder.Match.SideBEntryId).OrderByDescending(x => x.UserId).ToList();
        var reorderRepA = reorderSideA.Single(x => x.IsMatchRepresentative);
        var reorderRepB = reorderSideB.Single(x => x.IsMatchRepresentative);
        Assert.True((await fixture.MatchService.SubmitTeamReorderOrderAsync(match.Id, reorderRepA.UserId, reorderSideA.Select(x => x.UserId).ToList())).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var otherTeamBeforeSubmit = await fixture.MatchService.GetWorkspaceAsync(match.Id, reorderRepB.UserId);
        Assert.Empty(otherTeamBeforeSubmit!.CurrentPrivateTeamOrder);
        Assert.True((await fixture.MatchService.SubmitTeamReorderOrderAsync(match.Id, reorderRepB.UserId, reorderSideB.Select(x => x.UserId).ToList())).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var sequenceTwo = await fixture.MatchService.GetWorkspaceAsync(match.Id, reorderRepA.UserId);
        Assert.Equal(TournamentMatchStatus.InProgress, sequenceTwo!.Match.Status);
        Assert.Equal(expectedRounds, sequenceTwo.Match.Battle!.Lineups.Count(x => x.SequenceNo == 2));
        Assert.All(sequenceTwo.Match.Battle.Lineups.Where(x => x.SequenceNo == 1), x => Assert.False(x.IsCurrent));
        Assert.All(sequenceTwo.Match.Battle.Lineups.Where(x => x.SequenceNo == 2), x => Assert.True(x.IsCurrent));
        Assert.Equal(expectedRounds + 1, sequenceTwo.Match.Battle.Rounds.Single(x => x.Status == BattleRoundStatus.InProgress).RoundNo);
        Assert.Equal(expectedRounds, sequenceTwo.Match.Battle.SideAScore);
    }

    [Fact]
    public async Task TeamMatch_WhenAnyRequiredMemberDeclines_WholeEntryLosesWithoutBattle()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (_, _, match) = await fixture.CreateStartedTeamMatchAsync(TournamentRuleSet.DuoFourBladeSixPoints);
        var declining = match.Participants.First();

        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, declining.UserId, false)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.TournamentMatches.Include(x => x.Participants).Include(x => x.Battle).SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentMatchStatus.Walkover, saved.Status);
        Assert.Equal(declining.TournamentEntryId, saved.LoserEntryId);
        Assert.NotEqual(declining.TournamentEntryId, saved.WinnerEntryId);
        Assert.Null(saved.Battle);
        Assert.All(saved.Participants.Where(x => x.UserId != declining.UserId), x => Assert.Equal(TournamentParticipationStatus.Invalidated, x.Status));
        Assert.Equal(TournamentStatus.Completed, (await fixture.Db.Tournaments.SingleAsync(x => x.Id == saved.TournamentId)).Status);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public TournamentService Service { get; }
        public TournamentMatchService MatchService { get; }

        private TestDatabase(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new TournamentService(db);
            MatchService = new TournamentMatchService(db);
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, db);
        }

        public async Task<User> AddUserAsync(string account)
        {
            var user = new User
            {
                Account = account,
                PasswordHash = "hash",
                DisplayName = account,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        public async Task<List<int>> AddBladesAsync(int userId, string prefix)
        {
            var now = DateTime.UtcNow;
            var blades = Enumerable.Range(1, 3).Select(index => new Beyblade
            {
                UserId = userId,
                Name = $"{prefix}-{index}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();
            Db.Beyblades.AddRange(blades);
            await Db.SaveChangesAsync();
            return blades.Select(x => x.Id).ToList();
        }

        public async Task<Tournament> CreateIndividualTournamentAsync(int organizerId, int capacity)
        {
            var result = await Service.CreateAsync(organizerId, new CreateTournamentRequest(
                "Individual Cup",
                TournamentRuleSet.IndividualThreeBladeFourPoints,
                TournamentRegistrationMode.Individual,
                TournamentFormat.SingleElimination,
                capacity,
                null));
            return result.Value!;
        }

        public async Task<Tournament> CreateTeamTournamentAsync(int organizerId)
        {
            var result = await Service.CreateAsync(organizerId, new CreateTournamentRequest(
                "Team Cup",
                TournamentRuleSet.DuoSixBladeEightPoints,
                TournamentRegistrationMode.CompleteTeam,
                TournamentFormat.SingleElimination,
                8,
                null));
            return result.Value!;
        }

        public async Task<(Tournament Tournament, User Organizer, TournamentMatch Match)> CreateStartedTeamMatchAsync(TournamentRuleSet ruleSet)
        {
            var organizer = await AddUserAsync($"organizer-{ruleSet}");
            var rule = TournamentRuleCatalog.Get(ruleSet);
            var created = await Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
                $"{ruleSet} Cup", ruleSet, TournamentRegistrationMode.CompleteTeam,
                TournamentFormat.SingleElimination, 2, null));
            var tournament = created.Value!;
            for (var teamIndex = 1; teamIndex <= 2; teamIndex++)
            {
                var representative = await AddUserAsync($"{ruleSet}-team{teamIndex}-member1");
                var entry = (await Service.CreateTemporaryTeamAsync(tournament.Id, representative.Id, $"Team {teamIndex}")).Value!;
                for (var memberIndex = 2; memberIndex <= rule.TeamSize!.Value; memberIndex++)
                {
                    var member = await AddUserAsync($"{ruleSet}-team{teamIndex}-member{memberIndex}");
                    Assert.True((await Service.InviteTeamMemberAsync(tournament.Id, entry.Id, representative.Id, member.Account)).Succeeded);
                    var invitation = await Db.TournamentInvitations.SingleAsync(x => x.TournamentEntryId == entry.Id && x.InvitedUserId == member.Id && x.Status == TournamentInvitationStatus.Pending);
                    Assert.True((await Service.RespondToTeamInvitationAsync(invitation.Id, member.Id, true)).Succeeded);
                }
                Assert.True((await Service.RegisterCompleteTeamAsync(tournament.Id, entry.Id, representative.Id)).Succeeded);
            }
            Assert.True((await Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
            Assert.True((await Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 11)).Succeeded);
            Assert.True((await Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
            Db.ChangeTracker.Clear();
            var match = await Db.TournamentMatches.Include(x => x.Participants).ThenInclude(x => x.User)
                .SingleAsync(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            return (tournament, organizer, match);
        }

        public async Task<Tournament> CreateSystemPairingTournamentAsync(int organizerId, int teamCapacity)
        {
            var result = await Service.CreateAsync(organizerId, new CreateTournamentRequest(
                "System Pairing Cup",
                TournamentRuleSet.DuoSixBladeEightPoints,
                TournamentRegistrationMode.SystemAssignedTeam,
                TournamentFormat.SingleElimination,
                teamCapacity,
                null));
            return result.Value!;
        }

        public async Task<(Tournament Tournament, List<User> Players)> CreateClosedIndividualTournamentAsync(
            int organizerId,
            int entryCount,
            TournamentFormat format)
        {
            var result = await Service.CreateAsync(organizerId, new CreateTournamentRequest(
                "Scheduled Cup",
                TournamentRuleSet.IndividualThreeBladeFourPoints,
                TournamentRegistrationMode.Individual,
                format,
                entryCount,
                null));
            var players = new List<User>();
            for (var index = 1; index <= entryCount; index++)
            {
                var player = await AddUserAsync($"scheduled-{format}-{index}");
                players.Add(player);
                Assert.True((await Service.RegisterIndividualAsync(result.Value!.Id, player.Id)).Succeeded);
            }
            Assert.True((await Service.CloseRegistrationAsync(result.Value!.Id, organizerId)).Succeeded);
            return (result.Value!, players);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
