using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using BeybladeRecordSystem.Realtime;
using BeybladeRecordSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Tests;

public class QuickBattleFlowTests
{
    [Fact]
    public async Task Invitation_RejectsSelfAndPublishesCounterpartyStateChanges()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();

        var selfInvitation = await fixture.Flow.SendInvitationAsync(fixture.PlayerA.Id, fixture.PlayerA.Id);

        Assert.False(selfInvitation.Succeeded);
        Assert.Empty(await fixture.Db.QuickBattleInvitations.ToListAsync());
        Assert.Empty(fixture.Publisher.Events);

        var invitation = (await fixture.Flow.SendInvitationAsync(fixture.PlayerA.Id, fixture.PlayerB.Id)).Value!;
        var pendingEvent = Assert.Single(fixture.Publisher.Events);
        Assert.Equal(fixture.PlayerB.Id, pendingEvent.UserId);
        Assert.Equal("quick-invitation-state", pendingEvent.EventType);

        fixture.Publisher.Events.Clear();
        Assert.True((await fixture.Flow.WithdrawInvitationAsync(invitation.Id, fixture.PlayerA.Id)).Succeeded);
        var withdrawnEvent = Assert.Single(fixture.Publisher.Events);
        Assert.Equal(fixture.PlayerB.Id, withdrawnEvent.UserId);
        Assert.Equal("quick-invitation-state", withdrawnEvent.EventType);
    }

    [Theory]
    [InlineData(BattleStatus.LineupSelection, "/Battles/Setup/42")]
    [InlineData(BattleStatus.InProgress, "/Battles/Battle/42")]
    [InlineData(BattleStatus.ReorderSelection, "/Battles/Reorder/42")]
    [InlineData(BattleStatus.Completed, "/Battles/Details/42")]
    [InlineData(BattleStatus.Forfeited, "/Battles/Details/42")]
    public void BattleTargetUrl_UsesCurrentPersistedStatus(BattleStatus status, string expected)
    {
        Assert.Equal(expected, QuickBattleFlowService.GetBattleTargetUrl(42, status));
    }

    [Fact]
    public async Task GetActiveBattles_ReturnsEveryResumableQuickStateWithDestination_AndPreservesOwnership()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var outsider = new User
        {
            Account = "quick-outsider",
            PasswordHash = "x",
            DisplayName = "Quick Outsider",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.Users.Add(outsider);
        await fixture.Db.SaveChangesAsync();

        var tournament = new Tournament
        {
            Name = "Active query isolation",
            Mode = TournamentMode.Individual,
            Format = TournamentFormat.SingleElimination,
            RegistrationMode = TournamentRegistrationMode.Individual,
            RuleSet = TournamentRuleSet.IndividualThreeBladeFourPoints,
            BeybladesPerPlayer = 3,
            ScoreToWin = 4,
            TargetEntryCount = 4,
            OrganizerUserId = fixture.PlayerA.Id,
            RulesSnapshot = "test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid().ToByteArray()
        };
        fixture.Db.Tournaments.Add(tournament);
        await fixture.Db.SaveChangesAsync();
        var tournamentMatch = new TournamentMatch
        {
            TournamentId = tournament.Id,
            Bracket = TournamentBracket.Winners,
            RoundNumber = 1,
            MatchNumber = 1,
            SequenceNumber = 1,
            SideASourceKind = TournamentParticipantSourceKind.Entry,
            SideASourceReferenceId = 1,
            SideBSourceKind = TournamentParticipantSourceKind.Entry,
            SideBSourceReferenceId = 2,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid().ToByteArray()
        };
        fixture.Db.TournamentMatches.Add(tournamentMatch);
        await fixture.Db.SaveChangesAsync();

        var expected = new Dictionary<BattleStatus, QuickBattleResumeTarget>
        {
            [BattleStatus.LineupSelection] = QuickBattleResumeTarget.Setup,
            [BattleStatus.LineupReview] = QuickBattleResumeTarget.Setup,
            [BattleStatus.LineupLocked] = QuickBattleResumeTarget.Setup,
            [BattleStatus.SideSelection] = QuickBattleResumeTarget.Setup,
            [BattleStatus.InProgress] = QuickBattleResumeTarget.Battle,
            [BattleStatus.ReorderSelection] = QuickBattleResumeTarget.Reorder,
            [BattleStatus.VictoryPendingCompletion] = QuickBattleResumeTarget.Battle
        };
        var createdAt = DateTime.UtcNow;
        fixture.Db.Battles.AddRange(expected.Keys.Select((status, index) => new Battle
        {
            SourceType = BattleSourceType.Quick,
            PlayerAId = fixture.PlayerA.Id,
            PlayerBId = fixture.PlayerB.Id,
            CreatedByUserId = fixture.PlayerA.Id,
            Status = status,
            CreatedAtUtc = createdAt.AddMinutes(index),
            Version = Guid.NewGuid().ToByteArray()
        }));
        fixture.Db.Battles.AddRange(
            new Battle
            {
                SourceType = BattleSourceType.Quick,
                PlayerAId = fixture.PlayerA.Id,
                PlayerBId = fixture.PlayerB.Id,
                CreatedByUserId = fixture.PlayerA.Id,
                Status = BattleStatus.Completed,
                CreatedAtUtc = createdAt.AddHours(1),
                Version = Guid.NewGuid().ToByteArray()
            },
            new Battle
            {
                SourceType = BattleSourceType.Quick,
                PlayerAId = fixture.PlayerA.Id,
                PlayerBId = fixture.PlayerB.Id,
                CreatedByUserId = fixture.PlayerA.Id,
                Status = BattleStatus.Forfeited,
                CreatedAtUtc = createdAt.AddHours(2),
                Version = Guid.NewGuid().ToByteArray()
            },
            new Battle
            {
                SourceType = BattleSourceType.Quick,
                PlayerAId = fixture.PlayerA.Id,
                PlayerBId = fixture.PlayerB.Id,
                CreatedByUserId = fixture.PlayerA.Id,
                Status = BattleStatus.Cancelled,
                CreatedAtUtc = createdAt.AddHours(3),
                Version = Guid.NewGuid().ToByteArray()
            },
            new Battle
            {
                SourceType = BattleSourceType.TournamentIndividual,
                ScoreToWin = 4,
                TournamentMatchId = tournamentMatch.Id,
                PlayerAId = fixture.PlayerA.Id,
                PlayerBId = fixture.PlayerB.Id,
                CreatedByUserId = fixture.PlayerA.Id,
                Status = BattleStatus.InProgress,
                CreatedAtUtc = createdAt.AddHours(4),
                Version = Guid.NewGuid().ToByteArray()
            });
        await fixture.Db.SaveChangesAsync();

        var active = await fixture.Flow.GetActiveBattlesAsync(fixture.PlayerA.Id);

        Assert.Equal(expected.Count, active.Count);
        Assert.All(active, item =>
        {
            Assert.Equal(expected[item.Battle.Status], item.Target);
            Assert.Equal(fixture.PlayerA.Id, item.Battle.PlayerAId);
            Assert.Equal("Quick B", item.Battle.PlayerB.DisplayName);
        });
        Assert.Empty(await fixture.Flow.GetActiveBattlesAsync(outsider.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuickBattleFlowService.GetResumeTarget(BattleStatus.Completed));
    }

    [Fact]
    public async Task Invitation_DeclineAndWithdrawHardDeleteWithoutCreatingBattle()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();

        var first = await fixture.Flow.SendInvitationAsync(fixture.PlayerA.Id, fixture.PlayerB.Id);
        Assert.True(first.Succeeded);
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
        Assert.False((await fixture.Flow.DeclineInvitationAsync(first.Value!.Id, fixture.PlayerA.Id)).Succeeded);
        Assert.True((await fixture.Flow.DeclineInvitationAsync(first.Value.Id, fixture.PlayerB.Id)).Succeeded);
        Assert.Empty(await fixture.Db.QuickBattleInvitations.ToListAsync());
        Assert.Empty(await fixture.Db.Battles.ToListAsync());

        var second = (await fixture.Flow.SendInvitationAsync(fixture.PlayerA.Id, fixture.PlayerB.Id)).Value!;
        Assert.False((await fixture.Flow.WithdrawInvitationAsync(second.Id, fixture.PlayerB.Id)).Succeeded);
        Assert.True((await fixture.Flow.WithdrawInvitationAsync(second.Id, fixture.PlayerA.Id)).Succeeded);
        Assert.Empty(await fixture.Db.QuickBattleInvitations.ToListAsync());
        Assert.Empty(await fixture.Db.Battles.ToListAsync());
    }

    [Fact]
    public async Task AcceptInvitation_CreatesPersistentBattleAndDeletesInvitation()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var invitation = (await fixture.Flow.SendInvitationAsync(fixture.PlayerA.Id, fixture.PlayerB.Id)).Value!;

        var accepted = await fixture.Flow.AcceptInvitationAsync(invitation.Id, fixture.PlayerB.Id);

        Assert.True(accepted.Succeeded);
        Assert.Empty(await fixture.Db.QuickBattleInvitations.ToListAsync());
        var battle = await fixture.Db.Battles.SingleAsync();
        Assert.Equal(accepted.Value, battle.Id);
        Assert.Equal(BattleStatus.LineupSelection, battle.Status);
        Assert.Equal(1, battle.LineupSequenceNo);
        Assert.Null(battle.SideADesignation);
        Assert.Equal(fixture.PlayerA.Id, battle.CreatedByUserId);
    }

    [Fact]
    public async Task Lineups_AreOwnedAndPrivateUntilBothPlayersSubmit()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateAcceptedBattleAsync();

        Assert.False((await fixture.Flow.SubmitLineupAsync(
            battleId, fixture.PlayerA.Id, fixture.PlayerBBladeIds)).Succeeded);
        Assert.True((await fixture.Flow.SubmitLineupAsync(
            battleId, fixture.PlayerA.Id, fixture.PlayerABladeIds)).Succeeded);

        var aWaiting = await fixture.Flow.GetWorkspaceAsync(battleId, fixture.PlayerA.Id);
        var bWaiting = await fixture.Flow.GetWorkspaceAsync(battleId, fixture.PlayerB.Id);
        Assert.True(aWaiting!.CurrentUserSubmitted);
        Assert.Equal(3, aWaiting.CurrentPrivateSelections.Count);
        Assert.Empty(aWaiting.VisibleSelections);
        Assert.False(bWaiting!.CurrentUserSubmitted);
        Assert.Empty(bWaiting.CurrentPrivateSelections);
        Assert.Empty(bWaiting.VisibleSelections);

        Assert.True((await fixture.Flow.SubmitLineupAsync(
            battleId, fixture.PlayerB.Id, fixture.PlayerBBladeIds)).Succeeded);
        var review = await fixture.Flow.GetWorkspaceAsync(battleId, fixture.PlayerA.Id);
        Assert.Equal(BattleStatus.LineupReview, review!.Battle.Status);
        Assert.Equal(6, review.VisibleSelections.Count);
        Assert.Empty(review.CurrentPrivateSelections);
    }

    [Fact]
    public async Task EditRequest_RejectionPreservesVersionAndAcceptanceResetsBothPlayers()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateBattleInReviewAsync();

        Assert.True((await fixture.Flow.RequestLineupEditAsync(battleId, fixture.PlayerA.Id)).Succeeded);
        Assert.False((await fixture.Flow.RespondLineupEditAsync(battleId, fixture.PlayerA.Id, true)).Succeeded);
        Assert.True((await fixture.Flow.RespondLineupEditAsync(battleId, fixture.PlayerB.Id, false)).Succeeded);
        Assert.False((await fixture.Flow.RequestLineupEditAsync(battleId, fixture.PlayerA.Id)).Succeeded);

        var afterReject = await fixture.Flow.GetWorkspaceAsync(battleId, fixture.PlayerA.Id);
        Assert.Equal(1, afterReject!.Battle.LineupSequenceNo);
        Assert.Equal(BattleStatus.LineupReview, afterReject.Battle.Status);
        Assert.Equal(6, afterReject.VisibleSelections.Count);
        Assert.True(afterReject.CurrentUserEditRequestUsed);

        Assert.True((await fixture.Flow.RequestLineupEditAsync(battleId, fixture.PlayerB.Id)).Succeeded);
        Assert.True((await fixture.Flow.RespondLineupEditAsync(battleId, fixture.PlayerA.Id, true)).Succeeded);
        var afterAccept = await fixture.Flow.GetWorkspaceAsync(battleId, fixture.PlayerA.Id);
        Assert.Equal(2, afterAccept!.Battle.LineupSequenceNo);
        Assert.Equal(BattleStatus.LineupSelection, afterAccept.Battle.Status);
        Assert.False(afterAccept.CurrentUserSubmitted);
        Assert.False(afterAccept.CurrentUserEditRequestUsed);
        Assert.Empty(afterAccept.VisibleSelections);
        Assert.Equal(6, await fixture.Db.BattleLineupSelections.CountAsync(x => x.BattleId == battleId && x.SequenceNo == 1));
        Assert.Empty(await fixture.Db.BattleLineupSelections.Where(x => x.BattleId == battleId && x.SequenceNo == 2).ToListAsync());
    }

    [Fact]
    public async Task BothConfirm_LocksMaterializedLineup_AndExplicitSideIsRequiredBeforeStart()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateBattleInReviewAsync();

        Assert.True((await fixture.Flow.ConfirmLineupAsync(battleId, fixture.PlayerA.Id)).Succeeded);
        Assert.False((await fixture.Battles.StartBattleAsync(battleId, fixture.PlayerA.Id)).Succeeded);
        Assert.True((await fixture.Flow.ConfirmLineupAsync(battleId, fixture.PlayerB.Id)).Succeeded);

        var locked = await fixture.Db.Battles.Include(x => x.Lineups).SingleAsync(x => x.Id == battleId);
        Assert.Equal(BattleStatus.LineupLocked, locked.Status);
        Assert.Equal(3, locked.Lineups.Count);
        Assert.All(locked.Lineups, x => Assert.True(x.IsCurrent));
        Assert.False((await fixture.Battles.StartBattleAsync(battleId, fixture.PlayerA.Id)).Succeeded);
        Assert.False((await fixture.Battles.AssignSidesAsync(battleId, fixture.PlayerB.Id, BattleSide.X)).Succeeded);
        Assert.True((await fixture.Battles.AssignSidesAsync(battleId, fixture.PlayerA.Id, BattleSide.X)).Succeeded);
        Assert.True((await fixture.Battles.StartBattleAsync(battleId, fixture.PlayerA.Id)).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var started = await fixture.Db.Battles.Include(x => x.Rounds).SingleAsync(x => x.Id == battleId);
        Assert.Equal(BattleStatus.InProgress, started.Status);
        Assert.Equal(BattleSide.X, started.SideADesignation);
        Assert.Single(started.Rounds);
    }

    [Fact]
    public async Task AssigningSides_KeepsBothPlayersInSetup_UntilFirstRoundStarts()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateBattleInReviewAsync();
        Assert.True((await fixture.Flow.ConfirmLineupAsync(battleId, fixture.PlayerA.Id)).Succeeded);
        Assert.True((await fixture.Flow.ConfirmLineupAsync(battleId, fixture.PlayerB.Id)).Succeeded);

        fixture.Publisher.Events.Clear();
        Assert.True((await fixture.Battles.AssignSidesAsync(battleId, fixture.PlayerA.Id, BattleSide.B)).Succeeded);

        var sideEvents = fixture.Publisher.Events.Where(x => x.EventType == "battle-state").ToList();
        Assert.Equal(2, sideEvents.Count);
        Assert.All(sideEvents, item => Assert.Equal(
            $"/Battles/Setup/{battleId}",
            item.Payload.GetType().GetProperty("targetUrl")!.GetValue(item.Payload)));

        fixture.Publisher.Events.Clear();
        Assert.True((await fixture.Battles.StartBattleAsync(battleId, fixture.PlayerA.Id)).Succeeded);

        var startEvents = fixture.Publisher.Events.Where(x => x.EventType == "battle-state").ToList();
        Assert.Equal(2, startEvents.Count);
        Assert.All(startEvents, item => Assert.Equal(
            $"/Battles/Battle/{battleId}",
            item.Payload.GetType().GetProperty("targetUrl")!.GetValue(item.Payload)));
    }

    [Fact]
    public async Task Reorder_IsPrivatePerPlayerAndAppliesOnlyAfterBothSubmit()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateStartedBattleAsync();
        var round = await fixture.Db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
        for (var index = 0; index < 3; index++)
        {
            Assert.True((await fixture.Battles.RecordBattleResultAsync(
                battleId, round.Id, fixture.PlayerA.Id, fixture.PlayerA.Id, ResultType.SpinFinish)).Succeeded);
            var completed = await fixture.Battles.CompleteRoundAsync(battleId, round.Id, fixture.PlayerA.Id);
            if (index < 2) round = completed.Value!;
        }

        fixture.Db.ChangeTracker.Clear();
        var waiting = await fixture.Db.Battles.SingleAsync(x => x.Id == battleId);
        Assert.Equal(BattleStatus.ReorderSelection, waiting.Status);
        Assert.Equal(3, waiting.SideAScore);
        Assert.True((await fixture.Flow.SubmitReorderAsync(
            battleId, fixture.PlayerA.Id, fixture.PlayerABladeIds.AsEnumerable().Reverse().ToList())).Succeeded);
        var playerAWorkspace = await fixture.Flow.GetReorderWorkspaceAsync(battleId, fixture.PlayerA.Id);
        var playerBWorkspace = await fixture.Flow.GetReorderWorkspaceAsync(battleId, fixture.PlayerB.Id);
        Assert.True(playerAWorkspace!.CurrentUserSubmitted);
        Assert.Equal(3, playerAWorkspace.CurrentPrivateSelections.Count);
        Assert.False(playerBWorkspace!.CurrentUserSubmitted);
        Assert.Empty(playerBWorkspace.CurrentPrivateSelections);

        Assert.True((await fixture.Flow.SubmitReorderAsync(
            battleId, fixture.PlayerB.Id, fixture.PlayerBBladeIds.AsEnumerable().Reverse().ToList())).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        var resumed = await fixture.Db.Battles.Include(x => x.Lineups).Include(x => x.Rounds).SingleAsync(x => x.Id == battleId);
        Assert.Equal(BattleStatus.InProgress, resumed.Status);
        Assert.Equal(3, resumed.SideAScore);
        Assert.Equal(2, resumed.LineupSequenceNo);
        var current = resumed.Lineups.Where(x => x.IsCurrent).OrderBy(x => x.PositionNo).ToList();
        Assert.Equal(fixture.PlayerABladeIds[2], current[0].PlayerABeybladeId);
        Assert.Equal(fixture.PlayerBBladeIds[2], current[0].PlayerBBeybladeId);
        Assert.Contains(resumed.Rounds, x => x.RoundNo == 4 && x.Status == BattleRoundStatus.InProgress);
    }

    [Fact]
    public async Task RevisionWhileWaitingForReorder_InvalidatesLaterRoundsAndRestartsAtSecondPosition()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        var battleId = await fixture.CreateStartedBattleAsync();
        var round = await fixture.Db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
        var firstRoundId = round.Id;
        for (var index = 0; index < 3; index++)
        {
            Assert.True((await fixture.Battles.RecordBattleResultAsync(
                battleId, round.Id, fixture.PlayerA.Id, fixture.PlayerA.Id, ResultType.SpinFinish)).Succeeded);
            var completed = await fixture.Battles.CompleteRoundAsync(battleId, round.Id, fixture.PlayerA.Id);
            if (index < 2) round = completed.Value!;
        }

        Assert.True((await fixture.Battles.ReviseRoundAsync(
            battleId, firstRoundId, fixture.PlayerA.Id, fixture.PlayerB.Id,
            ResultType.SpinFinish, "等待重排時修正首局勝方")).Succeeded);

        fixture.Db.ChangeTracker.Clear();
        var revised = await fixture.Db.Battles.SingleAsync(x => x.Id == battleId);
        Assert.Equal(BattleStatus.InProgress, revised.Status);
        Assert.Equal(0, revised.SideAScore);
        Assert.Equal(1, revised.SideBScore);
        var restarted = await fixture.Db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
        Assert.Equal(2, restarted.PositionNo);
        Assert.All(await fixture.Db.BattleRoundEvents.Where(x => x.BattleRound.RoundNo > 1).ToListAsync(),
            x => Assert.False(x.IsEffective));
        Assert.Single(await fixture.Db.BattleRoundRevisions.Where(x => x.BattleRoundId == firstRoundId).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_IsCapturedAtSubmission_AndReorderPreservesOriginalSnapshot(bool recordBeforeBattle)
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        await PartCatalog.ImportAsync(fixture.Db);
        var configurationService = new BeybladeConfigurationService(fixture.Db);
        var ids = await fixture.Db.Parts.Where(x =>
            (x.Category == PartCategory.Blade && x.Name == "時鐘幻象") ||
            (x.Category == PartCategory.Ratchet && x.Name == "4-55") ||
            (x.Category == PartCategory.Bit && x.Name == "S")).Select(x => x.Id).ToArrayAsync();
        var bladeId = fixture.PlayerABladeIds[0];
        var originalName = (await fixture.Db.Beyblades.FindAsync(bladeId))!.Name;
        if (recordBeforeBattle)
            Assert.True((await configurationService.RecordAsync(fixture.PlayerA.Id, bladeId, ids)).Succeeded);
        var battleId = await fixture.CreateStartedBattleAsync();
        if (!recordBeforeBattle)
            Assert.True((await configurationService.RecordAsync(fixture.PlayerA.Id, bladeId, ids)).Succeeded);
        var configurationId = await fixture.Db.BeybladeConfigurations.Select(x => x.Id).SingleAsync();
        int? expected = recordBeforeBattle ? configurationId : null;
        var expectedName = recordBeforeBattle ? originalName + " · v1 · 時鐘幻象4-55S" : originalName;
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(expected, (await fixture.Db.BattleLineupSelections
            .SingleAsync(x => x.BattleId == battleId && x.BeybladeId == bladeId)).BeybladeConfigurationId);
        Assert.Equal(expected, (await fixture.Db.BattleLineups
            .SingleAsync(x => x.BattleId == battleId && x.PlayerABeybladeId == bladeId)).PlayerAConfigurationId);
        Assert.Equal(expectedName, (await fixture.Db.BattleLineupSelections
            .SingleAsync(x => x.BattleId == battleId && x.BeybladeId == bladeId)).BeybladeNameSnapshot);
        Assert.True((await new BeybladeService(fixture.Db).RenameAsync(fixture.PlayerA.Id, bladeId, "renamed")).Succeeded);
        var newBitId = await fixture.Db.Parts.Where(x => x.Category == PartCategory.Bit && x.Name == "J").Select(x => x.Id).SingleAsync();
        var newParts = await fixture.Db.Parts.Where(x => ids.Contains(x.Id) && x.Category != PartCategory.Bit).Select(x => x.Id).ToListAsync();
        newParts.Add(newBitId);
        Assert.True((await configurationService.RecordAsync(fixture.PlayerA.Id, bladeId, newParts)).Succeeded);
        for (var index = 0; index < 3; index++)
        {
            var round = await fixture.Db.BattleRounds.SingleAsync(x => x.BattleId == battleId && x.Status == BattleRoundStatus.InProgress);
            Assert.True((await fixture.Battles.RecordBattleResultAsync(
                battleId, round.Id, fixture.PlayerA.Id, fixture.PlayerA.Id, ResultType.SpinFinish)).Succeeded);
            Assert.True((await fixture.Battles.CompleteRoundAsync(battleId, round.Id, fixture.PlayerA.Id)).Succeeded);
        }
        Assert.True((await fixture.Flow.SubmitReorderAsync(battleId, fixture.PlayerA.Id, fixture.PlayerABladeIds.AsEnumerable().Reverse().ToArray())).Succeeded);
        Assert.True((await fixture.Flow.SubmitReorderAsync(battleId, fixture.PlayerB.Id, fixture.PlayerBBladeIds)).Succeeded);
        fixture.Db.ChangeTracker.Clear();
        Assert.All(await fixture.Db.BattleLineups.Where(x => x.BattleId == battleId && x.PlayerABeybladeId == bladeId).ToListAsync(),
            x => { Assert.Equal(expected, x.PlayerAConfigurationId); Assert.Equal(expectedName, x.PlayerABeybladeNameSnapshot); });
        Assert.Equal(expected, (await fixture.Db.BattleLineupSelections.SingleAsync(
            x => x.BattleId == battleId && x.SequenceNo == 2 && x.BeybladeId == bladeId)).BeybladeConfigurationId);
        var nextBattle = await fixture.CreateAcceptedBattleAsync();
        Assert.False((await fixture.Flow.SubmitLineupAsync(nextBattle, fixture.PlayerA.Id, fixture.PlayerABladeIds)).Succeeded);
        Assert.False((await fixture.Flow.SubmitLineupAsync(nextBattle, fixture.PlayerA.Id, fixture.PlayerABladeIds, [configurationId, configurationId, 0])).Succeeded);
        Assert.True((await fixture.Flow.SubmitLineupAsync(nextBattle, fixture.PlayerA.Id, fixture.PlayerABladeIds, [configurationId, 0, 0])).Succeeded);
        var secondConfigurationId = (await configurationService.GetMineAsync(fixture.PlayerA.Id, bladeId))!.Id;
        Assert.False((await fixture.Flow.SubmitLineupAsync(nextBattle, fixture.PlayerA.Id, fixture.PlayerABladeIds, [secondConfigurationId, 0, 0])).Succeeded);
        Assert.Equal(configurationId, (await fixture.Db.BattleLineupSelections.SingleAsync(
            x => x.BattleId == nextBattle && x.BeybladeId == bladeId)).BeybladeConfigurationId);
        Assert.Equal("renamed · v1 · 時鐘幻象4-55S", (await fixture.Db.BattleLineupSelections.SingleAsync(
            x => x.BattleId == nextBattle && x.BeybladeId == bladeId)).BeybladeNameSnapshot);
    }

    [Fact]
    public async Task ConfiguredLineup_RejectsDuplicateParts_AndAcceptsDistinctVersions()
    {
        await using var fixture = await QuickBattleFixture.CreateAsync();
        await PartCatalog.ImportAsync(fixture.Db);
        var configurations = new BeybladeConfigurationService(fixture.Db);

        async Task<int> RecordAsync(int bladeId, string bladeName, string ratchetName, string bitName)
        {
            var partIds = await fixture.Db.Parts
                .Where(x =>
                    (x.Category == PartCategory.Blade && x.Name == bladeName) ||
                    (x.Category == PartCategory.Ratchet && x.Name == ratchetName) ||
                    (x.Category == PartCategory.Bit && x.Name == bitName))
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.Equal(3, partIds.Length);
            Assert.True((await configurations.RecordAsync(fixture.PlayerA.Id, bladeId, partIds)).Succeeded);
            return await fixture.Db.BeybladeConfigurations
                .Where(x => x.BeybladeId == bladeId)
                .OrderByDescending(x => x.VersionNo)
                .Select(x => x.Id)
                .FirstAsync();
        }

        var first = await RecordAsync(fixture.PlayerABladeIds[0], "時鐘幻象", "1-50", "J");
        var duplicated = await RecordAsync(fixture.PlayerABladeIds[1], "地獄鐮刀", "1-50", "S");
        var third = await RecordAsync(fixture.PlayerABladeIds[2], "騎士長槍", "3-60", "B");
        var rejectedBattleId = await fixture.CreateAcceptedBattleAsync();

        var rejected = await fixture.Flow.SubmitLineupAsync(
            rejectedBattleId,
            fixture.PlayerA.Id,
            fixture.PlayerABladeIds,
            [first, duplicated, third]);

        Assert.False(rejected.Succeeded);
        Assert.Contains("1-50", rejected.Error);
        Assert.Empty(await fixture.Db.BattleLineupSelections
            .Where(x => x.BattleId == rejectedBattleId && x.UserId == fixture.PlayerA.Id)
            .ToListAsync());

        var distinct = await RecordAsync(fixture.PlayerABladeIds[1], "地獄鐮刀", "2-60", "S");
        var acceptedBattleId = await fixture.CreateAcceptedBattleAsync();
        Assert.True((await fixture.Flow.SubmitLineupAsync(
            acceptedBattleId,
            fixture.PlayerA.Id,
            fixture.PlayerABladeIds,
            [first, distinct, third])).Succeeded);
    }

    private sealed class QuickBattleFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public QuickBattleFlowService Flow { get; }
        public BattleService Battles { get; }
        public RecordingRealtimePublisher Publisher { get; }
        public User PlayerA { get; private init; } = null!;
        public User PlayerB { get; private init; } = null!;
        public List<int> PlayerABladeIds { get; private init; } = [];
        public List<int> PlayerBBladeIds { get; private init; } = [];

        private QuickBattleFixture(SqliteConnection connection, AppDbContext db)
        {
            this.connection = connection;
            Db = db;
            Publisher = new RecordingRealtimePublisher();
            Flow = new QuickBattleFlowService(db, realtimePublisher: Publisher);
            Battles = new BattleService(db, Publisher);
        }

        public static async Task<QuickBattleFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var playerA = new User { Account = "quick-a", PasswordHash = "x", DisplayName = "Quick A", CreatedAtUtc = now, UpdatedAtUtc = now };
            var playerB = new User { Account = "quick-b", PasswordHash = "x", DisplayName = "Quick B", CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Users.AddRange(playerA, playerB);
            await db.SaveChangesAsync();
            var blades = Enumerable.Range(1, 3).Select(index => new Beyblade { UserId = playerA.Id, Name = $"A{index}", CreatedAtUtc = now, UpdatedAtUtc = now })
                .Concat(Enumerable.Range(1, 3).Select(index => new Beyblade { UserId = playerB.Id, Name = $"B{index}", CreatedAtUtc = now, UpdatedAtUtc = now }))
                .ToList();
            db.Beyblades.AddRange(blades);
            await db.SaveChangesAsync();
            return new QuickBattleFixture(connection, db)
            {
                PlayerA = playerA,
                PlayerB = playerB,
                PlayerABladeIds = blades.Take(3).Select(x => x.Id).ToList(),
                PlayerBBladeIds = blades.Skip(3).Select(x => x.Id).ToList()
            };
        }

        public async Task<int> CreateAcceptedBattleAsync()
        {
            var invitation = (await Flow.SendInvitationAsync(PlayerA.Id, PlayerB.Id)).Value!;
            return (await Flow.AcceptInvitationAsync(invitation.Id, PlayerB.Id)).Value;
        }

        public async Task<int> CreateBattleInReviewAsync()
        {
            var battleId = await CreateAcceptedBattleAsync();
            Assert.True((await Flow.SubmitLineupAsync(battleId, PlayerA.Id, PlayerABladeIds)).Succeeded);
            Assert.True((await Flow.SubmitLineupAsync(battleId, PlayerB.Id, PlayerBBladeIds)).Succeeded);
            return battleId;
        }

        public async Task<int> CreateStartedBattleAsync()
        {
            var battleId = await CreateBattleInReviewAsync();
            Assert.True((await Flow.ConfirmLineupAsync(battleId, PlayerA.Id)).Succeeded);
            Assert.True((await Flow.ConfirmLineupAsync(battleId, PlayerB.Id)).Succeeded);
            Assert.True((await Battles.AssignSidesAsync(battleId, PlayerA.Id, BattleSide.B)).Succeeded);
            Assert.True((await Battles.StartBattleAsync(battleId, PlayerA.Id)).Succeeded);
            return battleId;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingRealtimePublisher : IRealtimePublisher
    {
        public List<(int UserId, string EventType, object Payload)> Events { get; } = [];

        public Task PublishUserAsync(int userId, string eventType, object payload, CancellationToken cancellationToken = default)
        {
            Events.Add((userId, eventType, payload));
            return Task.CompletedTask;
        }

        public Task PublishUsersAsync(IEnumerable<int> userIds, string eventType, object payload, CancellationToken cancellationToken = default)
        {
            Events.AddRange(userIds.Distinct().Select(userId => (userId, eventType, payload)));
            return Task.CompletedTask;
        }
    }
}
