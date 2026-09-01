using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class RepairLegacyIdentityConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH RankedAccounts AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY UPPER(TRIM(Account)) ORDER BY Id) AS DuplicateNo
                    FROM Users
                )
                UPDATE Users
                SET Account = SUBSTR(TRIM(Account), 1, MAX(1, 63 - LENGTH(CAST(Id AS TEXT)))) || '-' || Id
                WHERE Id IN (SELECT Id FROM RankedAccounts WHERE DuplicateNo > 1);

                WITH RankedDisplayNames AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY UPPER(TRIM(DisplayName)) ORDER BY Id) AS DuplicateNo
                    FROM Users
                )
                UPDATE Users
                SET DisplayName = SUBSTR(TRIM(DisplayName), 1, MAX(1, 62 - LENGTH(CAST(Id AS TEXT)))) || ' #' || Id
                WHERE Id IN (SELECT Id FROM RankedDisplayNames WHERE DuplicateNo > 1);

                UPDATE Users
                SET NormalizedAccount = CHAR(31) || 'ACCOUNT:' || Id,
                    NormalizedDisplayName = CHAR(31) || 'DISPLAY:' || Id;

                UPDATE Users
                SET Account = TRIM(Account),
                    DisplayName = TRIM(DisplayName),
                    NormalizedAccount = UPPER(TRIM(Account)),
                    NormalizedDisplayName = UPPER(TRIM(DisplayName));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original conflicting values cannot be reconstructed safely.
        }
    }
}
