using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBattleLineupSequenceSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_TournamentEntryId_PositionNo",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_TournamentEntryId_UserId",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineupSelections_BattleId_BeybladeId",
                table: "BattleLineupSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineupSelections_BattleId_UserId_PositionNo",
                table: "BattleLineupSelections");

            migrationBuilder.AddColumn<int>(
                name: "SequenceNo",
                table: "BattleTeamOrderSelections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SequenceNo",
                table: "BattleLineupSelections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEntryId_PositionNo",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "SequenceNo", "TournamentEntryId", "PositionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEntryId_UserId",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "SequenceNo", "TournamentEntryId", "UserId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BattleTeamOrderSelection_SequenceNo",
                table: "BattleTeamOrderSelections",
                sql: "SequenceNo > 0");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_BeybladeId",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "SequenceNo", "BeybladeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_UserId_PositionNo",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "SequenceNo", "UserId", "PositionNo" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BattleLineupSelection_SequenceNo",
                table: "BattleLineupSelections",
                sql: "SequenceNo > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEntryId_PositionNo",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEntryId_UserId",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BattleTeamOrderSelection_SequenceNo",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_BeybladeId",
                table: "BattleLineupSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_UserId_PositionNo",
                table: "BattleLineupSelections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BattleLineupSelection_SequenceNo",
                table: "BattleLineupSelections");

            migrationBuilder.DropColumn(
                name: "SequenceNo",
                table: "BattleTeamOrderSelections");

            migrationBuilder.DropColumn(
                name: "SequenceNo",
                table: "BattleLineupSelections");

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_TournamentEntryId_PositionNo",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "TournamentEntryId", "PositionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_TournamentEntryId_UserId",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "TournamentEntryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_BeybladeId",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "BeybladeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_UserId_PositionNo",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "UserId", "PositionNo" },
                unique: true);
        }
    }
}
