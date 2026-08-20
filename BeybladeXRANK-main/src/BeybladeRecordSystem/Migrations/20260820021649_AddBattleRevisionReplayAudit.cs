using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBattleRevisionReplayAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NewBattleSnapshot",
                table: "BattleRoundRevisions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreviousBattleSnapshot",
                table: "BattleRoundRevisions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "InvalidationReason",
                table: "BattleRoundEvents",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewBattleSnapshot",
                table: "BattleRoundRevisions");

            migrationBuilder.DropColumn(
                name: "PreviousBattleSnapshot",
                table: "BattleRoundRevisions");

            migrationBuilder.DropColumn(
                name: "InvalidationReason",
                table: "BattleRoundEvents");
        }
    }
}
