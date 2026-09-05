using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPartsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BeybladeConfigurationId",
                table: "BattleLineupSelections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayerAConfigurationId",
                table: "BattleLineups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayerBConfigurationId",
                table: "BattleLineups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BeybladeConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BeybladeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeybladeConfigurations", x => x.Id);
                    table.UniqueConstraint("AK_BeybladeConfigurations_Id_BeybladeId", x => new { x.Id, x.BeybladeId });
                    table.ForeignKey(
                        name: "FK_BeybladeConfigurations_Beyblades_BeybladeId",
                        column: x => x.BeybladeId,
                        principalTable: "Beyblades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IntegratesRatchet = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                    table.CheckConstraint("CK_Part_Category", "\"Category\" BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_Part_IntegratesRatchet", "NOT \"IntegratesRatchet\" OR \"Category\" IN (0, 2)");
                    table.CheckConstraint("CK_Part_Name", "LENGTH(TRIM(\"Name\")) > 0 AND \"Name\" = TRIM(\"Name\")");
                });

            migrationBuilder.CreateTable(
                name: "BeybladeConfigurationParts",
                columns: table => new
                {
                    ConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    PartNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeybladeConfigurationParts", x => new { x.ConfigurationId, x.PartId });
                    table.ForeignKey(
                        name: "FK_BeybladeConfigurationParts_BeybladeConfigurations_Configura~",
                        column: x => x.ConfigurationId,
                        principalTable: "BeybladeConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BeybladeConfigurationParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartSeries",
                columns: table => new
                {
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    Series = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartSeries", x => new { x.PartId, x.Series });
                    table.CheckConstraint("CK_PartSeries_Series", "\"Series\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_PartSeries_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BeybladeConfigurationId_BeybladeId",
                table: "BattleLineupSelections",
                columns: new[] { "BeybladeConfigurationId", "BeybladeId" });

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerAConfigurationId_PlayerABeybladeId",
                table: "BattleLineups",
                columns: new[] { "PlayerAConfigurationId", "PlayerABeybladeId" });

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerBConfigurationId_PlayerBBeybladeId",
                table: "BattleLineups",
                columns: new[] { "PlayerBConfigurationId", "PlayerBBeybladeId" });

            migrationBuilder.CreateIndex(
                name: "IX_BeybladeConfigurationParts_PartId",
                table: "BeybladeConfigurationParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_BeybladeConfigurations_BeybladeId",
                table: "BeybladeConfigurations",
                column: "BeybladeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_Category_Name",
                table: "Parts",
                columns: new[] { "Category", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleLineups_BeybladeConfigurations_PlayerAConfigurationId~",
                table: "BattleLineups",
                columns: new[] { "PlayerAConfigurationId", "PlayerABeybladeId" },
                principalTable: "BeybladeConfigurations",
                principalColumns: new[] { "Id", "BeybladeId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleLineups_BeybladeConfigurations_PlayerBConfigurationId~",
                table: "BattleLineups",
                columns: new[] { "PlayerBConfigurationId", "PlayerBBeybladeId" },
                principalTable: "BeybladeConfigurations",
                principalColumns: new[] { "Id", "BeybladeId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleLineupSelections_BeybladeConfigurations_BeybladeConfi~",
                table: "BattleLineupSelections",
                columns: new[] { "BeybladeConfigurationId", "BeybladeId" },
                principalTable: "BeybladeConfigurations",
                principalColumns: new[] { "Id", "BeybladeId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BattleLineups_BeybladeConfigurations_PlayerAConfigurationId~",
                table: "BattleLineups");

            migrationBuilder.DropForeignKey(
                name: "FK_BattleLineups_BeybladeConfigurations_PlayerBConfigurationId~",
                table: "BattleLineups");

            migrationBuilder.DropForeignKey(
                name: "FK_BattleLineupSelections_BeybladeConfigurations_BeybladeConfi~",
                table: "BattleLineupSelections");

            migrationBuilder.DropTable(
                name: "BeybladeConfigurationParts");

            migrationBuilder.DropTable(
                name: "PartSeries");

            migrationBuilder.DropTable(
                name: "BeybladeConfigurations");

            migrationBuilder.DropTable(
                name: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineupSelections_BeybladeConfigurationId_BeybladeId",
                table: "BattleLineupSelections");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineups_PlayerAConfigurationId_PlayerABeybladeId",
                table: "BattleLineups");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineups_PlayerBConfigurationId_PlayerBBeybladeId",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "BeybladeConfigurationId",
                table: "BattleLineupSelections");

            migrationBuilder.DropColumn(
                name: "PlayerAConfigurationId",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "PlayerBConfigurationId",
                table: "BattleLineups");
        }
    }
}
