namespace BeybladeRecordSystem.Domain.Enums;

public enum UserNotificationKind
{
    Information,
    Invitation,
    ActionRequired,
    InvitationAccepted,
    InvitationDeclined,
    InvitationCancelled,
    InvitationInvalidated,
    BattleReady,
    BattleCompleted,
    TournamentUpdate
}

public enum UserNotificationActionType
{
    None,
    AcceptQuickBattleInvitation,
    AcceptTournamentInvitation,
    AcceptTeamInvitation,
    AcceptRepresentativeTransfer
}
