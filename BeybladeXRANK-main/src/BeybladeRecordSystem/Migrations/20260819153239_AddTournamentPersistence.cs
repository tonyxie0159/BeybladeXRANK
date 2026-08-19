using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations;

public partial class AddTournamentPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tournaments",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Mode = table.Column<int>(type: "INTEGER", nullable: false),
                Format = table.Column<int>(type: "INTEGER", nullable: false),
                RegistrationMode = table.Column<int>(type: "INTEGER", nullable: false),
                RuleSet = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                RegistrationStage = table.Column<int>(type: "INTEGER", nullable: false),
                TeamSize = table.Column<int>(type: "INTEGER", nullable: true),
                BeybladesPerPlayer = table.Column<int>(type: "INTEGER", nullable: false),
                ScoreToWin = table.Column<int>(type: "INTEGER", nullable: false),
                TargetEntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                OrganizerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                RulesSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                CancellationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RegistrationClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                Version = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tournaments", x => x.Id);
                table.CheckConstraint("CK_Tournament_BeybladesPerPlayer", "BeybladesPerPlayer > 0");
                table.CheckConstraint("CK_Tournament_ScoreToWin", "ScoreToWin > 0");
                table.CheckConstraint("CK_Tournament_TargetEntryCount", "TargetEntryCount BETWEEN 2 AND 512");
                table.CheckConstraint("CK_Tournament_TeamSize", "(Mode = 0 AND TeamSize IS NULL) OR (Mode = 1 AND TeamSize IN (2, 3))");
                table.ForeignKey("FK_Tournaments_Users_OrganizerUserId", x => x.OrganizerUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TournamentEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                SchedulePosition = table.Column<int>(type: "INTEGER", nullable: true),
                DisplayNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 192, nullable: false),
                TeamName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                IndividualUserId = table.Column<int>(type: "INTEGER", nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RegisteredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                WithdrawnAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TournamentEntries", x => x.Id);
                table.ForeignKey("FK_TournamentEntries_Tournaments_TournamentId", x => x.TournamentId, "Tournaments", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_TournamentEntries_Users_IndividualUserId", x => x.IndividualUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TournamentEntryMembers",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                TournamentEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                MemberOrder = table.Column<int>(type: "INTEGER", nullable: false),
                IsRepresentative = table.Column<bool>(type: "INTEGER", nullable: false),
                DisplayNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TournamentEntryMembers", x => x.Id);
                table.CheckConstraint("CK_TournamentEntryMember_MemberOrder", "MemberOrder > 0");
                table.ForeignKey("FK_TournamentEntryMembers_TournamentEntries_TournamentEntryId", x => x.TournamentEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_TournamentEntryMembers_Tournaments_TournamentId", x => x.TournamentId, "Tournaments", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_TournamentEntryMembers_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TournamentInvitations",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                TournamentEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                InvitedUserId = table.Column<int>(type: "INTEGER", nullable: false),
                InvitedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RespondedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                InvalidatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TournamentInvitations", x => x.Id);
                table.ForeignKey("FK_TournamentInvitations_TournamentEntries_TournamentEntryId", x => x.TournamentEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_TournamentInvitations_Tournaments_TournamentId", x => x.TournamentId, "Tournaments", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_TournamentInvitations_Users_InvitedByUserId", x => x.InvitedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentInvitations_Users_InvitedUserId", x => x.InvitedUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TournamentMatches",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                Bracket = table.Column<int>(type: "INTEGER", nullable: false),
                RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                MatchNumber = table.Column<int>(type: "INTEGER", nullable: false),
                SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                SideASourceKind = table.Column<int>(type: "INTEGER", nullable: false),
                SideASourceReferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                SideBSourceKind = table.Column<int>(type: "INTEGER", nullable: true),
                SideBSourceReferenceId = table.Column<int>(type: "INTEGER", nullable: true),
                SideAEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                SideBEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                WinnerEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                LoserEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                WinnerToMatchId = table.Column<int>(type: "INTEGER", nullable: true),
                LoserToMatchId = table.Column<int>(type: "INTEGER", nullable: true),
                BattleId = table.Column<int>(type: "INTEGER", nullable: true),
                IsBye = table.Column<bool>(type: "INTEGER", nullable: false),
                IsSeedQualifier = table.Column<bool>(type: "INTEGER", nullable: false),
                IsResetFinal = table.Column<bool>(type: "INTEGER", nullable: false),
                ResolutionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                Version = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TournamentMatches", x => x.Id);
                table.CheckConstraint("CK_TournamentMatch_ByeSide", "IsBye = 0 OR SideBSourceReferenceId IS NULL");
                table.CheckConstraint("CK_TournamentMatch_MatchNumber", "MatchNumber > 0");
                table.CheckConstraint("CK_TournamentMatch_RoundNumber", "RoundNumber > 0");
                table.CheckConstraint("CK_TournamentMatch_SequenceNumber", "SequenceNumber > 0");
                table.CheckConstraint("CK_TournamentMatch_SideAReference", "SideASourceReferenceId > 0");
                table.ForeignKey("FK_TournamentMatches_Battles_BattleId", x => x.BattleId, "Battles", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentEntries_LoserEntryId", x => x.LoserEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentEntries_SideAEntryId", x => x.SideAEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentEntries_SideBEntryId", x => x.SideBEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentEntries_WinnerEntryId", x => x.WinnerEntryId, "TournamentEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentMatches_LoserToMatchId", x => x.LoserToMatchId, "TournamentMatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_TournamentMatches_WinnerToMatchId", x => x.WinnerToMatchId, "TournamentMatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TournamentMatches_Tournaments_TournamentId", x => x.TournamentId, "Tournaments", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_TournamentEntries_IndividualUserId", "TournamentEntries", "IndividualUserId");
        migrationBuilder.CreateIndex("IX_TournamentEntries_TournamentId_IndividualUserId", "TournamentEntries", new[] { "TournamentId", "IndividualUserId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentEntries_TournamentId_RegistrationNumber", "TournamentEntries", new[] { "TournamentId", "RegistrationNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentEntries_TournamentId_SchedulePosition", "TournamentEntries", new[] { "TournamentId", "SchedulePosition" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentEntryMembers_TournamentEntryId_MemberOrder", "TournamentEntryMembers", new[] { "TournamentEntryId", "MemberOrder" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentEntryMembers_TournamentId_UserId", "TournamentEntryMembers", new[] { "TournamentId", "UserId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentEntryMembers_UserId", "TournamentEntryMembers", "UserId");
        migrationBuilder.CreateIndex("IX_TournamentInvitations_InvitedByUserId", "TournamentInvitations", "InvitedByUserId");
        migrationBuilder.CreateIndex("IX_TournamentInvitations_InvitedUserId_Status_CreatedAtUtc", "TournamentInvitations", new[] { "InvitedUserId", "Status", "CreatedAtUtc" });
        migrationBuilder.CreateIndex("IX_TournamentInvitations_TournamentEntryId", "TournamentInvitations", "TournamentEntryId");
        migrationBuilder.CreateIndex("IX_TournamentInvitations_TournamentId_Status", "TournamentInvitations", new[] { "TournamentId", "Status" });
        migrationBuilder.CreateIndex("IX_TournamentMatches_BattleId", "TournamentMatches", "BattleId", unique: true);
        migrationBuilder.CreateIndex("IX_TournamentMatches_LoserEntryId", "TournamentMatches", "LoserEntryId");
        migrationBuilder.CreateIndex("IX_TournamentMatches_LoserToMatchId", "TournamentMatches", "LoserToMatchId");
        migrationBuilder.CreateIndex("IX_TournamentMatches_SideAEntryId", "TournamentMatches", "SideAEntryId");
        migrationBuilder.CreateIndex("IX_TournamentMatches_SideBEntryId", "TournamentMatches", "SideBEntryId");
        migrationBuilder.CreateIndex("IX_TournamentMatches_TournamentId_Bracket_RoundNumber_MatchNumber", "TournamentMatches", new[] { "TournamentId", "Bracket", "RoundNumber", "MatchNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentMatches_TournamentId_SequenceNumber", "TournamentMatches", new[] { "TournamentId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_TournamentMatches_TournamentId_Status", "TournamentMatches", new[] { "TournamentId", "Status" });
        migrationBuilder.CreateIndex("IX_TournamentMatches_WinnerEntryId", "TournamentMatches", "WinnerEntryId");
        migrationBuilder.CreateIndex("IX_TournamentMatches_WinnerToMatchId", "TournamentMatches", "WinnerToMatchId");
        migrationBuilder.CreateIndex("IX_Tournaments_OrganizerUserId_UpdatedAtUtc", "Tournaments", new[] { "OrganizerUserId", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_Tournaments_Status_UpdatedAtUtc", "Tournaments", new[] { "Status", "UpdatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TournamentEntryMembers");
        migrationBuilder.DropTable("TournamentInvitations");
        migrationBuilder.DropTable("TournamentMatches");
        migrationBuilder.DropTable("TournamentEntries");
        migrationBuilder.DropTable("Tournaments");
    }
}
