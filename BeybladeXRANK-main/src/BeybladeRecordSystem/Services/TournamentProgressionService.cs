using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Services;

public class TournamentProgressionService(AppDbContext db)
{
    public async Task CompleteMatchAndAdvanceAsync(
        TournamentMatch completedMatch,
        int winnerEntryId,
        int loserEntryId,
        TournamentMatchStatus terminalStatus,
        string resolutionReason,
        DateTime now)
    {
        var tournament = await db.Tournaments.AsSplitQuery()
            .Include(x => x.Entries).ThenInclude(x => x.Members)
            .Include(x => x.Matches).ThenInclude(x => x.Participants)
            .SingleAsync(x => x.Id == completedMatch.TournamentId);
        var match = tournament.Matches.Single(x => x.Id == completedMatch.Id);
        match.WinnerEntryId = winnerEntryId;
        match.LoserEntryId = loserEntryId;
        match.Status = terminalStatus;
        match.ResolutionReason = resolutionReason;
        match.CompletedAtUtc = now;
        match.UpdatedAtUtc = now;
        match.Version = Guid.NewGuid().ToByteArray();

        var resetFinal = tournament.Matches.SingleOrDefault(x => x.IsResetFinal && x.Status == TournamentMatchStatus.WaitingForParticipants &&
            (x.SideASourceReferenceId == match.Id || x.SideBSourceReferenceId == match.Id));
        if (match.Bracket == TournamentBracket.GrandFinal && !match.IsResetFinal && resetFinal is not null)
        {
            var undefeatedEntryId = ResolveUndefeatedFinalist(tournament.Matches, match);
            if (winnerEntryId == undefeatedEntryId)
            {
                resetFinal.WinnerEntryId = winnerEntryId;
                resetFinal.LoserEntryId = loserEntryId;
                resetFinal.Status = TournamentMatchStatus.NotRequired;
                resetFinal.ResolutionReason = "ResetFinalNotRequired";
                resetFinal.CompletedAtUtc = now;
                resetFinal.UpdatedAtUtc = now;
                resetFinal.Version = Guid.NewGuid().ToByteArray();
            }
        }

        foreach (var target in tournament.Matches.Where(x => x.Status == TournamentMatchStatus.WaitingForParticipants))
        {
            if (target.SideASourceReferenceId == match.Id)
                target.SideAEntryId = ResolveSource(target.SideASourceKind, target.SideAEntryId, winnerEntryId, loserEntryId);
            if (target.SideBSourceReferenceId == match.Id)
                target.SideBEntryId = ResolveSource(target.SideBSourceKind, target.SideBEntryId, winnerEntryId, loserEntryId);
            target.UpdatedAtUtc = now;
            target.Version = Guid.NewGuid().ToByteArray();
        }

        var next = tournament.Matches
            .Where(x => !x.IsBye && x.Status == TournamentMatchStatus.WaitingForParticipants && x.SideAEntryId is not null && x.SideBEntryId is not null)
            .OrderBy(x => x.SequenceNumber)
            .FirstOrDefault();
        if (next is null && tournament.Format == TournamentFormat.Swiss)
            next = CreateNextSwissRound(tournament, now);
        if (next is not null)
        {
            next.Status = TournamentMatchStatus.AwaitingParticipationConfirmation;
            next.UpdatedAtUtc = now;
            next.Version = Guid.NewGuid().ToByteArray();
            foreach (var entryId in new[] { next.SideAEntryId!.Value, next.SideBEntryId!.Value })
            {
                var entry = tournament.Entries.Single(x => x.Id == entryId);
                if (tournament.Mode == TournamentMode.Individual)
                    next.Participants.Add(CreateParticipant(next, entry, entry.IndividualUserId!.Value, false, now));
                else
                    foreach (var member in entry.Members.OrderBy(x => x.MemberOrder))
                        next.Participants.Add(CreateParticipant(next, entry, member.UserId, member.IsRepresentative, now));
            }
        }
        else if (tournament.Matches.All(x => x.Status is TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or TournamentMatchStatus.Forfeited or TournamentMatchStatus.NotRequired))
        {
            tournament.Status = TournamentStatus.Completed;
            tournament.CompletedAtUtc = now;
        }
        tournament.UpdatedAtUtc = now;
        tournament.Version = Guid.NewGuid().ToByteArray();
    }

    private static int? ResolveSource(TournamentParticipantSourceKind? kind, int? current, int winnerEntryId, int loserEntryId) => kind switch
    {
        TournamentParticipantSourceKind.MatchWinner => winnerEntryId,
        TournamentParticipantSourceKind.MatchLoser => loserEntryId,
        _ => current
    };

    private static int ResolveUndefeatedFinalist(IEnumerable<TournamentMatch> matches, TournamentMatch grandFinal)
    {
        if (grandFinal.SideASourceKind == TournamentParticipantSourceKind.MatchWinner &&
            matches.Single(x => x.Id == grandFinal.SideASourceReferenceId).Bracket == TournamentBracket.Winners)
            return grandFinal.SideAEntryId!.Value;
        if (grandFinal.SideBSourceKind == TournamentParticipantSourceKind.MatchWinner &&
            matches.Single(x => x.Id == grandFinal.SideBSourceReferenceId).Bracket == TournamentBracket.Winners)
            return grandFinal.SideBEntryId!.Value;
        throw new InvalidOperationException("Grand Final 缺少勝部冠軍來源。");
    }

    private static TournamentMatch? CreateNextSwissRound(Tournament tournament, DateTime now)
    {
        var scheduledEntryIds = tournament.Matches.Where(x => x.Bracket == TournamentBracket.Swiss && x.RoundNumber == 1)
            .SelectMany(x => new[] { x.SideAEntryId, x.SideBEntryId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToHashSet();
        var scheduledEntries = tournament.Entries.Where(x => scheduledEntryIds.Contains(x.Id)).ToList();
        var currentRound = tournament.Matches.Where(x => x.Bracket == TournamentBracket.Swiss).Max(x => x.RoundNumber);
        if (currentRound >= SwissPairingGenerator.RoundCountFor(scheduledEntries.Count)) return null;
        var completed = tournament.Matches.Where(x => x.Bracket == TournamentBracket.Swiss && IsTerminal(x.Status)).ToList();
        if (tournament.Matches.Any(x => x.Bracket == TournamentBracket.Swiss && x.RoundNumber == currentRound && !IsTerminal(x.Status))) return null;

        var wins = scheduledEntries.ToDictionary(
            entry => entry.Id,
            entry => completed.Count(x => x.WinnerEntryId == entry.Id));
        var opponents = scheduledEntries.ToDictionary(entry => entry.Id, _ => new List<int>());
        foreach (var played in completed.Where(x => !x.IsBye && x.SideAEntryId is not null && x.SideBEntryId is not null))
        {
            opponents[played.SideAEntryId!.Value].Add(played.SideBEntryId!.Value);
            opponents[played.SideBEntryId!.Value].Add(played.SideAEntryId!.Value);
        }
        var playedCount = scheduledEntries.ToDictionary(
            entry => entry.Id,
            entry => completed.Count(x => x.WinnerEntryId == entry.Id || x.LoserEntryId == entry.Id));
        var standings = scheduledEntries.Select(entry =>
        {
            var opponentIds = opponents[entry.Id];
            var buchholz = opponentIds.Sum(id => wins[id]);
            var opponentWinRate = opponentIds.Count == 0 ? 0 : opponentIds.Average(id => (decimal)wins[id] / Math.Max(1, playedCount[id]));
            return new SwissEntryStanding(
                entry.Id,
                wins[entry.Id],
                opponentIds.ToHashSet(),
                completed.Any(x => x.IsBye && x.WinnerEntryId == entry.Id),
                buchholz,
                opponentWinRate);
        }).ToList();
        var nextRoundNumber = currentRound + 1;
        var definition = SwissPairingGenerator.GenerateRound(
            standings,
            nextRoundNumber,
            unchecked((tournament.Id * 397) ^ nextRoundNumber));
        var sequence = tournament.Matches.Max(x => x.SequenceNumber);
        var created = new List<TournamentMatch>();
        for (var index = 0; index < definition.Pairings.Count; index++)
        {
            var pairing = definition.Pairings[index];
            var match = new TournamentMatch
            {
                Tournament = tournament,
                Bracket = TournamentBracket.Swiss,
                RoundNumber = nextRoundNumber,
                MatchNumber = index + 1,
                SequenceNumber = ++sequence,
                Status = TournamentMatchStatus.WaitingForParticipants,
                SideASourceKind = TournamentParticipantSourceKind.Entry,
                SideASourceReferenceId = pairing.EntryAId,
                SideBSourceKind = TournamentParticipantSourceKind.Entry,
                SideBSourceReferenceId = pairing.EntryBId,
                SideAEntryId = pairing.EntryAId,
                SideBEntryId = pairing.EntryBId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            };
            tournament.Matches.Add(match);
            created.Add(match);
        }
        if (definition.ByeEntryId is int byeEntryId)
            tournament.Matches.Add(new TournamentMatch
            {
                Tournament = tournament,
                Bracket = TournamentBracket.Swiss,
                RoundNumber = nextRoundNumber,
                MatchNumber = definition.Pairings.Count + 1,
                SequenceNumber = ++sequence,
                Status = TournamentMatchStatus.Completed,
                SideASourceKind = TournamentParticipantSourceKind.Entry,
                SideASourceReferenceId = byeEntryId,
                SideAEntryId = byeEntryId,
                WinnerEntryId = byeEntryId,
                IsBye = true,
                ResolutionReason = "Bye",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                Version = Guid.NewGuid().ToByteArray()
            });
        return created.OrderBy(x => x.MatchNumber).FirstOrDefault();
    }

    private static bool IsTerminal(TournamentMatchStatus status) => status is
        TournamentMatchStatus.Completed or TournamentMatchStatus.Walkover or
        TournamentMatchStatus.Forfeited or TournamentMatchStatus.NotRequired;

    private static TournamentMatchParticipant CreateParticipant(TournamentMatch match, TournamentEntry entry, int userId, bool isRepresentative, DateTime now) => new()
    {
        TournamentMatch = match,
        TournamentEntryId = entry.Id,
        UserId = userId,
        IsMatchRepresentative = isRepresentative,
        Status = TournamentParticipationStatus.Pending,
        NotifiedAtUtc = now,
        Version = Guid.NewGuid().ToByteArray()
    };
}
