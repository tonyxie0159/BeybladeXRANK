using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBeybladeConfigurationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BeybladeConfigurations_BeybladeId",
                table: "BeybladeConfigurations");

            migrationBuilder.AddColumn<string>(
                name: "UpperName",
                table: "Beyblades",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartsKey",
                table: "BeybladeConfigurations",
                type: "character varying(65)",
                maxLength: 65,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "VersionNo",
                table: "BeybladeConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "BeybladeNameSnapshot",
                table: "BattleLineupSelections",
                type: "character varying(520)",
                maxLength: 520,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(503)",
                oldMaxLength: 503);

            migrationBuilder.Sql("""
                UPDATE "BeybladeConfigurations" c
                SET "PartsKey" = (SELECT string_agg(p."PartId"::text, ',' ORDER BY p."PartId")
                                 FROM "BeybladeConfigurationParts" p WHERE p."ConfigurationId" = c."Id");
                UPDATE "Beyblades" b
                SET "UpperName" = names.upper_name
                FROM (
                    SELECT c."BeybladeId",
                        COALESCE(MAX(cp."PartNameSnapshot") FILTER (WHERE p."Category" = 0),
                            MAX(cp."PartNameSnapshot") FILTER (WHERE p."Category" = 3) ||
                            COALESCE(MAX(cp."PartNameSnapshot") FILTER (WHERE p."Category" = 4),
                                     MAX(cp."PartNameSnapshot") FILTER (WHERE p."Category" = 6))) AS upper_name
                    FROM "BeybladeConfigurations" c
                    JOIN "BeybladeConfigurationParts" cp ON cp."ConfigurationId" = c."Id"
                    JOIN "Parts" p ON p."Id" = cp."PartId"
                    GROUP BY c."BeybladeId"
                ) names WHERE b."Id" = names."BeybladeId";
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Beyblades" WHERE NOT "IsDeleted" AND "UpperName" IS NOT NULL
                               GROUP BY "UserId", "UpperName" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate owned upper names: resolve legacy Beyblade ownership/grouping before upgrading; history was not merged.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Beyblades_UserId_UpperName",
                table: "Beyblades",
                columns: new[] { "UserId", "UpperName" },
                unique: true,
                filter: "\"UpperName\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_BeybladeConfigurations_BeybladeId_PartsKey",
                table: "BeybladeConfigurations",
                columns: new[] { "BeybladeId", "PartsKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeybladeConfigurations_BeybladeId_VersionNo",
                table: "BeybladeConfigurations",
                columns: new[] { "BeybladeId", "VersionNo" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Configuration_Version",
                table: "BeybladeConfigurations",
                sql: "\"VersionNo\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "BeybladeConfigurations" GROUP BY "BeybladeId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot downgrade: multiple configuration versions exist.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "BattleLineupSelections" WHERE LENGTH("BeybladeNameSnapshot") > 503) THEN
                        RAISE EXCEPTION 'Cannot downgrade: version names exceed the old snapshot limit.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Beyblades_UserId_UpperName",
                table: "Beyblades");

            migrationBuilder.DropIndex(
                name: "IX_BeybladeConfigurations_BeybladeId_PartsKey",
                table: "BeybladeConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_BeybladeConfigurations_BeybladeId_VersionNo",
                table: "BeybladeConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Configuration_Version",
                table: "BeybladeConfigurations");

            migrationBuilder.DropColumn(
                name: "UpperName",
                table: "Beyblades");

            migrationBuilder.DropColumn(
                name: "PartsKey",
                table: "BeybladeConfigurations");

            migrationBuilder.DropColumn(
                name: "VersionNo",
                table: "BeybladeConfigurations");

            migrationBuilder.AlterColumn<string>(
                name: "BeybladeNameSnapshot",
                table: "BattleLineupSelections",
                type: "character varying(503)",
                maxLength: 503,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(520)",
                oldMaxLength: 520);

            migrationBuilder.CreateIndex(
                name: "IX_BeybladeConfigurations_BeybladeId",
                table: "BeybladeConfigurations",
                column: "BeybladeId",
                unique: true);
        }
    }
}
