using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class ExpandBeybladeNameSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BeybladeNameSnapshot",
                table: "BattleLineupSelections",
                type: "character varying(503)",
                maxLength: 503,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL length casts can truncate; refuse rollback if newer snapshots no longer fit.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BattleLineupSelections" WHERE LENGTH("BeybladeNameSnapshot") > 100) THEN
                        RAISE EXCEPTION 'Cannot shrink BeybladeNameSnapshot: existing names exceed 100 characters.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "BeybladeNameSnapshot",
                table: "BattleLineupSelections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(503)",
                oldMaxLength: 503);
        }
    }
}
