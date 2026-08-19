using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMatchLineupFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMatchRepresentative",
                table: "TournamentMatchParticipants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BattleTeamOrderSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleId = table.Column<int>(type: "INTEGER", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleTeamOrderSelections", x => x.Id);
                    table.CheckConstraint("CK_BattleTeamOrderSelection_PositionNo", "PositionNo > 0");
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_Battles_BattleId",
                        column: x => x.BattleId,
                        principalTable: "Battles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_TournamentEntries_TournamentEntryId",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "IX_BattleTeamOrderSelections_SubmittedByUserId",
                table: "BattleTeamOrderSelections",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_TournamentEntryId",
                table: "BattleTeamOrderSelections",
                column: "TournamentEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_UserId",
                table: "BattleTeamOrderSelections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleTeamOrderSelections");

            migrationBuilder.DropColumn(
                name: "IsMatchRepresentative",
                table: "TournamentMatchParticipants");
        }
    }
}
