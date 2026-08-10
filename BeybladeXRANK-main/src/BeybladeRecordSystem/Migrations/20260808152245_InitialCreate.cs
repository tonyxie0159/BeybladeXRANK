using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Battles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerAId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerBId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerAScore = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerBScore = table.Column<int>(type: "INTEGER", nullable: false),
                    WinningPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Battles", x => x.Id);
                    table.CheckConstraint("CK_Battle_DifferentPlayers", "PlayerAId <> PlayerBId");
                    table.ForeignKey(
                        name: "FK_Battles_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Battles_Users_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Battles_Users_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Beyblades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beyblades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Beyblades_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BattleLineups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleId = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNo = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerABeybladeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerABeybladeNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerBBeybladeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerBBeybladeNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleLineups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattleLineups_Battles_BattleId",
                        column: x => x.BattleId,
                        principalTable: "Battles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BattleLineups_Beyblades_PlayerABeybladeId",
                        column: x => x.PlayerABeybladeId,
                        principalTable: "Beyblades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleLineups_Beyblades_PlayerBBeybladeId",
                        column: x => x.PlayerBBeybladeId,
                        principalTable: "Beyblades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BattleRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineupId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundNo = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerABeybladeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerABeybladeNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerBBeybladeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerBBeybladeNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattleRounds_BattleLineups_LineupId",
                        column: x => x.LineupId,
                        principalTable: "BattleLineups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleRounds_Battles_BattleId",
                        column: x => x.BattleId,
                        principalTable: "Battles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BattleRoundEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleRoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    WinnerPlayerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResultType = table.Column<int>(type: "INTEGER", nullable: true),
                    ScoreAwarded = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEffective = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleRoundEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattleRoundEvents_BattleRounds_BattleRoundId",
                        column: x => x.BattleRoundId,
                        principalTable: "BattleRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BattleRoundRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BattleRoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousEffectiveEventSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    NewEffectiveEventSnapshot = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleRoundRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattleRoundRevisions_BattleRounds_BattleRoundId",
                        column: x => x.BattleRoundId,
                        principalTable: "BattleRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BattleRoundRevisions_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_BattleId_SequenceNo_PositionNo",
                table: "BattleLineups",
                columns: new[] { "BattleId", "SequenceNo", "PositionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerABeybladeId",
                table: "BattleLineups",
                column: "PlayerABeybladeId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerBBeybladeId",
                table: "BattleLineups",
                column: "PlayerBBeybladeId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRoundEvents_BattleRoundId_EventSequence",
                table: "BattleRoundEvents",
                columns: new[] { "BattleRoundId", "EventSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleRoundRevisions_BattleRoundId",
                table: "BattleRoundRevisions",
                column: "BattleRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRoundRevisions_ChangedByUserId",
                table: "BattleRoundRevisions",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRounds_BattleId_RoundNo",
                table: "BattleRounds",
                columns: new[] { "BattleId", "RoundNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleRounds_LineupId",
                table: "BattleRounds",
                column: "LineupId");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_CreatedByUserId",
                table: "Battles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_PlayerAId",
                table: "Battles",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_PlayerBId",
                table: "Battles",
                column: "PlayerBId");

            migrationBuilder.CreateIndex(
                name: "IX_Beyblades_UserId_Name",
                table: "Beyblades",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Account",
                table: "Users",
                column: "Account",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleRoundEvents");

            migrationBuilder.DropTable(
                name: "BattleRoundRevisions");

            migrationBuilder.DropTable(
                name: "BattleRounds");

            migrationBuilder.DropTable(
                name: "BattleLineups");

            migrationBuilder.DropTable(
                name: "Battles");

            migrationBuilder.DropTable(
                name: "Beyblades");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
