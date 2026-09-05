using System.Security.Claims;
using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
        var invitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, organizer.Account)).Value!;
        Assert.False((await fixture.Service.CloseRegistrationAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.False((await fixture.Service.RegisterIndividualAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.SingleAsync();
        Assert.Equal(TournamentRegistrationStage.Closed, saved.RegistrationStage);
        Assert.NotNull(saved.RegistrationClosedAtUtc);
        var invalidated = await fixture.Db.TournamentInvitations.SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(TournamentInvitationStatus.Invalidated, invalidated.Status);
        Assert.NotNull(invalidated.InvalidatedAtUtc);
    }

    [Fact]
    public async Task ReopenRegistration_IndividualRequiresOrganizerAndClearsClosedState()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("reopen-individual-organizer");
        var first = await fixture.AddUserAsync("reopen-individual-first");
        var second = await fixture.AddUserAsync("reopen-individual-second");
        var newcomer = await fixture.AddUserAsync("reopen-individual-new");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 4);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, second.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);

        Assert.False((await fixture.Service.ReopenRegistrationAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, newcomer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var reopened = await fixture.Db.Tournaments.SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentRegistrationStage.Open, reopened.RegistrationStage);
        Assert.Null(reopened.RegistrationClosedAtUtc);
        Assert.Equal(3, await fixture.Db.TournamentEntries.CountAsync(x =>
            x.TournamentId == tournament.Id && x.Status == TournamentEntryStatus.Registered));
    }

    [Fact]
    public async Task ReopenRegistration_CompleteTeamKeepsRegisteredTeamsAndAllowsNewTeams()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("reopen-team-organizer");
        var tournament = await fixture.CreateTeamTournamentAsync(organizer.Id);
        for (var teamIndex = 1; teamIndex <= 2; teamIndex++)
        {
            var representative = await fixture.AddUserAsync($"reopen-team-{teamIndex}-rep");
            var teammate = await fixture.AddUserAsync($"reopen-team-{teamIndex}-mate");
            var entry = (await fixture.Service.CreateTemporaryTeamAsync(
                tournament.Id, representative.Id, $"Reopen Team {teamIndex}")).Value!;
            Assert.True((await fixture.Service.InviteTeamMemberAsync(
                tournament.Id, entry.Id, representative.Id, teammate.Account)).Succeeded);
            var invitation = await fixture.Db.TournamentInvitations.SingleAsync(x =>
                x.TournamentEntryId == entry.Id && x.InvitedUserId == teammate.Id);
            Assert.True((await fixture.Service.RespondToTeamInvitationAsync(
                invitation.Id, teammate.Id, true)).Succeeded);
            Assert.True((await fixture.Service.RegisterCompleteTeamAsync(
                tournament.Id, entry.Id, representative.Id)).Succeeded);
        }
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);

        Assert.True((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        var newRepresentative = await fixture.AddUserAsync("reopen-team-new-rep");
        Assert.True((await fixture.Service.CreateTemporaryTeamAsync(
            tournament.Id, newRepresentative.Id, "New Team")).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var reopened = await fixture.Db.Tournaments.Include(x => x.Entries)
            .SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentRegistrationStage.Open, reopened.RegistrationStage);
        Assert.Equal(2, reopened.Entries.Count(x => x.Status == TournamentEntryStatus.Registered));
        Assert.Single(reopened.Entries, x => x.Status == TournamentEntryStatus.Pending);
    }

    [Fact]
    public async Task ReopenRegistration_ScheduleDraftClearsMatchesAndSchedulePositions()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("reopen-draft-organizer");
        var first = await fixture.AddUserAsync("reopen-draft-first");
        var second = await fixture.AddUserAsync("reopen-draft-second");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 4);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, first.Id)).Succeeded);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, second.Id)).Succeeded);
        Assert.True((await fixture.Service.CloseRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 17)).Succeeded);
        Assert.NotEmpty(await fixture.Db.TournamentMatches.Where(x => x.TournamentId == tournament.Id).ToListAsync());
        Assert.All(await fixture.Db.TournamentEntries.Where(x => x.TournamentId == tournament.Id).ToListAsync(),
            entry => Assert.NotNull(entry.SchedulePosition));

        Assert.True((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var reopened = await fixture.Db.Tournaments.Include(x => x.Entries).Include(x => x.Matches)
            .SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentRegistrationStage.Open, reopened.RegistrationStage);
        Assert.Empty(reopened.Matches);
        Assert.All(reopened.Entries, entry => Assert.Null(entry.SchedulePosition));
        Assert.Null(reopened.RegistrationClosedAtUtc);
    }

    [Fact]
    public async Task ReopenRegistration_RejectsTournamentAfterFormalStart()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("reopen-started-organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 23)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);

        Assert.False((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(TournamentStatus.InProgress,
            (await fixture.Db.Tournaments.SingleAsync(x => x.Id == tournament.Id)).Status);
        Assert.NotEmpty(await fixture.Db.TournamentMatches.Where(x => x.TournamentId == tournament.Id).ToListAsync());
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
    public async Task ParticipantInvitation_RequiresOrganizerAndUsesServerValidatedUserId()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("invite-organizer");
        var outsider = await fixture.AddUserAsync("invite-outsider");
        var accountTarget = await fixture.AddUserAsync("account-target");
        var displayTarget = await fixture.AddUserAsync("display-target");
        displayTarget.DisplayName = "Unique Player";
        await fixture.Db.SaveChangesAsync();
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 8);
        var teamTournament = await fixture.CreateTeamTournamentAsync(organizer.Id);

        Assert.False((await fixture.Service.InviteParticipantAsync(
            tournament.Id, outsider.Id, accountTarget.Id)).Succeeded);
        Assert.True((await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, accountTarget.Id)).Succeeded);
        Assert.True((await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, displayTarget.Id)).Succeeded);
        Assert.False((await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, accountTarget.Id)).Succeeded);
        Assert.False((await fixture.Service.InviteParticipantAsync(
            teamTournament.Id, organizer.Id, outsider.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var invitations = await fixture.Db.TournamentInvitations
            .Where(x => x.TournamentId == tournament.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, invitations.Count);
        Assert.All(invitations, invitation =>
        {
            Assert.Equal(TournamentInvitationType.Tournament, invitation.Type);
            Assert.Equal(TournamentInvitationStatus.Pending, invitation.Status);
            Assert.Null(invitation.TournamentEntryId);
        });
        Assert.Empty(await fixture.Db.TournamentEntries
            .Where(x => x.TournamentId == tournament.Id).ToListAsync());
    }

    [Fact]
    public async Task ParticipantInvitation_AcceptOrDeclinePreservesHistoryAndWaitingForMeBoundary()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("response-organizer");
        var first = await fixture.AddUserAsync("response-first");
        var second = await fixture.AddUserAsync("response-second");
        var outsider = await fixture.AddUserAsync("response-outsider");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 4);
        var firstInvitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, first.Account)).Value!;
        var secondInvitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, second.Account)).Value!;

        var waiting = await fixture.Service.GetListAsync(first.Id, TournamentListFilter.WaitingForMe);
        Assert.Single(waiting.Items);
        Assert.True(waiting.Items[0].HasPendingAction);
        Assert.NotNull(await fixture.Service.GetPendingParticipantInvitationAsync(tournament.Id, first.Id));
        Assert.False((await fixture.Service.RespondToTournamentInvitationAsync(
            firstInvitation.Id, outsider.Id, true)).Succeeded);
        Assert.True((await fixture.Service.RespondToTournamentInvitationAsync(
            firstInvitation.Id, first.Id, true)).Succeeded);
        Assert.True((await fixture.Service.RespondToTournamentInvitationAsync(
            secondInvitation.Id, second.Id, false)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var accepted = await fixture.Db.TournamentInvitations.SingleAsync(x => x.Id == firstInvitation.Id);
        var declined = await fixture.Db.TournamentInvitations.SingleAsync(x => x.Id == secondInvitation.Id);
        Assert.Equal(TournamentInvitationStatus.Accepted, accepted.Status);
        Assert.NotNull(accepted.RespondedAtUtc);
        Assert.Equal(TournamentInvitationStatus.Declined, declined.Status);
        Assert.NotNull(declined.RespondedAtUtc);
        var entry = await fixture.Db.TournamentEntries.SingleAsync(x => x.TournamentId == tournament.Id);
        Assert.Equal(first.Id, entry.IndividualUserId);
        Assert.Equal(TournamentEntryStatus.Registered, entry.Status);
        Assert.NotNull(entry.RegistrationNumber);
        Assert.Empty((await fixture.Service.GetListAsync(first.Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Single((await fixture.Service.GetListAsync(first.Id, TournamentListFilter.Participating)).Items);
    }

    [Fact]
    public async Task ParticipantInvitation_ManualRegistrationAndCapacityInvalidatePendingInvitations()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("capacity-organizer");
        var first = await fixture.AddUserAsync("capacity-first");
        var second = await fixture.AddUserAsync("capacity-second");
        var third = await fixture.AddUserAsync("capacity-third");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 2);
        var firstInvitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, first.Account)).Value!;
        var secondInvitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, second.Account)).Value!;
        var thirdInvitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, third.Account)).Value!;

        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, second.Id)).Succeeded);
        Assert.True((await fixture.Service.RespondToTournamentInvitationAsync(
            firstInvitation.Id, first.Id, true)).Succeeded);
        Assert.False((await fixture.Service.RespondToTournamentInvitationAsync(
            thirdInvitation.Id, third.Id, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var invitations = await fixture.Db.TournamentInvitations
            .Where(x => x.TournamentId == tournament.Id).ToDictionaryAsync(x => x.Id);
        Assert.Equal(TournamentInvitationStatus.Accepted, invitations[firstInvitation.Id].Status);
        Assert.Equal(TournamentInvitationStatus.Invalidated, invitations[secondInvitation.Id].Status);
        Assert.Equal(TournamentInvitationStatus.Invalidated, invitations[thirdInvitation.Id].Status);
        Assert.NotNull(invitations[secondInvitation.Id].InvalidatedAtUtc);
        Assert.NotNull(invitations[thirdInvitation.Id].InvalidatedAtUtc);
        Assert.Equal(2, await fixture.Db.TournamentEntries.CountAsync(x =>
            x.TournamentId == tournament.Id && x.Status == TournamentEntryStatus.Registered));
        Assert.Equal(TournamentRegistrationStage.CapacityReached,
            (await fixture.Db.Tournaments.SingleAsync(x => x.Id == tournament.Id)).RegistrationStage);
    }

    [Fact]
    public async Task ParticipantInvitation_AcceptReusesWithdrawnEntry()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("reactivate-organizer");
        var player = await fixture.AddUserAsync("reactivate-player");
        var tournament = await fixture.CreateIndividualTournamentAsync(organizer.Id, 4);
        Assert.True((await fixture.Service.RegisterIndividualAsync(tournament.Id, player.Id)).Succeeded);
        var originalEntryId = (await fixture.Db.TournamentEntries.SingleAsync()).Id;
        Assert.True((await fixture.Service.WithdrawAsync(tournament.Id, player.Id)).Succeeded);
        var invitation = (await fixture.Service.InviteParticipantAsync(
            tournament.Id, organizer.Id, player.Account)).Value!;

        Assert.True((await fixture.Service.RespondToTournamentInvitationAsync(
            invitation.Id, player.Id, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var entry = await fixture.Db.TournamentEntries.SingleAsync();
        Assert.Equal(originalEntryId, entry.Id);
        Assert.Equal(TournamentEntryStatus.Registered, entry.Status);
        Assert.Null(entry.WithdrawnAtUtc);
        Assert.NotNull(entry.RegistrationNumber);
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
        Assert.True((await fixture.Service.ReopenRegistrationAsync(tournament.Id, organizer.Id)).Succeeded);
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

    [Theory]
    [InlineData(TournamentFormat.SingleElimination)]
    [InlineData(TournamentFormat.DoubleElimination)]
    public async Task CompletedEliminationSchedule_PublishesFormalStandingsFromPersistedResults(
        TournamentFormat format)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync($"standings-organizer-{format}");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(organizer.Id, 4, format);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 37)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);

        for (var completedMatchCount = 0; completedMatchCount < 20; completedMatchCount++)
        {
            fixture.Db.ChangeTracker.Clear();
            tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants)
                .SingleAsync(x => x.Id == tournament.Id);
            if (tournament.Status == TournamentStatus.Completed) break;
            var active = tournament.Matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
                active, active.SideAEntryId!.Value, active.SideBEntryId!.Value,
                TournamentMatchStatus.Completed, "TestResult", DateTime.UtcNow);
            await fixture.Db.SaveChangesAsync();
        }

        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Completed, tournament.Status);
        var standings = await new TournamentStandingsService(fixture.Db).GetStandingsAsync(tournament.Id);

        Assert.Equal(4, standings.Count);
        Assert.Equal(format == TournamentFormat.SingleElimination ? [1, 2, 3, 3] : [1, 2, 3, 4],
            standings.Select(x => x.Rank));
        Assert.Equal(TournamentStandingPlacement.Champion, standings[0].Placement);
        Assert.Equal(TournamentStandingPlacement.RunnerUp, standings[1].Placement);
        if (format == TournamentFormat.SingleElimination)
            Assert.All(standings.Skip(1), x => Assert.Equal(1, x.Losses));
        else
            Assert.All(standings.Skip(1), x => Assert.Equal(2, x.Losses));
    }

    [Fact]
    public async Task RoundRobinChampionTie_CreatesAndCompletesPlayoffBeforeTournamentCompletion()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("round-robin-playoff-organizer");
        var (tournament, _) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 3, TournamentFormat.RoundRobin);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 41)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        var entryIds = await fixture.Db.TournamentEntries.Where(x => x.TournamentId == tournament.Id)
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

        for (var completedMatchCount = 0; completedMatchCount < 3; completedMatchCount++)
        {
            fixture.Db.ChangeTracker.Clear();
            tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants)
                .SingleAsync(x => x.Id == tournament.Id);
            var active = tournament.Matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            var pair = new HashSet<int> { active.SideAEntryId!.Value, active.SideBEntryId!.Value };
            var winnerEntryId = pair.SetEquals([entryIds[0], entryIds[1]])
                ? entryIds[0]
                : pair.SetEquals([entryIds[1], entryIds[2]])
                    ? entryIds[1]
                    : entryIds[2];
            var loserEntryId = active.SideAEntryId == winnerEntryId
                ? active.SideBEntryId!.Value
                : active.SideAEntryId.Value;
            await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
                active, winnerEntryId, loserEntryId,
                TournamentMatchStatus.Completed, "TestResult", DateTime.UtcNow);
            await fixture.Db.SaveChangesAsync();
        }

        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants)
            .SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.InProgress, tournament.Status);
        Assert.Null(tournament.CompletedAtUtc);
        Assert.Equal(2, tournament.Matches.Count(x => x.Bracket == TournamentBracket.Playoff));
        Assert.Single(tournament.Matches, x => x.Bracket == TournamentBracket.Playoff &&
            x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        var publicDetails = await fixture.Service.GetPublicDetailsAsync(tournament.Id, organizer.Id);
        Assert.Equal(2, publicDetails!.Matches.Count(x => x.Bracket == TournamentBracket.Playoff));
        Assert.Single(publicDetails.Matches, x => x.Bracket == TournamentBracket.Playoff && x.IsCurrent);
        var regulationMatch = tournament.Matches.First(x => x.Bracket == TournamentBracket.RoundRobin && !x.IsBye);
        var blockedReopen = await fixture.MatchService.VoidAndReopenAsync(
            regulationMatch.Id, organizer.Id, "late correction", true);
        Assert.False(blockedReopen.Succeeded);
        Assert.Contains("冠軍加賽已建立", blockedReopen.Error);
        var tiedStandings = await new TournamentStandingsService(fixture.Db).GetStandingsAsync(tournament.Id);
        Assert.All(tiedStandings, x => Assert.Equal(1, x.Rank));

        for (var completedPlayoffCount = 0; completedPlayoffCount < 2; completedPlayoffCount++)
        {
            fixture.Db.ChangeTracker.Clear();
            tournament = await fixture.Db.Tournaments.Include(x => x.Matches).ThenInclude(x => x.Participants)
                .SingleAsync(x => x.Id == tournament.Id);
            var active = tournament.Matches.Single(x => x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            Assert.Equal(TournamentBracket.Playoff, active.Bracket);
            await new TournamentProgressionService(fixture.Db).CompleteMatchAndAdvanceAsync(
                active, active.SideAEntryId!.Value, active.SideBEntryId!.Value,
                TournamentMatchStatus.Completed, "ChampionPlayoff", DateTime.UtcNow);
            await fixture.Db.SaveChangesAsync();
        }

        fixture.Db.ChangeTracker.Clear();
        tournament = await fixture.Db.Tournaments.Include(x => x.Matches).SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Completed, tournament.Status);
        Assert.NotNull(tournament.CompletedAtUtc);
        Assert.All(tournament.Matches.Where(x => x.Bracket == TournamentBracket.Playoff),
            x => Assert.Equal(TournamentMatchStatus.Completed, x.Status));
        Assert.All(await fixture.Db.TournamentMatchParticipants
            .Where(x => x.TournamentMatch!.TournamentId == tournament.Id && x.TournamentMatch.Bracket == TournamentBracket.Playoff)
            .GroupBy(x => x.TournamentMatchId).ToListAsync(), x => Assert.Equal(2, x.Count()));
        var finalStandings = await new TournamentStandingsService(fixture.Db).GetStandingsAsync(tournament.Id);
        Assert.Equal([1, 2, 2], finalStandings.Select(x => x.Rank));
        Assert.Equal(TournamentStandingPlacement.Champion, finalStandings[0].Placement);
        Assert.Equal(3, finalStandings.Sum(x => x.Wins));
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
        var standings = await new TournamentStandingsService(fixture.Db).GetStandingsAsync(tournament.Id);
        Assert.Equal(5, standings.Count);
        Assert.Equal(tournament.Matches.Count(x => !x.IsBye), standings.Sum(x => x.Wins));
        Assert.All(standings, x => Assert.Equal(0, x.ScoreDifference));
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
        await PartCatalog.ImportAsync(fixture.Db);
        var configurationService = new BeybladeConfigurationService(fixture.Db);
        string[] bladeNames = ["時鐘幻象", "地獄鐮刀", "騎士長槍"];
        string[] ratchetNames = ["4-55", "1-50", "3-60"];
        string[] bitNames = ["S", "B", "J"];
        var configurationIds = new Dictionary<int, int>();
        var configurationPartsByUser = new Dictionary<int, int[]>();
        var duplicateConfigurationIds = new Dictionary<int, int>();
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
            var memberIndex = participants
                .Where(x => x.TournamentEntryId == participant.TournamentEntryId)
                .OrderBy(x => x.UserId)
                .ToList()
                .IndexOf(participant);
            var configurationPartIds = await fixture.Db.Parts.Where(x =>
                (x.Category == PartCategory.Blade && x.Name == bladeNames[memberIndex]) ||
                (x.Category == PartCategory.Ratchet && x.Name == ratchetNames[memberIndex]) ||
                (x.Category == PartCategory.Bit && x.Name == bitNames[memberIndex])).Select(x => x.Id).ToArrayAsync();
            configurationPartsByUser[participant.UserId] = configurationPartIds;
            Assert.True((await configurationService.RecordAsync(participant.UserId, blades[0], configurationPartIds)).Succeeded);
            configurationIds[blades[0]] = await fixture.Db.BeybladeConfigurations.Where(x => x.BeybladeId == blades[0]).Select(x => x.Id).SingleAsync();
            var variant = await fixture.Db.Parts.Where(x => configurationPartIds.Contains(x.Id) && x.Category != PartCategory.Bit).Select(x => x.Id).ToListAsync();
            variant.Add(await fixture.Db.Parts.Where(x => x.Category == PartCategory.Bit && x.Name == "A").Select(x => x.Id).SingleAsync());
            Assert.True((await configurationService.RecordAsync(participant.UserId, blades[0], variant)).Succeeded);
            if (participant == participants[^1])
                configurationIds[blades[0]] = (await configurationService.GetMineAsync(participant.UserId, blades[0]))!.Id;
        }
        foreach (var team in participants.GroupBy(x => x.TournamentEntryId))
        {
            var teammates = team.OrderBy(x => x.UserId).ToList();
            if (teammates.Count < 2) continue;
            var second = teammates[1];
            var bladeId = bladesByUser[second.UserId][0];
            var firstParts = configurationPartsByUser[teammates[0].UserId];
            var secondParts = configurationPartsByUser[second.UserId];
            var duplicateParts = await fixture.Db.Parts
                .Where(x =>
                    (secondParts.Contains(x.Id) && x.Category == PartCategory.Blade) ||
                    (firstParts.Contains(x.Id) && x.Category != PartCategory.Blade))
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.True((await configurationService.RecordAsync(
                second.UserId,
                bladeId,
                duplicateParts)).Succeeded);
            duplicateConfigurationIds[bladeId] = (await configurationService.GetMineAsync(second.UserId, bladeId))!.Id;
        }
        foreach (var participant in participants)
        {
            var selectedBlades = bladesByUser[participant.UserId];
            var selectedVersions = selectedBlades.Select(id => configurationIds.GetValueOrDefault(id)).ToArray();
            var foreignVersions = selectedVersions.ToArray();
            foreignVersions[0] = configurationIds[bladesByUser[participants.First(x => x.UserId != participant.UserId).UserId][0]];
            Assert.False((await fixture.MatchService.SubmitLineupAsync(match.Id, participant.UserId, selectedBlades, foreignVersions)).Succeeded);
            if (duplicateConfigurationIds.TryGetValue(selectedBlades[0], out var duplicateConfigurationId))
            {
                var duplicatedVersions = selectedVersions.ToArray();
                duplicatedVersions[0] = duplicateConfigurationId;
                var duplicated = await fixture.MatchService.SubmitLineupAsync(
                    match.Id, participant.UserId, selectedBlades, duplicatedVersions);
                Assert.False(duplicated.Succeeded);
                Assert.Contains("不可重複使用零件", duplicated.Error);
            }
            Assert.True((await fixture.MatchService.SubmitLineupAsync(match.Id, participant.UserId, selectedBlades, selectedVersions)).Succeeded);
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
            Assert.Equal(configurationIds.TryGetValue(row.PlayerABeybladeId, out var aConfig) ? (int?)aConfig : null, row.PlayerAConfigurationId);
            Assert.Equal(configurationIds.TryGetValue(row.PlayerBBeybladeId, out var bConfig) ? (int?)bConfig : null, row.PlayerBConfigurationId);
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
        Assert.All(sequenceTwo.Match.Battle.Lineups, row =>
        {
            Assert.Equal(configurationIds.TryGetValue(row.PlayerABeybladeId, out var aConfig) ? (int?)aConfig : null, row.PlayerAConfigurationId);
            Assert.Equal(configurationIds.TryGetValue(row.PlayerBBeybladeId, out var bConfig) ? (int?)bConfig : null, row.PlayerBConfigurationId);
        });
        Assert.Equal(expectedRounds + 1, sequenceTwo.Match.Battle.Rounds.Single(x => x.Status == BattleRoundStatus.InProgress).RoundNo);
        Assert.Equal(expectedRounds, sequenceTwo.Match.Battle.SideAScore);
    }

    [Fact]
    public async Task CancelTournament_PreservesCompletedRoundsAndExcludesCurrentRoundEvents()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("cancel-organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var player in players)
            bladesByUser[player.Id] = await fixture.AddBladesAsync(player.Id, $"cancel-{player.Id}");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 31)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.Include(x => x.Participants)
            .SingleAsync(x => x.TournamentId == tournament.Id && x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        foreach (var participant in match.Participants)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, participant.UserId, true)).Succeeded);
        foreach (var participant in match.Participants)
            Assert.True((await fixture.MatchService.SubmitLineupAsync(match.Id, participant.UserId, bladesByUser[participant.UserId])).Succeeded);
        foreach (var participant in match.Participants)
            Assert.True((await fixture.MatchService.ConfirmLineupAsync(match.Id, participant.UserId)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.MatchService.AssignSidesAndStartAsync(match.Id, organizer.Id, BattleSide.B);
        Assert.True(started.Succeeded);
        var battleService = new BattleService(fixture.Db);
        var battle = (await battleService.GetBattleAsync(started.Value, organizer.Id)).Value!;
        var completedRound = battle.Rounds.Single();
        var winningUserId = battle.PlayerAId!.Value;
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, completedRound.Id, organizer.Id, winningUserId, ResultType.SpinFinish)).Succeeded);
        var currentRound = (await battleService.CompleteRoundAsync(battle.Id, completedRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, currentRound.Id, organizer.Id, currentRound.PlayerAId!.Value, ResultType.SpinFinish)).Succeeded);

        Assert.False((await fixture.Service.CancelTournamentAsync(tournament.Id, players[0].Id, "無權限")).Succeeded);
        Assert.True((await fixture.Service.CancelTournamentAsync(tournament.Id, organizer.Id, "場地臨時停用")).Succeeded);
        Assert.False((await fixture.Service.CancelTournamentAsync(tournament.Id, organizer.Id, null)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments.AsSplitQuery()
            .Include(x => x.Matches).ThenInclude(x => x.Participants)
            .Include(x => x.Matches).ThenInclude(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
            .SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Cancelled, saved.Status);
        Assert.Equal("場地臨時停用", saved.CancellationReason);
        Assert.NotNull(saved.CancelledAtUtc);
        var savedMatch = Assert.Single(saved.Matches);
        Assert.Equal(TournamentMatchStatus.Cancelled, savedMatch.Status);
        Assert.Null(savedMatch.WinnerEntryId);
        Assert.Null(savedMatch.LoserEntryId);
        var savedBattle = savedMatch.Battle!;
        Assert.Equal(BattleStatus.Cancelled, savedBattle.Status);
        Assert.Null(savedBattle.WinningPlayerId);
        Assert.Null(savedBattle.WinningSide);
        Assert.Equal(1, savedBattle.SideAScore);
        Assert.Equal(0, savedBattle.SideBScore);
        var preserved = savedBattle.Rounds.Single(x => x.Id == completedRound.Id);
        Assert.Equal(BattleRoundStatus.Completed, preserved.Status);
        Assert.All(preserved.Events, x => Assert.True(x.IsEffective));
        var excluded = savedBattle.Rounds.Single(x => x.Id == currentRound.Id);
        Assert.Equal(BattleRoundStatus.InProgress, excluded.Status);
        Assert.NotEmpty(excluded.Events);
        Assert.All(excluded.Events, x => Assert.False(x.IsEffective));

        var bladeStatistics = await new StatisticsService(fixture.Db).GetBeybladeStatisticsAsync(winningUserId, null);
        var winningBlade = bladeStatistics.Single(x => x.BeybladeId == preserved.PlayerABeybladeId);
        Assert.Equal(1, winningBlade.Wins);
        Assert.Equal(1, winningBlade.Score);
    }

    [Fact]
    public async Task CancelTournament_InvalidatesPendingInvitationsAndPreservesRegistrationData()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("registration-cancel-organizer");
        var representative = await fixture.AddUserAsync("registration-cancel-representative");
        var invited = await fixture.AddUserAsync("registration-cancel-invited");
        var tournament = (await fixture.Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "保留資料的團體賽",
            TournamentRuleSet.DuoFourBladeSixPoints,
            TournamentRegistrationMode.CompleteTeam,
            TournamentFormat.SingleElimination,
            8,
            "原始規則備註"))).Value!;
        var rulesSnapshot = tournament.RulesSnapshot;
        var team = (await fixture.Service.CreateTemporaryTeamAsync(
            tournament.Id, representative.Id, "保留的臨時隊伍")).Value!;
        Assert.True((await fixture.Service.InviteTeamMemberAsync(
            tournament.Id, team.Id, representative.Id, invited.Account)).Succeeded);

        Assert.True((await fixture.Service.CancelTournamentAsync(
            tournament.Id, organizer.Id, "報名階段取消")).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Tournaments
            .Include(x => x.Entries).ThenInclude(x => x.Members)
            .Include(x => x.Invitations)
            .SingleAsync(x => x.Id == tournament.Id);
        Assert.Equal(TournamentStatus.Cancelled, saved.Status);
        Assert.Equal("保留資料的團體賽", saved.Name);
        Assert.Equal("原始規則備註", saved.Notes);
        Assert.Equal(rulesSnapshot, saved.RulesSnapshot);
        Assert.Single(saved.Entries);
        Assert.Equal("保留的臨時隊伍", saved.Entries.Single().TeamName);
        var invitation = Assert.Single(saved.Invitations);
        Assert.Equal(TournamentInvitationStatus.Invalidated, invitation.Status);
        Assert.NotNull(invitation.InvalidatedAtUtc);
    }

    [Fact]
    public async Task NoShow_RequiresOrganizerConfirmedPendingEntryAndAdvancesOnceWithoutBattleOrStatistics()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("no-show-organizer");
        var outsider = await fixture.AddUserAsync("no-show-outsider");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 31)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.Include(x => x.Participants)
            .SingleAsync(x => x.TournamentId == tournament.Id &&
                x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        var attending = match.Participants.Single(x => x.UserId == players[0].Id);
        var absent = match.Participants.Single(x => x.UserId == players[1].Id);

        Assert.True((await fixture.MatchService.RespondParticipationAsync(
            match.Id, attending.UserId, true)).Succeeded);
        Assert.False((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, absent.TournamentEntryId, null, false)).Succeeded);
        Assert.False((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, outsider.Id, absent.TournamentEntryId, null, true)).Succeeded);
        Assert.False((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, int.MaxValue, null, true)).Succeeded);
        Assert.False((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, attending.TournamentEntryId, null, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var stillWaiting = await fixture.Db.TournamentMatches.SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, stillWaiting.Status);
        Assert.Null(stillWaiting.WinnerEntryId);
        Assert.Empty(await fixture.Db.Battles.ToListAsync());

        Assert.True((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, absent.TournamentEntryId, "交通中斷", true)).Succeeded);
        Assert.False((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, absent.TournamentEntryId, null, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.TournamentMatches
            .Include(x => x.Tournament)
            .Include(x => x.Participants)
            .SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentMatchStatus.Walkover, saved.Status);
        Assert.Equal(attending.TournamentEntryId, saved.WinnerEntryId);
        Assert.Equal(absent.TournamentEntryId, saved.LoserEntryId);
        Assert.Equal("NoShow: 交通中斷", saved.ResolutionReason);
        Assert.Equal(TournamentParticipationStatus.Accepted,
            saved.Participants.Single(x => x.UserId == attending.UserId).Status);
        Assert.Equal(TournamentParticipationStatus.NoShow,
            saved.Participants.Single(x => x.UserId == absent.UserId).Status);
        Assert.Equal(TournamentStatus.Completed, saved.Tournament.Status);
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
        var statistics = await new StatisticsService(fixture.Db)
            .GetUserStatisticsSectionsAsync(attending.UserId);
        Assert.Equal(0, statistics.TournamentIndividual.Wins);
        Assert.Equal(0, statistics.TournamentIndividual.Losses);
        Assert.Equal(0, statistics.TournamentIndividual.Score);
        Assert.Equal(0, statistics.TournamentIndividual.AgainstScore);
    }

    [Fact]
    public async Task TeamNoShow_AwardsWholeEntryAndMarksOnlyItsPendingMembersAsNoShow()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (tournament, organizer, match) = await fixture.CreateStartedTeamMatchAsync(
            TournamentRuleSet.DuoFourBladeSixPoints);
        var absentEntryId = match.SideAEntryId!.Value;
        var winnerEntryId = match.SideBEntryId!.Value;

        Assert.True((await fixture.MatchService.DeclareNoShowAsync(
            match.Id, organizer.Id, absentEntryId, null, true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.TournamentMatches
            .Include(x => x.Tournament)
            .Include(x => x.Participants)
            .SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentMatchStatus.Walkover, saved.Status);
        Assert.Equal(winnerEntryId, saved.WinnerEntryId);
        Assert.Equal(absentEntryId, saved.LoserEntryId);
        Assert.Equal("NoShow", saved.ResolutionReason);
        Assert.All(saved.Participants.Where(x => x.TournamentEntryId == absentEntryId),
            x => Assert.Equal(TournamentParticipationStatus.NoShow, x.Status));
        Assert.All(saved.Participants.Where(x => x.TournamentEntryId == winnerEntryId),
            x => Assert.Equal(TournamentParticipationStatus.Invalidated, x.Status));
        Assert.Equal(TournamentStatus.Completed, saved.Tournament.Status);
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
    }

    [Fact]
    public async Task WaitingForMe_FollowsExactParticipantAndOrganizerMatchActions()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("action-organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 41)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.SingleAsync(x => x.TournamentId == tournament.Id);

        var playerWaiting = Assert.Single((await fixture.Service.GetListAsync(
            players[0].Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal(match.Id, playerWaiting.ActionMatchId);
        Assert.Equal("回覆出賽通知", playerWaiting.PendingActionLabel);
        var organizerWaiting = Assert.Single((await fixture.Service.GetListAsync(
            organizer.Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal("檢視出賽回覆／判定未到", organizerWaiting.PendingActionLabel);

        Assert.True((await fixture.MatchService.RespondParticipationAsync(
            match.Id, players[0].Id, true)).Succeeded);
        Assert.Empty((await fixture.Service.GetListAsync(
            players[0].Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.True((await fixture.MatchService.RespondParticipationAsync(
            match.Id, players[1].Id, true)).Succeeded);

        var lineupAction = Assert.Single((await fixture.Service.GetListAsync(
            players[0].Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal("提交私密陣容", lineupAction.PendingActionLabel);
        var blades = await fixture.AddBladesAsync(players[0].Id, "action-lineup");
        Assert.True((await fixture.MatchService.SubmitLineupAsync(
            match.Id, players[0].Id, blades)).Succeeded);
        Assert.Empty((await fixture.Service.GetListAsync(
            players[0].Id, TournamentListFilter.WaitingForMe)).Items);
    }

    [Fact]
    public async Task WaitingForMe_IncludesTeamCompletionAndOrganizerLifecycleActions()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("queue-team-organizer");
        var tournament = (await fixture.Service.CreateAsync(organizer.Id, new CreateTournamentRequest(
            "隊伍待辦賽事",
            TournamentRuleSet.DuoFourBladeSixPoints,
            TournamentRegistrationMode.CompleteTeam,
            TournamentFormat.SingleElimination,
            2,
            null))).Value!;

        async Task<TournamentEntry> CreateCompleteTeamAsync(string prefix)
        {
            var representative = await fixture.AddUserAsync($"{prefix}-representative");
            var member = await fixture.AddUserAsync($"{prefix}-member");
            var entry = (await fixture.Service.CreateTemporaryTeamAsync(
                tournament.Id, representative.Id, prefix)).Value!;
            var incomplete = Assert.Single((await fixture.Service.GetListAsync(
                representative.Id, TournamentListFilter.WaitingForMe)).Items);
            Assert.Equal("完成隊伍組建", incomplete.PendingActionLabel);

            Assert.True((await fixture.Service.InviteTeamMemberAsync(
                tournament.Id, entry.Id, representative.Id, member.Account)).Succeeded);
            var invitation = await fixture.Db.TournamentInvitations.SingleAsync(x =>
                x.TournamentEntryId == entry.Id && x.InvitedUserId == member.Id &&
                x.Status == TournamentInvitationStatus.Pending);
            Assert.True((await fixture.Service.RespondToTeamInvitationAsync(
                invitation.Id, member.Id, true)).Succeeded);
            var complete = Assert.Single((await fixture.Service.GetListAsync(
                representative.Id, TournamentListFilter.WaitingForMe)).Items);
            Assert.Equal("確認整隊報名", complete.PendingActionLabel);
            Assert.True((await fixture.Service.RegisterCompleteTeamAsync(
                tournament.Id, entry.Id, representative.Id)).Succeeded);
            Assert.Empty((await fixture.Service.GetListAsync(
                representative.Id, TournamentListFilter.WaitingForMe)).Items);
            return entry;
        }

        _ = await CreateCompleteTeamAsync("queue-team-a");
        _ = await CreateCompleteTeamAsync("queue-team-b");
        var capacityAction = Assert.Single((await fixture.Service.GetListAsync(
            organizer.Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal("關閉報名並準備賽程", capacityAction.PendingActionLabel);

        Assert.True((await fixture.Service.CloseRegistrationAsync(
            tournament.Id, organizer.Id)).Succeeded);
        var scheduleAction = Assert.Single((await fixture.Service.GetListAsync(
            organizer.Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal("產生隊伍／賽程", scheduleAction.PendingActionLabel);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(
            tournament.Id, organizer.Id, 43)).Succeeded);
        var startAction = Assert.Single((await fixture.Service.GetListAsync(
            organizer.Id, TournamentListFilter.WaitingForMe)).Items);
        Assert.Equal("確認賽程並正式開始", startAction.PendingActionLabel);
    }

    [Fact]
    public async Task TournamentList_PrioritizesPendingActionsAndIncludesCompleteSummary()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var firstOrganizer = await fixture.AddUserAsync("summary-first-organizer");
        var secondOrganizer = await fixture.AddUserAsync("summary-second-organizer");
        var invited = await fixture.AddUserAsync("summary-invited");
        var actionable = (await fixture.Service.CreateAsync(firstOrganizer.Id, new CreateTournamentRequest(
            "需要回覆的賽事",
            TournamentRuleSet.IndividualThreeBladeFourPoints,
            TournamentRegistrationMode.Individual,
            TournamentFormat.Swiss,
            8,
            "請提前十五分鐘報到"))).Value!;
        Assert.True((await fixture.Service.InviteParticipantAsync(
            actionable.Id, firstOrganizer.Id, invited.Account)).Succeeded);
        _ = (await fixture.Service.CreateAsync(secondOrganizer.Id, new CreateTournamentRequest(
            "較新但無待辦的賽事",
            TournamentRuleSet.DuoFourBladeSixPoints,
            TournamentRegistrationMode.CompleteTeam,
            TournamentFormat.SingleElimination,
            4,
            null))).Value!;

        var list = await fixture.Service.GetListAsync(invited.Id, TournamentListFilter.All);
        var first = list.Items.First();
        Assert.Equal(actionable.Id, first.Id);
        Assert.True(first.HasPendingAction);
        Assert.True(first.HasPendingInvitation);
        Assert.Null(first.ActionMatchId);
        Assert.Equal("回覆邀請", first.PendingActionLabel);
        Assert.Equal(TournamentMode.Individual, first.Mode);
        Assert.Equal(TournamentRegistrationMode.Individual, first.RegistrationMode);
        Assert.Equal(TournamentFormat.Swiss, first.Format);
        Assert.Equal(TournamentRuleSet.IndividualThreeBladeFourPoints, first.RuleSet);
        Assert.Equal("請提前十五分鐘報到", first.Notes);
    }

    [Fact]
    public async Task PublicDetails_HidesPrivateSelectionsUntilLineupIsMaterialized()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("public-private-organizer");
        var spectator = await fixture.AddUserAsync("public-private-spectator");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var player in players)
            bladesByUser[player.Id] = await fixture.AddBladesAsync(player.Id, $"public-{player.Id}");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 47)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.Include(x => x.Participants)
            .SingleAsync(x => x.TournamentId == tournament.Id &&
                x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);

        var spectatorBefore = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        var spectatorMatch = Assert.Single(spectatorBefore.Matches, x => x.Id == match.Id);
        Assert.True(spectatorMatch.IsCurrent);
        Assert.False(spectatorMatch.CanOpenWorkspace);
        Assert.Null(spectatorMatch.Battle);
        Assert.Null(await fixture.MatchService.GetWorkspaceAsync(match.Id, spectator.Id));
        Assert.True(Assert.Single((await fixture.Service.GetPublicDetailsAsync(
            tournament.Id, players[0].Id))!.Matches, x => x.Id == match.Id).CanOpenWorkspace);
        Assert.True(Assert.Single((await fixture.Service.GetPublicDetailsAsync(
            tournament.Id, organizer.Id))!.Matches, x => x.Id == match.Id).CanOpenWorkspace);
        Assert.NotNull(await fixture.MatchService.GetWorkspaceAsync(match.Id, players[0].Id));
        Assert.NotNull(await fixture.MatchService.GetWorkspaceAsync(match.Id, organizer.Id));

        foreach (var participant in match.Participants)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(
                match.Id, participant.UserId, true)).Succeeded);
        var lineupStage = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        var privateToken = lineupStage.PollToken;
        Assert.Null(Assert.Single(lineupStage.Matches, x => x.Id == match.Id).Battle);

        Assert.True((await fixture.MatchService.SubmitLineupAsync(
            match.Id, players[0].Id, bladesByUser[players[0].Id])).Succeeded);
        var onePrivateSubmission = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        Assert.Equal(privateToken, onePrivateSubmission.PollToken);
        Assert.Null(Assert.Single(onePrivateSubmission.Matches, x => x.Id == match.Id).Battle);

        Assert.True((await fixture.MatchService.SubmitLineupAsync(
            match.Id, players[1].Id, bladesByUser[players[1].Id])).Succeeded);
        var publicLineup = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        Assert.NotEqual(privateToken, publicLineup.PollToken);
        var publicBattle = Assert.Single(publicLineup.Matches, x => x.Id == match.Id).Battle;
        Assert.NotNull(publicBattle);
        Assert.Equal(3, publicBattle.Lineup.Count);
        Assert.All(publicBattle.Lineup, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.PlayerADisplayName));
            Assert.False(string.IsNullOrWhiteSpace(x.PlayerABeybladeName));
            Assert.False(string.IsNullOrWhiteSpace(x.PlayerBDisplayName));
            Assert.False(string.IsNullOrWhiteSpace(x.PlayerBBeybladeName));
        });
        Assert.Null(await fixture.MatchService.GetWorkspaceAsync(match.Id, spectator.Id));
    }

    [Fact]
    public async Task PublicDetails_ShowsCompletedWinnerScoreSidesAndActualPlayers()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (tournament, organizer, players, match, battle) =
            await fixture.CreateStartedIndividualBattleAsync("public-result");
        var spectator = await fixture.AddUserAsync("public-result-spectator");
        var battleService = new BattleService(fixture.Db);
        var firstRound = battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, firstRound.Id, organizer.Id, battle.PlayerAId!.Value, ResultType.Extreme)).Succeeded);
        var secondRound = (await battleService.CompleteRoundAsync(
            battle.Id, firstRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, secondRound.Id, organizer.Id, battle.PlayerAId.Value, ResultType.SpinFinish)).Succeeded);
        Assert.True((await battleService.FinishBattleAsync(battle.Id, organizer.Id)).Succeeded);

        var details = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        var publicMatch = Assert.Single(details.Matches, x => x.Id == match.Id);
        var expectedWinner = players.Single(x => x.Id == battle.PlayerAId).DisplayName;
        var expectedLoser = players.Single(x => x.Id == battle.PlayerBId).DisplayName;
        Assert.Equal(TournamentStatus.Completed, details.Status);
        Assert.Equal(TournamentMatchStatus.Completed, publicMatch.Status);
        Assert.Equal(expectedWinner, publicMatch.WinnerLabel);
        Assert.Equal(expectedLoser, publicMatch.LoserLabel);
        Assert.False(publicMatch.CanOpenWorkspace);
        Assert.NotNull(publicMatch.CompletedAtUtc);
        var publicBattle = Assert.IsType<BeybladeRecordSystem.ViewModels.TournamentPublicBattleViewModel>(
            publicMatch.Battle);
        Assert.Equal(BattleStatus.Completed, publicBattle.Status);
        Assert.Equal(4, publicBattle.SideAScore);
        Assert.Equal(0, publicBattle.SideBScore);
        Assert.Equal(4, publicBattle.ScoreToWin);
        Assert.Equal(BattleSide.B, publicBattle.SideADesignation);
        Assert.Equal(3, publicBattle.Lineup.Count);
        Assert.Contains(publicBattle.Lineup, x => x.PlayerADisplayName == expectedWinner);
        Assert.Null(await fixture.MatchService.GetWorkspaceAsync(match.Id, spectator.Id));
    }

    [Fact]
    public async Task PublicDetails_CancelledTournamentKeepsCompletedPublicResultOnly()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("public-cancel-organizer");
        var spectator = await fixture.AddUserAsync("public-cancel-spectator");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 4, TournamentFormat.SingleElimination);
        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var player in players)
            bladesByUser[player.Id] = await fixture.AddBladesAsync(player.Id, $"public-cancel-{player.Id}");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 53)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var firstMatch = await fixture.Db.TournamentMatches.Include(x => x.Participants)
            .SingleAsync(x => x.TournamentId == tournament.Id &&
                x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        foreach (var participant in firstMatch.Participants)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(
                firstMatch.Id, participant.UserId, true)).Succeeded);
        foreach (var participant in firstMatch.Participants)
            Assert.True((await fixture.MatchService.SubmitLineupAsync(
                firstMatch.Id, participant.UserId, bladesByUser[participant.UserId])).Succeeded);
        foreach (var participant in firstMatch.Participants)
            Assert.True((await fixture.MatchService.ConfirmLineupAsync(
                firstMatch.Id, participant.UserId)).Succeeded);
        var started = await fixture.MatchService.AssignSidesAndStartAsync(
            firstMatch.Id, organizer.Id, BattleSide.X);
        Assert.True(started.Succeeded);
        var startedBattle = (await new BattleService(fixture.Db)
            .GetBattleAsync(started.Value, organizer.Id)).Value!;
        var battleService = new BattleService(fixture.Db);
        var firstRound = startedBattle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(
            startedBattle.Id, firstRound.Id, organizer.Id,
            startedBattle.PlayerAId!.Value, ResultType.Extreme)).Succeeded);
        var secondRound = (await battleService.CompleteRoundAsync(
            startedBattle.Id, firstRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(
            startedBattle.Id, secondRound.Id, organizer.Id,
            startedBattle.PlayerAId.Value, ResultType.SpinFinish)).Succeeded);
        Assert.True((await battleService.FinishBattleAsync(startedBattle.Id, organizer.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var nextMatch = await fixture.Db.TournamentMatches.SingleAsync(x =>
            x.TournamentId == tournament.Id &&
            x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        Assert.True((await fixture.Service.CancelTournamentAsync(
            tournament.Id, organizer.Id, "場地臨時關閉")).Succeeded);

        var details = (await fixture.Service.GetPublicDetailsAsync(tournament.Id, spectator.Id))!;
        Assert.Equal(TournamentStatus.Cancelled, details.Status);
        Assert.Equal("場地臨時關閉", details.CancellationReason);
        Assert.NotNull(details.CancelledAtUtc);
        var completedMatch = Assert.Single(details.Matches, x => x.Id == firstMatch.Id);
        Assert.Equal(TournamentMatchStatus.Completed, completedMatch.Status);
        Assert.NotNull(completedMatch.WinnerLabel);
        Assert.NotNull(completedMatch.Battle);
        Assert.Equal(4, completedMatch.Battle!.SideAScore);
        var cancelledNext = Assert.Single(details.Matches, x => x.Id == nextMatch.Id);
        Assert.Equal(TournamentMatchStatus.Cancelled, cancelledNext.Status);
        Assert.Null(cancelledNext.Battle);
        Assert.All(details.Matches.Where(x => x.Status == TournamentMatchStatus.Cancelled),
            x => Assert.Null(x.Battle));
    }

    [Fact]
    public async Task PollHandlers_AreGetOnlyAndDoNotMutateTournamentState()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("poll-organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 2, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 59)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var match = await fixture.Db.TournamentMatches.SingleAsync(x => x.TournamentId == tournament.Id);
        var tournamentVersion = (byte[])(await fixture.Db.Tournaments
            .Where(x => x.Id == tournament.Id).Select(x => x.Version).SingleAsync()).Clone();
        var matchVersion = (byte[])(await fixture.Db.TournamentMatches
            .Where(x => x.Id == match.Id).Select(x => x.Version).SingleAsync()).Clone();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, players[0].Id.ToString())], "Test"));
        PageContext Context() => new()
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var listPage = new BeybladeRecordSystem.Pages.Tournaments.IndexModel(fixture.Service)
        {
            PageContext = Context()
        };
        Assert.IsType<JsonResult>(await listPage.OnGetPollAsync());
        var detailsPage = new BeybladeRecordSystem.Pages.Tournaments.DetailsModel(
            fixture.Service, fixture.MatchService, new TournamentStandingsService(fixture.Db))
        {
            PageContext = Context()
        };
        Assert.IsType<JsonResult>(await detailsPage.OnGetPollAsync(tournament.Id));
        var matchPage = new BeybladeRecordSystem.Pages.Tournaments.MatchModel(fixture.MatchService)
        {
            PageContext = Context()
        };
        Assert.IsType<JsonResult>(await matchPage.OnGetPollAsync(match.Id));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), x =>
            x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(tournamentVersion, await fixture.Db.Tournaments
            .Where(x => x.Id == tournament.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(matchVersion, await fixture.Db.TournamentMatches
            .Where(x => x.Id == match.Id).Select(x => x.Version).SingleAsync());
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
    }

    [Fact]
    public async Task TeamMatch_ForfeitByAnyAcceptedMember_LosesWholeEntryAndAdvancesTournament()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (tournament, organizer, match) = await fixture.CreateStartedTeamMatchAsync(TournamentRuleSet.DuoFourBladeSixPoints);
        var participantIds = match.Participants.Select(x => x.UserId).ToList();
        foreach (var participantId in participantIds)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, participantId, true)).Succeeded);

        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var participantId in participantIds)
        {
            bladesByUser[participantId] = (await fixture.AddBladesAsync(participantId, $"forfeit-{participantId}")).Take(2).ToList();
            Assert.True((await fixture.MatchService.SubmitLineupAsync(match.Id, participantId, bladesByUser[participantId])).Succeeded);
        }

        fixture.Db.ChangeTracker.Clear();
        var workspace = await fixture.MatchService.GetWorkspaceAsync(match.Id, participantIds[0]);
        var sideA = workspace!.Match.Participants.Where(x => x.TournamentEntryId == workspace.Match.SideAEntryId).OrderBy(x => x.UserId).ToList();
        var sideB = workspace.Match.Participants.Where(x => x.TournamentEntryId == workspace.Match.SideBEntryId).OrderBy(x => x.UserId).ToList();
        Assert.True((await fixture.MatchService.SubmitTeamOrderAsync(
            match.Id, sideA.Single(x => x.IsMatchRepresentative).UserId, sideA.Select(x => x.UserId).ToList())).Succeeded);
        Assert.True((await fixture.MatchService.SubmitTeamOrderAsync(
            match.Id, sideB.Single(x => x.IsMatchRepresentative).UserId, sideB.Select(x => x.UserId).ToList())).Succeeded);
        foreach (var participantId in participantIds)
            Assert.True((await fixture.MatchService.ConfirmLineupAsync(match.Id, participantId)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.MatchService.AssignSidesAndStartAsync(match.Id, organizer.Id, BattleSide.B);
        Assert.True(started.Succeeded);
        var battleService = new BattleService(fixture.Db);
        var battle = (await battleService.GetBattleAsync(started.Value, organizer.Id)).Value!;
        var firstRound = battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, firstRound.Id, organizer.Id, firstRound.PlayerAId!.Value, ResultType.SpinFinish)).Succeeded);
        var currentRound = (await battleService.CompleteRoundAsync(battle.Id, firstRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, currentRound.Id, organizer.Id, currentRound.PlayerBId!.Value, ResultType.SpinFinish)).Succeeded);
        var outsider = await fixture.AddUserAsync("forfeit-outsider");
        Assert.False((await fixture.MatchService.ForfeitAsync(match.Id, outsider.Id, null)).Succeeded);
        var forfeitingMember = sideB.Single(x => !x.IsMatchRepresentative);
        Assert.True((await fixture.MatchService.ForfeitAsync(match.Id, forfeitingMember.UserId, "隊員受傷")).Succeeded);
        Assert.False((await fixture.MatchService.ForfeitAsync(match.Id, forfeitingMember.UserId, null)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.TournamentMatches
            .Include(x => x.Tournament)
            .Include(x => x.Battle).ThenInclude(x => x!.Rounds).ThenInclude(x => x.Events)
            .SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentMatchStatus.Forfeited, saved.Status);
        Assert.Equal(saved.SideAEntryId, saved.WinnerEntryId);
        Assert.Equal(saved.SideBEntryId, saved.LoserEntryId);
        Assert.Equal("隊員受傷", saved.ResolutionReason);
        Assert.Equal(TournamentStatus.Completed, saved.Tournament.Status);
        Assert.Equal(BattleStatus.Forfeited, saved.Battle!.Status);
        Assert.Equal(BattleSide.B, saved.Battle.WinningSide);
        Assert.Null(saved.Battle.WinningPlayerId);
        Assert.Equal(1, saved.Battle.SideAScore);
        Assert.Equal(0, saved.Battle.SideBScore);
        Assert.All(saved.Battle.Rounds.Single(x => x.Id == firstRound.Id).Events, x => Assert.True(x.IsEffective));
        Assert.All(saved.Battle.Rounds.Single(x => x.Id == currentRound.Id).Events, x => Assert.False(x.IsEffective));
    }

    [Fact]
    public async Task VoidCompletedBattle_PreservesAuditExcludesStatisticsAndCreatesFreshBattle()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var (tournament, organizer, players, match, battle) = await fixture.CreateStartedIndividualBattleAsync("void");
        var battleService = new BattleService(fixture.Db);
        var firstRound = battle.Rounds.Single();
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, firstRound.Id, organizer.Id, battle.PlayerAId!.Value, ResultType.Extreme)).Succeeded);
        var secondRound = (await battleService.CompleteRoundAsync(
            battle.Id, firstRound.Id, organizer.Id)).Value!;
        Assert.True((await battleService.RecordBattleResultAsync(
            battle.Id, secondRound.Id, organizer.Id, battle.PlayerAId.Value, ResultType.SpinFinish)).Succeeded);
        Assert.True((await battleService.FinishBattleAsync(battle.Id, organizer.Id)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(TournamentStatus.Completed, (await fixture.Db.Tournaments.FindAsync(tournament.Id))!.Status);
        var before = await new StatisticsService(fixture.Db).GetBeybladeStatisticsAsync(battle.PlayerAId.Value, null);
        Assert.Equal(4, before.Sum(x => x.Score));

        Assert.False((await fixture.MatchService.VoidAndReopenAsync(match.Id, players[0].Id, "無權限", true)).Succeeded);
        Assert.False((await fixture.MatchService.VoidAndReopenAsync(match.Id, organizer.Id, " ", true)).Succeeded);
        Assert.True((await fixture.MatchService.VoidAndReopenAsync(match.Id, organizer.Id, "裁判誤記勝方", true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var reopened = await fixture.Db.TournamentMatches.AsSplitQuery()
            .Include(x => x.Tournament)
            .Include(x => x.Participants)
            .Include(x => x.Battle)
            .Include(x => x.VoidedBattles).ThenInclude(x => x.Rounds).ThenInclude(x => x.Events)
            .SingleAsync(x => x.Id == match.Id);
        Assert.Equal(TournamentStatus.InProgress, reopened.Tournament.Status);
        Assert.Null(reopened.Tournament.CompletedAtUtc);
        Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, reopened.Status);
        Assert.Null(reopened.WinnerEntryId);
        Assert.Null(reopened.LoserEntryId);
        Assert.Null(reopened.Battle);
        Assert.All(reopened.Participants, x =>
        {
            Assert.Equal(TournamentParticipationStatus.Pending, x.Status);
            Assert.False(x.LineupConfirmed);
            Assert.Null(x.RespondedAtUtc);
        });
        var voided = Assert.Single(reopened.VoidedBattles);
        Assert.Equal(battle.Id, voided.Id);
        Assert.Equal(BattleStatus.Voided, voided.Status);
        Assert.Null(voided.TournamentMatchId);
        Assert.Equal(match.Id, voided.VoidedTournamentMatchId);
        Assert.Equal(organizer.Id, voided.VoidedByUserId);
        Assert.Equal("裁判誤記勝方", voided.VoidReason);
        Assert.Contains("\"BattleStatus\":4", voided.VoidSnapshot);
        Assert.Contains("\"IsEffective\":true", voided.VoidSnapshot);
        Assert.NotNull(voided.VoidedAtUtc);
        Assert.Equal(4, voided.SideAScore);
        Assert.All(voided.Rounds.SelectMany(x => x.Events), x => Assert.False(x.IsEffective));
        var after = await new StatisticsService(fixture.Db).GetBeybladeStatisticsAsync(battle.PlayerAId.Value, null);
        Assert.Equal(0, after.Sum(x => x.Score));
        Assert.Equal(0, after.Sum(x => x.Wins));
        Assert.True((await battleService.GetBattleAsync(battle.Id, players[0].Id)).Succeeded);

        var pendingUserIds = reopened.Participants.Select(x => x.UserId).ToList();
        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, pendingUserIds[0], true)).Succeeded);
        Assert.Null((await fixture.MatchService.GetWorkspaceAsync(match.Id, pendingUserIds[0]))!.Match.Battle);
        Assert.True((await fixture.MatchService.RespondParticipationAsync(match.Id, pendingUserIds[1], true)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var replacement = await fixture.Db.Battles.SingleAsync(x => x.TournamentMatchId == match.Id);
        Assert.NotEqual(battle.Id, replacement.Id);
        Assert.Equal(BattleStatus.Draft, replacement.Status);
        Assert.Empty(await fixture.Db.BattleLineupSelections.Where(x => x.BattleId == replacement.Id).ToListAsync());
        Assert.Equal(2, await fixture.Db.Battles.CountAsync(x => x.TournamentMatchId == match.Id || x.VoidedTournamentMatchId == match.Id));
    }

    [Fact]
    public async Task VoidUpstreamBattle_BlocksStartedDownstreamAndRequiresConfirmationForPreparedLineup()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("downstream-void-organizer");
        var (created, _) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 4, TournamentFormat.SingleElimination);
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(created.Id, organizer.Id, 43)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var tournament = await fixture.Db.Tournaments.AsSplitQuery()
            .Include(x => x.Entries)
            .Include(x => x.Matches)
            .SingleAsync(x => x.Id == created.Id);
        var upstream = tournament.Matches.First(x => !x.IsBye && x.WinnerToMatchId is not null);
        var downstream = tournament.Matches.Single(x => x.Id == upstream.WinnerToMatchId);
        var now = DateTime.UtcNow;
        var upstreamA = tournament.Entries.Single(x => x.Id == upstream.SideAEntryId);
        var upstreamB = tournament.Entries.Single(x => x.Id == upstream.SideBEntryId);
        var otherFinalist = tournament.Entries.First(x => x.Id != upstreamA.Id && x.Id != upstreamB.Id);
        var affectedSideA = downstream.SideASourceReferenceId == upstream.Id;
        if (affectedSideA)
        {
            downstream.SideAEntryId = upstreamA.Id;
            downstream.SideBEntryId = otherFinalist.Id;
        }
        else
        {
            downstream.SideAEntryId = otherFinalist.Id;
            downstream.SideBEntryId = upstreamA.Id;
        }
        upstream.Status = TournamentMatchStatus.Completed;
        upstream.WinnerEntryId = upstreamA.Id;
        upstream.LoserEntryId = upstreamB.Id;
        upstream.CompletedAtUtc = now;
        upstream.Participants.Add(CreateTestParticipant(upstream, upstreamA, now));
        upstream.Participants.Add(CreateTestParticipant(upstream, upstreamB, now));
        upstream.Battle = CreateTestTournamentBattle(
            upstream, upstreamA, upstreamB, organizer.Id, tournament.ScoreToWin, BattleStatus.Completed, now);
        downstream.Status = TournamentMatchStatus.InProgress;
        var downstreamA = tournament.Entries.Single(x => x.Id == downstream.SideAEntryId);
        var downstreamB = tournament.Entries.Single(x => x.Id == downstream.SideBEntryId);
        downstream.Participants.Add(CreateTestParticipant(downstream, downstreamA, now));
        downstream.Participants.Add(CreateTestParticipant(downstream, downstreamB, now));
        downstream.Battle = CreateTestTournamentBattle(
            downstream, downstreamA, downstreamB, organizer.Id, tournament.ScoreToWin, BattleStatus.InProgress, now);
        tournament.Status = TournamentStatus.InProgress;
        tournament.StartedAtUtc = now;
        await fixture.Db.SaveChangesAsync();
        var upstreamBattleId = upstream.Battle.Id;
        var downstreamBattleId = downstream.Battle.Id;

        Assert.False((await fixture.MatchService.VoidAndReopenAsync(
            upstream.Id, organizer.Id, "上游結果錯誤", true)).Succeeded);
        Assert.Equal(BattleStatus.Completed, (await fixture.Db.Battles.FindAsync(upstreamBattleId))!.Status);

        downstream.Status = TournamentMatchStatus.LineupSelection;
        downstream.Battle.Status = BattleStatus.Draft;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await fixture.MatchService.VoidAndReopenAsync(
            upstream.Id, organizer.Id, "上游結果錯誤", false)).Succeeded);
        Assert.True((await fixture.MatchService.VoidAndReopenAsync(
            upstream.Id, organizer.Id, "上游結果錯誤", true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        upstream = await fixture.Db.TournamentMatches.AsSplitQuery()
            .Include(x => x.Participants)
            .Include(x => x.Battle)
            .Include(x => x.VoidedBattles)
            .SingleAsync(x => x.Id == upstream.Id);
        downstream = await fixture.Db.TournamentMatches.AsSplitQuery()
            .Include(x => x.Participants)
            .Include(x => x.Battle)
            .Include(x => x.VoidedBattles)
            .SingleAsync(x => x.Id == downstream.Id);
        Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, upstream.Status);
        Assert.Null(upstream.Battle);
        Assert.Equal(upstreamBattleId, Assert.Single(upstream.VoidedBattles).Id);
        Assert.All(upstream.Participants, x => Assert.Equal(TournamentParticipationStatus.Pending, x.Status));
        Assert.Equal(TournamentMatchStatus.WaitingForParticipants, downstream.Status);
        Assert.Null(downstream.Battle);
        Assert.Empty(downstream.Participants);
        Assert.Equal(downstreamBattleId, Assert.Single(downstream.VoidedBattles).Id);
        if (affectedSideA)
        {
            Assert.Null(downstream.SideAEntryId);
            Assert.Equal(otherFinalist.Id, downstream.SideBEntryId);
        }
        else
        {
            Assert.Equal(otherFinalist.Id, downstream.SideAEntryId);
            Assert.Null(downstream.SideBEntryId);
        }
    }

    [Fact]
    public async Task RevisionChangingCompletedWinner_RebuildsPreparedDownstreamAndBlocksStartedDownstream()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var organizer = await fixture.AddUserAsync("revision-downstream-organizer");
        var (tournament, players) = await fixture.CreateClosedIndividualTournamentAsync(
            organizer.Id, 4, TournamentFormat.SingleElimination);
        var bladesByUser = new Dictionary<int, List<int>>();
        foreach (var player in players)
            bladesByUser[player.Id] = await fixture.AddBladesAsync(player.Id, $"revision-{player.Id}");
        Assert.True((await fixture.Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 47)).Succeeded);
        Assert.True((await fixture.Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
        var battleService = new BattleService(fixture.Db);

        async Task<(int MatchId, int BattleId, int RevisionRoundId, int SideBPlayerId, int OldWinnerEntryId, int NewWinnerEntryId)>
            CompleteActiveMatchAsync()
        {
            fixture.Db.ChangeTracker.Clear();
            var active = await fixture.Db.TournamentMatches.Include(x => x.Participants)
                .SingleAsync(x => x.TournamentId == tournament.Id && x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            foreach (var participant in active.Participants)
                Assert.True((await fixture.MatchService.RespondParticipationAsync(active.Id, participant.UserId, true)).Succeeded);
            foreach (var participant in active.Participants)
                Assert.True((await fixture.MatchService.SubmitLineupAsync(
                    active.Id, participant.UserId, bladesByUser[participant.UserId])).Succeeded);
            foreach (var participant in active.Participants)
                Assert.True((await fixture.MatchService.ConfirmLineupAsync(active.Id, participant.UserId)).Succeeded);
            fixture.Db.ChangeTracker.Clear();
            var started = await fixture.MatchService.AssignSidesAndStartAsync(active.Id, organizer.Id, BattleSide.B);
            Assert.True(started.Succeeded);
            var battle = (await battleService.GetBattleAsync(started.Value, organizer.Id)).Value!;
            var firstRound = battle.Rounds.Single();
            Assert.True((await battleService.RecordBattleResultAsync(
                battle.Id, firstRound.Id, organizer.Id, battle.PlayerBId!.Value, ResultType.SpinFinish)).Succeeded);
            var secondRound = (await battleService.CompleteRoundAsync(
                battle.Id, firstRound.Id, organizer.Id)).Value!;
            Assert.True((await battleService.RecordBattleResultAsync(
                battle.Id, secondRound.Id, organizer.Id, battle.PlayerAId!.Value, ResultType.Extreme)).Succeeded);
            var thirdRound = (await battleService.CompleteRoundAsync(
                battle.Id, secondRound.Id, organizer.Id)).Value!;
            Assert.True((await battleService.RecordBattleResultAsync(
                battle.Id, thirdRound.Id, organizer.Id, battle.PlayerAId.Value, ResultType.SpinFinish)).Succeeded);
            Assert.True((await battleService.FinishBattleAsync(battle.Id, organizer.Id)).Succeeded);
            fixture.Db.ChangeTracker.Clear();
            var completed = await fixture.Db.TournamentMatches.SingleAsync(x => x.Id == active.Id);
            return (
                active.Id,
                battle.Id,
                secondRound.Id,
                battle.PlayerBId.Value,
                completed.SideAEntryId!.Value,
                completed.SideBEntryId!.Value);
        }

        var revisedSource = await CompleteActiveMatchAsync();
        _ = await CompleteActiveMatchAsync();
        fixture.Db.ChangeTracker.Clear();
        var final = await fixture.Db.TournamentMatches.Include(x => x.Participants).Include(x => x.Battle)
            .SingleAsync(x => x.TournamentId == tournament.Id && x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
        foreach (var participant in final.Participants)
            Assert.True((await fixture.MatchService.RespondParticipationAsync(final.Id, participant.UserId, true)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        final = await fixture.Db.TournamentMatches.Include(x => x.Participants).Include(x => x.Battle)
            .SingleAsync(x => x.Id == final.Id);
        Assert.Equal(TournamentMatchStatus.LineupSelection, final.Status);
        var preparedBattleId = final.Battle!.Id;
        var affectedSideA = final.SideASourceReferenceId == revisedSource.MatchId;
        Assert.Equal(revisedSource.OldWinnerEntryId, affectedSideA ? final.SideAEntryId : final.SideBEntryId);

        final.Status = TournamentMatchStatus.InProgress;
        final.Battle.Status = BattleStatus.InProgress;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await battleService.ReviseRoundAsync(
            revisedSource.BattleId,
            revisedSource.RevisionRoundId,
            organizer.Id,
            revisedSource.SideBPlayerId,
            ResultType.Extreme,
            "第二局勝方修正",
            true)).Succeeded);

        final.Status = TournamentMatchStatus.LineupSelection;
        final.Battle.Status = BattleStatus.Draft;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await battleService.ReviseRoundAsync(
            revisedSource.BattleId,
            revisedSource.RevisionRoundId,
            organizer.Id,
            revisedSource.SideBPlayerId,
            ResultType.Extreme,
            "第二局勝方修正",
            false)).Succeeded);
        Assert.True((await battleService.ReviseRoundAsync(
            revisedSource.BattleId,
            revisedSource.RevisionRoundId,
            organizer.Id,
            revisedSource.SideBPlayerId,
            ResultType.Extreme,
            "第二局勝方修正",
            true)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var revisedMatch = await fixture.Db.TournamentMatches.Include(x => x.Battle)
            .SingleAsync(x => x.Id == revisedSource.MatchId);
        Assert.Equal(TournamentMatchStatus.Completed, revisedMatch.Status);
        Assert.Equal(revisedSource.NewWinnerEntryId, revisedMatch.WinnerEntryId);
        Assert.Equal(revisedSource.OldWinnerEntryId, revisedMatch.LoserEntryId);
        Assert.Equal(BattleStatus.Completed, revisedMatch.Battle!.Status);
        Assert.Equal(0, revisedMatch.Battle.SideAScore);
        Assert.Equal(4, revisedMatch.Battle.SideBScore);
        Assert.Equal(revisedSource.SideBPlayerId, revisedMatch.Battle.WinningPlayerId);
        var invalidatedLaterEvent = await fixture.Db.BattleRoundEvents
            .Where(x => x.BattleRound.BattleId == revisedSource.BattleId && x.BattleRound.RoundNo == 3)
            .SingleAsync(x => x.EventType == BattleRoundEventType.BattleResult);
        Assert.False(invalidatedLaterEvent.IsEffective);
        Assert.Equal(BattleRoundEventInvalidationReason.SupersededByEarlierRoundRevision, invalidatedLaterEvent.InvalidationReason);

        final = await fixture.Db.TournamentMatches.AsSplitQuery()
            .Include(x => x.Participants)
            .Include(x => x.Battle)
            .Include(x => x.VoidedBattles)
            .SingleAsync(x => x.Id == final.Id);
        Assert.Equal(TournamentMatchStatus.AwaitingParticipationConfirmation, final.Status);
        Assert.Null(final.Battle);
        Assert.Equal(preparedBattleId, Assert.Single(final.VoidedBattles).Id);
        Assert.Equal(revisedSource.NewWinnerEntryId, affectedSideA ? final.SideAEntryId : final.SideBEntryId);
        Assert.DoesNotContain(final.Participants, x => x.TournamentEntryId == revisedSource.OldWinnerEntryId);
        Assert.Contains(final.Participants, x => x.TournamentEntryId == revisedSource.NewWinnerEntryId);
        Assert.All(final.Participants, x => Assert.Equal(TournamentParticipationStatus.Pending, x.Status));
        Assert.Equal(TournamentStatus.InProgress, (await fixture.Db.Tournaments.FindAsync(tournament.Id))!.Status);
        var audit = await fixture.Db.BattleRoundRevisions.SingleAsync(x => x.BattleRoundId == revisedSource.RevisionRoundId);
        Assert.Equal("第二局勝方修正", audit.Reason);
        Assert.Contains("\"WinnerEntryId\"", audit.PreviousBattleSnapshot);
        Assert.Contains("\"WinnerEntryId\"", audit.NewBattleSnapshot);
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

    private static TournamentMatchParticipant CreateTestParticipant(
        TournamentMatch match,
        TournamentEntry entry,
        DateTime now) => new()
    {
        TournamentMatch = match,
        TournamentEntryId = entry.Id,
        UserId = entry.IndividualUserId!.Value,
        Status = TournamentParticipationStatus.Accepted,
        NotifiedAtUtc = now,
        RespondedAtUtc = now,
        Version = [1]
    };

    private static Battle CreateTestTournamentBattle(
        TournamentMatch match,
        TournamentEntry sideA,
        TournamentEntry sideB,
        int organizerUserId,
        int scoreToWin,
        BattleStatus status,
        DateTime now) => new()
    {
        TournamentMatch = match,
        SourceType = BattleSourceType.TournamentIndividual,
        ScoreToWin = scoreToWin,
        PlayerAId = sideA.IndividualUserId,
        PlayerBId = sideB.IndividualUserId,
        CreatedByUserId = organizerUserId,
        Status = status,
        SideADesignation = BattleSide.B,
        CreatedAtUtc = now,
        StartedAtUtc = status == BattleStatus.Draft ? null : now,
        CompletedAtUtc = status == BattleStatus.Completed ? now : null,
        Version = [1]
    };

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

        public async Task<(Tournament Tournament, User Organizer, List<User> Players, TournamentMatch Match, Battle Battle)>
            CreateStartedIndividualBattleAsync(string prefix)
        {
            var organizer = await AddUserAsync($"{prefix}-organizer");
            var (tournament, players) = await CreateClosedIndividualTournamentAsync(
                organizer.Id, 2, TournamentFormat.SingleElimination);
            var bladesByUser = new Dictionary<int, List<int>>();
            foreach (var player in players)
                bladesByUser[player.Id] = await AddBladesAsync(player.Id, $"{prefix}-{player.Id}");
            Assert.True((await Service.GenerateScheduleDraftAsync(tournament.Id, organizer.Id, 37)).Succeeded);
            Assert.True((await Service.StartTournamentAsync(tournament.Id, organizer.Id)).Succeeded);
            Db.ChangeTracker.Clear();
            var match = await Db.TournamentMatches.Include(x => x.Participants)
                .SingleAsync(x => x.TournamentId == tournament.Id && x.Status == TournamentMatchStatus.AwaitingParticipationConfirmation);
            foreach (var participant in match.Participants)
                Assert.True((await MatchService.RespondParticipationAsync(match.Id, participant.UserId, true)).Succeeded);
            foreach (var participant in match.Participants)
                Assert.True((await MatchService.SubmitLineupAsync(match.Id, participant.UserId, bladesByUser[participant.UserId])).Succeeded);
            foreach (var participant in match.Participants)
                Assert.True((await MatchService.ConfirmLineupAsync(match.Id, participant.UserId)).Succeeded);
            Db.ChangeTracker.Clear();
            var started = await MatchService.AssignSidesAndStartAsync(match.Id, organizer.Id, BattleSide.B);
            Assert.True(started.Succeeded);
            var battle = (await new BattleService(Db).GetBattleAsync(started.Value, organizer.Id)).Value!;
            return (tournament, organizer, players, match, battle);
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
