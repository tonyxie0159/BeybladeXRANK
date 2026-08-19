using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeBattleModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Battles_BattleId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_BattleId",
                table: "TournamentMatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_DifferentPlayers",
                table: "Battles");

            migrationBuilder.RenameColumn(
                name: "PlayerAScore",
                table: "Battles",
                newName: "SideAScore");

            migrationBuilder.RenameColumn(
                name: "PlayerBScore",
                table: "Battles",
                newName: "SideBScore");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerBId",
                table: "Battles",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerAId",
                table: "Battles",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ScoreToWin",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "SideADesignation",
                table: "Battles",
                type: "INTEGER",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TournamentMatchId",
                table: "Battles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WinningSide",
                table: "Battles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerADisplayNameSnapshot",
                table: "BattleRounds",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlayerAId",
                table: "BattleRounds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerBDisplayNameSnapshot",
                table: "BattleRounds",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlayerBId",
                table: "BattleRounds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerADisplayNameSnapshot",
                table: "BattleLineups",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlayerAId",
                table: "BattleLineups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerBDisplayNameSnapshot",
                table: "BattleLineups",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlayerBId",
                table: "BattleLineups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE Battles
                SET TournamentMatchId = (
                    SELECT Id FROM TournamentMatches WHERE TournamentMatches.BattleId = Battles.Id
                )
                WHERE EXISTS (
                    SELECT 1 FROM TournamentMatches WHERE TournamentMatches.BattleId = Battles.Id
                );

                UPDATE Battles
                SET WinningSide = CASE
                    WHEN WinningPlayerId = PlayerAId THEN 0
                    WHEN WinningPlayerId = PlayerBId THEN 1
                    ELSE NULL
                END;

                UPDATE BattleLineups
                SET PlayerAId = (SELECT PlayerAId FROM Battles WHERE Battles.Id = BattleLineups.BattleId),
                    PlayerADisplayNameSnapshot = COALESCE((
                        SELECT Users.DisplayName
                        FROM Battles JOIN Users ON Users.Id = Battles.PlayerAId
                        WHERE Battles.Id = BattleLineups.BattleId
                    ), ''),
                    PlayerBId = (SELECT PlayerBId FROM Battles WHERE Battles.Id = BattleLineups.BattleId),
                    PlayerBDisplayNameSnapshot = COALESCE((
                        SELECT Users.DisplayName
                        FROM Battles JOIN Users ON Users.Id = Battles.PlayerBId
                        WHERE Battles.Id = BattleLineups.BattleId
                    ), '');

                UPDATE BattleRounds
                SET PlayerAId = (SELECT PlayerAId FROM BattleLineups WHERE BattleLineups.Id = BattleRounds.LineupId),
                    PlayerADisplayNameSnapshot = COALESCE((
                        SELECT PlayerADisplayNameSnapshot FROM BattleLineups WHERE BattleLineups.Id = BattleRounds.LineupId
                    ), ''),
                    PlayerBId = (SELECT PlayerBId FROM BattleLineups WHERE BattleLineups.Id = BattleRounds.LineupId),
                    PlayerBDisplayNameSnapshot = COALESCE((
                        SELECT PlayerBDisplayNameSnapshot FROM BattleLineups WHERE BattleLineups.Id = BattleRounds.LineupId
                    ), '');
                """);

            migrationBuilder.DropColumn(
                name: "BattleId",
                table: "TournamentMatches");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_TournamentMatchId",
                table: "Battles",
                column: "TournamentMatchId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_DifferentPlayers",
                table: "Battles",
                sql: "PlayerAId IS NULL OR PlayerBId IS NULL OR PlayerAId <> PlayerBId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_Scores",
                table: "Battles",
                sql: "SideAScore >= 0 AND SideBScore >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_ScoreToWin",
                table: "Battles",
                sql: "ScoreToWin > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles",
                sql: "(SourceType = 0 AND TournamentMatchId IS NULL) OR (SourceType IN (1, 2) AND TournamentMatchId IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRounds_PlayerAId",
                table: "BattleRounds",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRounds_PlayerBId",
                table: "BattleRounds",
                column: "PlayerBId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerAId",
                table: "BattleLineups",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerBId",
                table: "BattleLineups",
                column: "PlayerBId");

            migrationBuilder.AddForeignKey(
                name: "FK_BattleLineups_Users_PlayerAId",
                table: "BattleLineups",
                column: "PlayerAId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleLineups_Users_PlayerBId",
                table: "BattleLineups",
                column: "PlayerBId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleRounds_Users_PlayerAId",
                table: "BattleRounds",
                column: "PlayerAId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BattleRounds_Users_PlayerBId",
                table: "BattleRounds",
                column: "PlayerBId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Battles_TournamentMatches_TournamentMatchId",
                table: "Battles",
                column: "TournamentMatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BattleLineups_Users_PlayerAId",
                table: "BattleLineups");

            migrationBuilder.DropForeignKey(
                name: "FK_BattleLineups_Users_PlayerBId",
                table: "BattleLineups");

            migrationBuilder.DropForeignKey(
                name: "FK_BattleRounds_Users_PlayerAId",
                table: "BattleRounds");

            migrationBuilder.DropForeignKey(
                name: "FK_BattleRounds_Users_PlayerBId",
                table: "BattleRounds");

            migrationBuilder.DropForeignKey(
                name: "FK_Battles_TournamentMatches_TournamentMatchId",
                table: "Battles");

            migrationBuilder.DropIndex(
                name: "IX_Battles_TournamentMatchId",
                table: "Battles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_DifferentPlayers",
                table: "Battles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_Scores",
                table: "Battles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_ScoreToWin",
                table: "Battles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Battle_SourceMatch",
                table: "Battles");

            migrationBuilder.DropIndex(
                name: "IX_BattleRounds_PlayerAId",
                table: "BattleRounds");

            migrationBuilder.DropIndex(
                name: "IX_BattleRounds_PlayerBId",
                table: "BattleRounds");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineups_PlayerAId",
                table: "BattleLineups");

            migrationBuilder.DropIndex(
                name: "IX_BattleLineups_PlayerBId",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "ScoreToWin",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "SideADesignation",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "WinningSide",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "PlayerADisplayNameSnapshot",
                table: "BattleRounds");

            migrationBuilder.DropColumn(
                name: "PlayerAId",
                table: "BattleRounds");

            migrationBuilder.DropColumn(
                name: "PlayerBDisplayNameSnapshot",
                table: "BattleRounds");

            migrationBuilder.DropColumn(
                name: "PlayerBId",
                table: "BattleRounds");

            migrationBuilder.DropColumn(
                name: "PlayerADisplayNameSnapshot",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "PlayerAId",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "PlayerBDisplayNameSnapshot",
                table: "BattleLineups");

            migrationBuilder.DropColumn(
                name: "PlayerBId",
                table: "BattleLineups");

            migrationBuilder.RenameColumn(
                name: "SideAScore",
                table: "Battles",
                newName: "PlayerAScore");

            migrationBuilder.RenameColumn(
                name: "SideBScore",
                table: "Battles",
                newName: "PlayerBScore");

            migrationBuilder.AddColumn<int>(
                name: "BattleId",
                table: "TournamentMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE TournamentMatches
                SET BattleId = (
                    SELECT Id FROM Battles WHERE Battles.TournamentMatchId = TournamentMatches.Id
                );
                """);

            migrationBuilder.DropColumn(
                name: "TournamentMatchId",
                table: "Battles");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "Battles");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerBId",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PlayerAId",
                table: "Battles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_BattleId",
                table: "TournamentMatches",
                column: "BattleId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Battle_DifferentPlayers",
                table: "Battles",
                sql: "PlayerAId <> PlayerBId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Battles_BattleId",
                table: "TournamentMatches",
                column: "BattleId",
                principalTable: "Battles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
