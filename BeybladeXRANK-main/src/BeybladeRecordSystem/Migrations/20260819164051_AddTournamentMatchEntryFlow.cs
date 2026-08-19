using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentMatchEntryFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BattleLineupSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    BeybladeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerDisplayNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BeybladeNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleLineupSelections", x => x.Id);
                    table.CheckConstraint("CK_BattleLineupSelection_PositionNo", "PositionNo > 0");
                    table.ForeignKey(
                        name: "FK_BattleLineupSelections_Battles_BattleId",
                        column: x => x.BattleId,
                        principalTable: "Battles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BattleLineupSelections_Beyblades_BeybladeId",
                        column: x => x.BeybladeId,
                        principalTable: "Beyblades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleLineupSelections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentMatchParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentMatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LineupConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LineupConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatchParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatchParticipants_TournamentEntries_TournamentEntryId",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatchParticipants_TournamentMatches_TournamentMatchId",
                        column: x => x.TournamentMatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentMatchParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BeybladeId",
                table: "BattleLineupSelections",
                column: "BeybladeId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_UserId",
                table: "BattleLineupSelections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_TournamentEntryId",
                table: "TournamentMatchParticipants",
                column: "TournamentEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_TournamentMatchId_TournamentEntryId_UserId",
                table: "TournamentMatchParticipants",
                columns: new[] { "TournamentMatchId", "TournamentEntryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_TournamentMatchId_UserId",
                table: "TournamentMatchParticipants",
                columns: new[] { "TournamentMatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_UserId",
                table: "TournamentMatchParticipants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleLineupSelections");

            migrationBuilder.DropTable(
                name: "TournamentMatchParticipants");
        }
    }
}
