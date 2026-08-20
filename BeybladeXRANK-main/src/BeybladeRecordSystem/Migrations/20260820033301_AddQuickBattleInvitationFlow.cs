using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBattleInvitationFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LineupSequenceNo",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PendingLineupEditRequestedByUserId",
                table: "Battles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlayerAEditRequestUsed",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlayerALineupConfirmed",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlayerBEditRequestUsed",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlayerBLineupConfirmed",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QuickBattleInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InviterUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    InviteeUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickBattleInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickBattleInvitations_Users_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickBattleInvitations_Users_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_LineupSequenceNo",
                table: "Battles",
                sql: "LineupSequenceNo > 0");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBattleInvitations_InviteeUserId_CreatedAtUtc",
                table: "QuickBattleInvitations",
                columns: new[] { "InviteeUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QuickBattleInvitations_InviterUserId_InviteeUserId",
                table: "QuickBattleInvitations",
                columns: new[] { "InviterUserId", "InviteeUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickBattleInvitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_LineupSequenceNo",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "LineupSequenceNo",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PendingLineupEditRequestedByUserId",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PlayerAEditRequestUsed",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PlayerALineupConfirmed",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PlayerBEditRequestUsed",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PlayerBLineupConfirmed",
                table: "Battles");
        }
    }
}
