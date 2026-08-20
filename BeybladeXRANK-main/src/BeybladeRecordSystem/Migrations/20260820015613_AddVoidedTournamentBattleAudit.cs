using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddVoidedTournamentBattleAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles");

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Battles",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidSnapshot",
                table: "Battles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "Battles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "Battles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedTournamentMatchId",
                table: "Battles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Battles_VoidedByUserId",
                table: "Battles",
                column: "VoidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_VoidedTournamentMatchId",
                table: "Battles",
                column: "VoidedTournamentMatchId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles",
                sql: "(SourceType = 0 AND Status <> 7 AND TournamentMatchId IS NULL AND VoidedTournamentMatchId IS NULL) OR (SourceType IN (1, 2) AND ((Status <> 7 AND TournamentMatchId IS NOT NULL AND VoidedTournamentMatchId IS NULL) OR (Status = 7 AND TournamentMatchId IS NULL AND VoidedTournamentMatchId IS NOT NULL AND VoidedByUserId IS NOT NULL AND VoidedAtUtc IS NOT NULL AND LENGTH(TRIM(VoidReason)) > 0 AND VoidSnapshot IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "FK_Battles_TournamentMatches_VoidedTournamentMatchId",
                table: "Battles",
                column: "VoidedTournamentMatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Battles_Users_VoidedByUserId",
                table: "Battles",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Battles_TournamentMatches_VoidedTournamentMatchId",
                table: "Battles");

            migrationBuilder.DropForeignKey(
                name: "FK_Battles_Users_VoidedByUserId",
                table: "Battles");

            migrationBuilder.DropIndex(
                name: "IX_Battles_VoidedByUserId",
                table: "Battles");

            migrationBuilder.DropIndex(
                name: "IX_Battles_VoidedTournamentMatchId",
                table: "Battles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "VoidSnapshot",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "VoidedTournamentMatchId",
                table: "Battles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles",
                sql: "(SourceType = 0 AND TournamentMatchId IS NULL) OR (SourceType IN (1, 2) AND TournamentMatchId IS NOT NULL)");
        }
    }
}
