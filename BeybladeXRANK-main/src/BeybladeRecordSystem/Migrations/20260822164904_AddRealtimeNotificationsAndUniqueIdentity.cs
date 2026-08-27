using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeNotificationsAndUniqueIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Account",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedAccount",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedDisplayName",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE Users
                SET NormalizedAccount = UPPER(TRIM(Account)),
                    NormalizedDisplayName = UPPER(TRIM(DisplayName));
                """);

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionEntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedAccount",
                table: "Users",
                column: "NormalizedAccount",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedDisplayName",
                table: "Users",
                column: "NormalizedDisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_CreatedAtUtc",
                table: "UserNotifications",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_DedupeKey",
                table: "UserNotifications",
                columns: new[] { "UserId", "DedupeKey" },
                unique: true,
                filter: "ResolvedAtUtc IS NULL AND DedupeKey IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedAccount",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedDisplayName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedAccount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedDisplayName",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Account",
                table: "Users",
                column: "Account",
                unique: true);
        }
    }
}
