using BeybladeRecordSystem.Domain.Enums;
using BeybladeRecordSystem.Domain.Tournaments;

namespace BeybladeRecordSystem.Services;

public static class TournamentDisplay
{
    public static string Label(TournamentStatus value) => value switch
    {
        TournamentStatus.RegistrationOpen => "開放報名",
        TournamentStatus.InProgress => "進行中",
        TournamentStatus.Completed => "已完成",
        TournamentStatus.Cancelled => "已取消",
        _ => "未知狀態"
    };

    public static string Label(TournamentRegistrationStage value) => value switch
    {
        TournamentRegistrationStage.Open => "報名中",
        TournamentRegistrationStage.CapacityReached => "名額已滿",
        TournamentRegistrationStage.Closed => "報名已關閉",
        TournamentRegistrationStage.AwaitingTeamFormation => "等待組隊",
        TournamentRegistrationStage.ScheduleDraftCreated => "賽程草稿已建立",
        TournamentRegistrationStage.AwaitingStart => "等待開始",
        _ => "未知階段"
    };

    public static string Label(TournamentMatchStatus value) => value switch
    {
        TournamentMatchStatus.WaitingForParticipants => "等待參賽者",
        TournamentMatchStatus.AwaitingParticipationConfirmation => "等待出賽確認",
        TournamentMatchStatus.ReadyForLineup => "可提交陣容",
        TournamentMatchStatus.LineupSelection => "選擇陣容中",
        TournamentMatchStatus.TeamOrderSelection => "選擇出賽順序",
        TournamentMatchStatus.LineupReview => "確認陣容中",
        TournamentMatchStatus.LineupLocked => "陣容已鎖定",
        TournamentMatchStatus.SideSelection => "選擇站位",
        TournamentMatchStatus.InProgress => "對戰中",
        TournamentMatchStatus.ReorderSelection => "重新排列中",
        TournamentMatchStatus.VictoryPendingCompletion => "等待確認完賽",
        TournamentMatchStatus.Completed => "已完成",
        TournamentMatchStatus.Forfeited => "棄權",
        TournamentMatchStatus.Walkover => "不戰勝",
        TournamentMatchStatus.Voided => "已撤銷",
        TournamentMatchStatus.NotRequired => "無需進行",
        TournamentMatchStatus.Cancelled => "已取消",
        _ => "未知狀態"
    };

    public static string Label(TournamentEntryStatus value) => value switch
    {
        TournamentEntryStatus.Pending => "等待完成報名",
        TournamentEntryStatus.Registered => "已報名",
        TournamentEntryStatus.Withdrawn => "已退出",
        TournamentEntryStatus.Forfeited => "已棄權",
        _ => "未知狀態"
    };

    public static string Label(TournamentInvitationStatus value) => value switch
    {
        TournamentInvitationStatus.Pending => "等待回覆",
        TournamentInvitationStatus.Accepted => "已接受",
        TournamentInvitationStatus.Declined => "已拒絕",
        TournamentInvitationStatus.Invalidated => "已失效",
        TournamentInvitationStatus.Cancelled => "已取消",
        _ => "未知狀態"
    };

    public static string Label(TournamentParticipationStatus value) => value switch
    {
        TournamentParticipationStatus.Pending => "等待確認",
        TournamentParticipationStatus.Accepted => "已確認出賽",
        TournamentParticipationStatus.Declined => "拒絕出賽",
        TournamentParticipationStatus.Invalidated => "已失效",
        TournamentParticipationStatus.NoShow => "未到場",
        _ => "未知狀態"
    };

    public static string Label(TournamentFormat value) => value switch
    {
        TournamentFormat.SingleElimination => "單淘汰",
        TournamentFormat.DoubleElimination => "雙敗淘汰",
        TournamentFormat.RoundRobin => "單循環",
        TournamentFormat.Swiss => "瑞士輪",
        _ => "未知賽制"
    };

    public static string Label(TournamentRegistrationMode value) => value switch
    {
        TournamentRegistrationMode.Individual => "個人報名",
        TournamentRegistrationMode.CompleteTeam => "隊伍報名",
        TournamentRegistrationMode.SystemAssignedTeam => "由系統自動組隊",
        _ => "未知報名方式"
    };

    public static string BadgeClass(TournamentStatus value) => value switch
    {
        TournamentStatus.RegistrationOpen => "text-bg-success",
        TournamentStatus.InProgress => "text-bg-primary",
        TournamentStatus.Completed => "text-bg-secondary",
        TournamentStatus.Cancelled => "text-bg-danger",
        _ => "text-bg-dark"
    };

    public static string BadgeClass(TournamentRegistrationStage value) => value switch
    {
        TournamentRegistrationStage.Open => "text-bg-success",
        TournamentRegistrationStage.CapacityReached or TournamentRegistrationStage.AwaitingTeamFormation or TournamentRegistrationStage.AwaitingStart => "text-bg-warning",
        TournamentRegistrationStage.ScheduleDraftCreated => "text-bg-info",
        _ => "text-bg-secondary"
    };

    public static string BadgeClass(TournamentMatchStatus value) => value switch
    {
        TournamentMatchStatus.Completed => "text-bg-success",
        TournamentMatchStatus.InProgress => "text-bg-primary",
        TournamentMatchStatus.Forfeited or TournamentMatchStatus.Cancelled or TournamentMatchStatus.Voided => "text-bg-danger",
        TournamentMatchStatus.Walkover => "text-bg-info",
        TournamentMatchStatus.NotRequired => "text-bg-secondary",
        _ => "text-bg-warning"
    };
}
