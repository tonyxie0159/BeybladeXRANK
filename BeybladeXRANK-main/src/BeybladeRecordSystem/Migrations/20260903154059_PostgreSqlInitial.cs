using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BeybladeRecordSystem.Migrations
{
    /// <inheritdoc />
    public partial class PostgreSqlInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Account = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedAccount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedDisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Beyblades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "QuickBattleInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InviterUserId = table.Column<int>(type: "integer", nullable: false),
                    InviteeUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickBattleInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickBattleInvitations_Users_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickBattleInvitations_Users_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    RegistrationMode = table.Column<int>(type: "integer", nullable: false),
                    RuleSet = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RegistrationStage = table.Column<int>(type: "integer", nullable: false),
                    TeamSize = table.Column<int>(type: "integer", nullable: true),
                    BeybladesPerPlayer = table.Column<int>(type: "integer", nullable: false),
                    ScoreToWin = table.Column<int>(type: "integer", nullable: false),
                    TargetEntryCount = table.Column<int>(type: "integer", nullable: false),
                    OrganizerUserId = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RulesSnapshot = table.Column<string>(type: "text", nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistrationClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                    table.CheckConstraint("CK_Tournament_BeybladesPerPlayer", "\"BeybladesPerPlayer\" > 0");
                    table.CheckConstraint("CK_Tournament_ScoreToWin", "\"ScoreToWin\" > 0");
                    table.CheckConstraint("CK_Tournament_TargetEntryCount", "\"TargetEntryCount\" BETWEEN 2 AND 512");
                    table.CheckConstraint("CK_Tournament_TeamSize", "(\"Mode\" = 0 AND \"TeamSize\" IS NULL) OR (\"Mode\" = 1 AND \"TeamSize\" IN (2, 3))");
                    table.ForeignKey(
                        name: "FK_Tournaments_Users_OrganizerUserId",
                        column: x => x.OrganizerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    ActionEntityId = table.Column<int>(type: "integer", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "TournamentEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SchedulePosition = table.Column<int>(type: "integer", nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    TeamName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IndividualUserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Users_IndividualUserId",
                        column: x => x.IndividualUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentEntryMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MemberOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRepresentative = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntryMembers", x => x.Id);
                    table.CheckConstraint("CK_TournamentEntryMember_MemberOrder", "\"MemberOrder\" > 0");
                    table.ForeignKey(
                        name: "FK_TournamentEntryMembers_TournamentEntries_TournamentEntryId",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntryMembers_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntryMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "integer", nullable: true),
                    InvitedUserId = table.Column<int>(type: "integer", nullable: false),
                    InvitedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentInvitations_TournamentEntries_TournamentEntryId",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentInvitations_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentInvitations_Users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentInvitations_Users_InvitedUserId",
                        column: x => x.InvitedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    Bracket = table.Column<int>(type: "integer", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    MatchNumber = table.Column<int>(type: "integer", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SideASourceKind = table.Column<int>(type: "integer", nullable: false),
                    SideASourceReferenceId = table.Column<int>(type: "integer", nullable: false),
                    SideBSourceKind = table.Column<int>(type: "integer", nullable: true),
                    SideBSourceReferenceId = table.Column<int>(type: "integer", nullable: true),
                    SideAEntryId = table.Column<int>(type: "integer", nullable: true),
                    SideBEntryId = table.Column<int>(type: "integer", nullable: true),
                    WinnerEntryId = table.Column<int>(type: "integer", nullable: true),
                    LoserEntryId = table.Column<int>(type: "integer", nullable: true),
                    WinnerToMatchId = table.Column<int>(type: "integer", nullable: true),
                    LoserToMatchId = table.Column<int>(type: "integer", nullable: true),
                    IsBye = table.Column<bool>(type: "boolean", nullable: false),
                    IsSeedQualifier = table.Column<bool>(type: "boolean", nullable: false),
                    IsResetFinal = table.Column<bool>(type: "boolean", nullable: false),
                    ResolutionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatches", x => x.Id);
                    table.CheckConstraint("CK_TournamentMatch_ByeSide", "NOT \"IsBye\" OR \"SideBSourceReferenceId\" IS NULL");
                    table.CheckConstraint("CK_TournamentMatch_MatchNumber", "\"MatchNumber\" > 0");
                    table.CheckConstraint("CK_TournamentMatch_RoundNumber", "\"RoundNumber\" > 0");
                    table.CheckConstraint("CK_TournamentMatch_SequenceNumber", "\"SequenceNumber\" > 0");
                    table.CheckConstraint("CK_TournamentMatch_SideAReference", "\"SideASourceReferenceId\" > 0");
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentEntries_LoserEntryId",
                        column: x => x.LoserEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentEntries_SideAEntryId",
                        column: x => x.SideAEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentEntries_SideBEntryId",
                        column: x => x.SideBEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentEntries_WinnerEntryId",
                        column: x => x.WinnerEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentMatches_LoserToMatchId",
                        column: x => x.LoserToMatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_TournamentMatches_WinnerToMatchId",
                        column: x => x.WinnerToMatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Battles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    ScoreToWin = table.Column<int>(type: "integer", nullable: false),
                    TournamentMatchId = table.Column<int>(type: "integer", nullable: true),
                    VoidedTournamentMatchId = table.Column<int>(type: "integer", nullable: true),
                    PlayerAId = table.Column<int>(type: "integer", nullable: true),
                    PlayerBId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SideAScore = table.Column<int>(type: "integer", nullable: false),
                    SideBScore = table.Column<int>(type: "integer", nullable: false),
                    SideADesignation = table.Column<int>(type: "integer", nullable: true),
                    WinningSide = table.Column<int>(type: "integer", nullable: true),
                    WinningPlayerId = table.Column<int>(type: "integer", nullable: true),
                    LineupSequenceNo = table.Column<int>(type: "integer", nullable: false),
                    PlayerALineupConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PlayerBLineupConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PlayerAEditRequestUsed = table.Column<bool>(type: "boolean", nullable: false),
                    PlayerBEditRequestUsed = table.Column<bool>(type: "boolean", nullable: false),
                    PendingLineupEditRequestedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VoidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VoidSnapshot = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Battles", x => x.Id);
                    table.CheckConstraint("CK_Battle_DifferentPlayers", "\"PlayerAId\" IS NULL OR \"PlayerBId\" IS NULL OR \"PlayerAId\" <> \"PlayerBId\"");
                    table.CheckConstraint("CK_Battle_LineupSequenceNo", "\"LineupSequenceNo\" > 0");
                    table.CheckConstraint("CK_Battle_Scores", "\"SideAScore\" >= 0 AND \"SideBScore\" >= 0");
                    table.CheckConstraint("CK_Battle_ScoreToWin", "\"ScoreToWin\" > 0");
                    table.CheckConstraint("CK_Battle_SourceMatch", "(\"SourceType\" = 0 AND \"Status\" <> 7 AND \"TournamentMatchId\" IS NULL AND \"VoidedTournamentMatchId\" IS NULL) OR (\"SourceType\" IN (1, 2) AND ((\"Status\" <> 7 AND \"TournamentMatchId\" IS NOT NULL AND \"VoidedTournamentMatchId\" IS NULL) OR (\"Status\" = 7 AND \"TournamentMatchId\" IS NULL AND \"VoidedTournamentMatchId\" IS NOT NULL AND \"VoidedByUserId\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND LENGTH(TRIM(\"VoidReason\")) > 0 AND \"VoidSnapshot\" IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_Battles_TournamentMatches_TournamentMatchId",
                        column: x => x.TournamentMatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Battles_TournamentMatches_VoidedTournamentMatchId",
                        column: x => x.VoidedTournamentMatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    table.ForeignKey(
                        name: "FK_Battles_Users_VoidedByUserId",
                        column: x => x.VoidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TournamentMatchParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentMatchId = table.Column<int>(type: "integer", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsMatchRepresentative = table.Column<bool>(type: "boolean", nullable: false),
                    LineupConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LineupConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatchParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatchParticipants_TournamentEntries_TournamentEnt~",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatchParticipants_TournamentMatches_TournamentMat~",
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

            migrationBuilder.CreateTable(
                name: "BattleLineups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleId = table.Column<int>(type: "integer", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    PositionNo = table.Column<int>(type: "integer", nullable: false),
                    PlayerAId = table.Column<int>(type: "integer", nullable: true),
                    PlayerADisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlayerABeybladeId = table.Column<int>(type: "integer", nullable: false),
                    PlayerABeybladeNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    PlayerBId = table.Column<int>(type: "integer", nullable: true),
                    PlayerBDisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlayerBBeybladeId = table.Column<int>(type: "integer", nullable: false),
                    PlayerBBeybladeNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
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
                    table.ForeignKey(
                        name: "FK_BattleLineups_Users_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleLineups_Users_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BattleLineupSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleId = table.Column<int>(type: "integer", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PositionNo = table.Column<int>(type: "integer", nullable: false),
                    BeybladeId = table.Column<int>(type: "integer", nullable: false),
                    PlayerDisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeybladeNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleLineupSelections", x => x.Id);
                    table.CheckConstraint("CK_BattleLineupSelection_PositionNo", "\"PositionNo\" > 0");
                    table.CheckConstraint("CK_BattleLineupSelection_SequenceNo", "\"SequenceNo\" > 0");
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
                name: "BattleTeamOrderSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleId = table.Column<int>(type: "integer", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    TournamentEntryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PositionNo = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleTeamOrderSelections", x => x.Id);
                    table.CheckConstraint("CK_BattleTeamOrderSelection_PositionNo", "\"PositionNo\" > 0");
                    table.CheckConstraint("CK_BattleTeamOrderSelection_SequenceNo", "\"SequenceNo\" > 0");
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_Battles_BattleId",
                        column: x => x.BattleId,
                        principalTable: "Battles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BattleTeamOrderSelections_TournamentEntries_TournamentEntry~",
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

            migrationBuilder.CreateTable(
                name: "BattleRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleId = table.Column<int>(type: "integer", nullable: false),
                    LineupId = table.Column<int>(type: "integer", nullable: false),
                    RoundNo = table.Column<int>(type: "integer", nullable: false),
                    PositionNo = table.Column<int>(type: "integer", nullable: false),
                    PlayerAId = table.Column<int>(type: "integer", nullable: true),
                    PlayerADisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlayerABeybladeId = table.Column<int>(type: "integer", nullable: false),
                    PlayerABeybladeNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    PlayerBId = table.Column<int>(type: "integer", nullable: true),
                    PlayerBDisplayNameSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlayerBBeybladeId = table.Column<int>(type: "integer", nullable: false),
                    PlayerBBeybladeNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    table.ForeignKey(
                        name: "FK_BattleRounds_Users_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BattleRounds_Users_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BattleRoundEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleRoundId = table.Column<int>(type: "integer", nullable: false),
                    EventSequence = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    ActorPlayerId = table.Column<int>(type: "integer", nullable: true),
                    WinnerPlayerId = table.Column<int>(type: "integer", nullable: true),
                    ResultType = table.Column<int>(type: "integer", nullable: true),
                    ScoreAwarded = table.Column<int>(type: "integer", nullable: false),
                    IsEffective = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidationReason = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BattleRoundId = table.Column<int>(type: "integer", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PreviousEffectiveEventSnapshot = table.Column<string>(type: "text", nullable: false),
                    NewEffectiveEventSnapshot = table.Column<string>(type: "text", nullable: false),
                    PreviousBattleSnapshot = table.Column<string>(type: "text", nullable: false),
                    NewBattleSnapshot = table.Column<string>(type: "text", nullable: false)
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
                name: "IX_BattleLineups_PlayerAId",
                table: "BattleLineups",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerBBeybladeId",
                table: "BattleLineups",
                column: "PlayerBBeybladeId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineups_PlayerBId",
                table: "BattleLineups",
                column: "PlayerBId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_BeybladeId",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "SequenceNo", "BeybladeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleLineupSelections_BattleId_SequenceNo_UserId_PositionNo",
                table: "BattleLineupSelections",
                columns: new[] { "BattleId", "SequenceNo", "UserId", "PositionNo" },
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
                name: "IX_BattleRounds_PlayerAId",
                table: "BattleRounds",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRounds_PlayerBId",
                table: "BattleRounds",
                column: "PlayerBId");

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
                name: "IX_Battles_TournamentMatchId",
                table: "Battles",
                column: "TournamentMatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Battles_VoidedByUserId",
                table: "Battles",
                column: "VoidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Battles_VoidedTournamentMatchId",
                table: "Battles",
                column: "VoidedTournamentMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEn~1",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "SequenceNo", "TournamentEntryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BattleTeamOrderSelections_BattleId_SequenceNo_TournamentEnt~",
                table: "BattleTeamOrderSelections",
                columns: new[] { "BattleId", "SequenceNo", "TournamentEntryId", "PositionNo" },
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

            migrationBuilder.CreateIndex(
                name: "IX_Beyblades_UserId_Name",
                table: "Beyblades",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuickBattleInvitations_InviteeUserId_CreatedAtUtc",
                table: "QuickBattleInvitations",
                columns: new[] { "InviteeUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QuickBattleInvitations_InviterUserId_InviteeUserId",
                table: "QuickBattleInvitations",
                columns: new[] { "InviterUserId", "InviteeUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_IndividualUserId",
                table: "TournamentEntries",
                column: "IndividualUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId_IndividualUserId",
                table: "TournamentEntries",
                columns: new[] { "TournamentId", "IndividualUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId_RegistrationNumber",
                table: "TournamentEntries",
                columns: new[] { "TournamentId", "RegistrationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId_SchedulePosition",
                table: "TournamentEntries",
                columns: new[] { "TournamentId", "SchedulePosition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntryMembers_TournamentEntryId_MemberOrder",
                table: "TournamentEntryMembers",
                columns: new[] { "TournamentEntryId", "MemberOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntryMembers_TournamentId_UserId",
                table: "TournamentEntryMembers",
                columns: new[] { "TournamentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntryMembers_UserId",
                table: "TournamentEntryMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvitations_InvitedByUserId",
                table: "TournamentInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvitations_InvitedUserId_Status_CreatedAtUtc",
                table: "TournamentInvitations",
                columns: new[] { "InvitedUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvitations_TournamentEntryId",
                table: "TournamentInvitations",
                column: "TournamentEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvitations_TournamentId_Status",
                table: "TournamentInvitations",
                columns: new[] { "TournamentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_LoserEntryId",
                table: "TournamentMatches",
                column: "LoserEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_LoserToMatchId",
                table: "TournamentMatches",
                column: "LoserToMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_SideAEntryId",
                table: "TournamentMatches",
                column: "SideAEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_SideBEntryId",
                table: "TournamentMatches",
                column: "SideBEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_Bracket_RoundNumber_MatchNum~",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "Bracket", "RoundNumber", "MatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_SequenceNumber",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_Status",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_WinnerEntryId",
                table: "TournamentMatches",
                column: "WinnerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_WinnerToMatchId",
                table: "TournamentMatches",
                column: "WinnerToMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_TournamentEntryId",
                table: "TournamentMatchParticipants",
                column: "TournamentEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchParticipants_TournamentMatchId_TournamentEnt~",
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

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_OrganizerUserId_UpdatedAtUtc",
                table: "Tournaments",
                columns: new[] { "OrganizerUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_Status_UpdatedAtUtc",
                table: "Tournaments",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_CreatedAtUtc",
                table: "UserNotifications",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_DedupeKey",
                table: "UserNotifications",
                columns: new[] { "UserId", "DedupeKey" },
                unique: true,
                filter: "\"ResolvedAtUtc\" IS NULL AND \"DedupeKey\" IS NOT NULL");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleLineupSelections");

            migrationBuilder.DropTable(
                name: "BattleRoundEvents");

            migrationBuilder.DropTable(
                name: "BattleRoundRevisions");

            migrationBuilder.DropTable(
                name: "BattleTeamOrderSelections");

            migrationBuilder.DropTable(
                name: "QuickBattleInvitations");

            migrationBuilder.DropTable(
                name: "TournamentEntryMembers");

            migrationBuilder.DropTable(
                name: "TournamentInvitations");

            migrationBuilder.DropTable(
                name: "TournamentMatchParticipants");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "BattleRounds");

            migrationBuilder.DropTable(
                name: "BattleLineups");

            migrationBuilder.DropTable(
                name: "Battles");

            migrationBuilder.DropTable(
                name: "Beyblades");

            migrationBuilder.DropTable(
                name: "TournamentMatches");

            migrationBuilder.DropTable(
                name: "TournamentEntries");

            migrationBuilder.DropTable(
                name: "Tournaments");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
