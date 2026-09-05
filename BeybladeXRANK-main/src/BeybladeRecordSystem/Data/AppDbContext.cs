using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Beyblade> Beyblades => Set<Beyblade>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartSeries> PartSeries => Set<PartSeries>();
    public DbSet<BeybladeConfiguration> BeybladeConfigurations => Set<BeybladeConfiguration>();
    public DbSet<BeybladeConfigurationPart> BeybladeConfigurationParts => Set<BeybladeConfigurationPart>();
    public DbSet<Battle> Battles => Set<Battle>();
    public DbSet<BattleLineup> BattleLineups => Set<BattleLineup>();
    public DbSet<BattleLineupSelection> BattleLineupSelections => Set<BattleLineupSelection>();
    public DbSet<BattleTeamOrderSelection> BattleTeamOrderSelections => Set<BattleTeamOrderSelection>();
    public DbSet<BattleRound> BattleRounds => Set<BattleRound>();
    public DbSet<BattleRoundEvent> BattleRoundEvents => Set<BattleRoundEvent>();
    public DbSet<BattleRoundRevision> BattleRoundRevisions => Set<BattleRoundRevision>();
    public DbSet<QuickBattleInvitation> QuickBattleInvitations => Set<QuickBattleInvitation>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentEntry> TournamentEntries => Set<TournamentEntry>();
    public DbSet<TournamentEntryMember> TournamentEntryMembers => Set<TournamentEntryMember>();
    public DbSet<TournamentInvitation> TournamentInvitations => Set<TournamentInvitation>();
    public DbSet<TournamentMatch> TournamentMatches => Set<TournamentMatch>();
    public DbSet<TournamentMatchParticipant> TournamentMatchParticipants => Set<TournamentMatchParticipant>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeUserIdentities();
        ProtectConfigurations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeUserIdentities();
        ProtectConfigurations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ProtectConfigurations()
    {
        foreach (var entry in ChangeTracker.Entries<Beyblade>())
            if (entry.State == EntityState.Modified && entry.Property(x => x.UpperName).IsModified &&
                entry.Property(x => x.UpperName).OriginalValue is not null)
                throw new InvalidOperationException("A Beyblade's assigned upper name is immutable.");
        foreach (var entry in ChangeTracker.Entries<Part>())
            if (entry.State == EntityState.Modified &&
                (entry.Property(x => x.Category).IsModified || entry.Property(x => x.IntegratesRatchet).IsModified))
                throw new InvalidOperationException("A saved part's category and assembly structure are immutable.");
        foreach (var entry in ChangeTracker.Entries<BeybladeConfiguration>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Saved Beyblade configurations are immutable.");
        foreach (var entry in ChangeTracker.Entries<BeybladeConfigurationPart>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Saved configuration parts are immutable.");
            if (entry.State == EntityState.Added &&
                (entry.Entity.Configuration is null || Entry(entry.Entity.Configuration).State != EntityState.Added))
                throw new InvalidOperationException("Parts must be saved together with a new complete configuration.");
        }
    }

    private void NormalizeUserIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<User>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.Account = entry.Entity.Account.Trim();
            entry.Entity.DisplayName = entry.Entity.DisplayName.Trim();
            entry.Entity.NormalizedAccount = IdentityNormalizer.Normalize(entry.Entity.Account);
            entry.Entity.NormalizedDisplayName = IdentityNormalizer.Normalize(entry.Entity.DisplayName);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        PartModelConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.NormalizedAccount).IsUnique();
            entity.HasIndex(x => x.NormalizedDisplayName).IsUnique();
            entity.Property(x => x.Account).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedAccount).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedDisplayName).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Beyblade>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.User).WithMany(x => x.Beyblades).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Battle>(entity =>
        {
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Battle_DifferentPlayers", "\"PlayerAId\" IS NULL OR \"PlayerBId\" IS NULL OR \"PlayerAId\" <> \"PlayerBId\"");
                t.HasCheckConstraint("CK_Battle_ScoreToWin", "\"ScoreToWin\" > 0");
                t.HasCheckConstraint("CK_Battle_Scores", "\"SideAScore\" >= 0 AND \"SideBScore\" >= 0");
                t.HasCheckConstraint("CK_Battle_LineupSequenceNo", "\"LineupSequenceNo\" > 0");
                t.HasCheckConstraint("CK_Battle_SourceMatch", "(\"SourceType\" = 0 AND \"Status\" <> 7 AND \"TournamentMatchId\" IS NULL AND \"VoidedTournamentMatchId\" IS NULL) OR (\"SourceType\" IN (1, 2) AND ((\"Status\" <> 7 AND \"TournamentMatchId\" IS NOT NULL AND \"VoidedTournamentMatchId\" IS NULL) OR (\"Status\" = 7 AND \"TournamentMatchId\" IS NULL AND \"VoidedTournamentMatchId\" IS NOT NULL AND \"VoidedByUserId\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND LENGTH(TRIM(\"VoidReason\")) > 0 AND \"VoidSnapshot\" IS NOT NULL)))");
            });
            entity.Property(x => x.VoidReason).HasMaxLength(500);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.PlayerA).WithMany().HasForeignKey(x => x.PlayerAId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerB).WithMany().HasForeignKey(x => x.PlayerBId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.VoidedByUser).WithMany().HasForeignKey(x => x.VoidedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TournamentMatch).WithOne(x => x.Battle).HasForeignKey<Battle>(x => x.TournamentMatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.VoidedTournamentMatch).WithMany(x => x.VoidedBattles).HasForeignKey(x => x.VoidedTournamentMatchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(500).IsRequired();
            entity.Property(x => x.TargetUrl).HasMaxLength(500).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(64);
            entity.Property(x => x.DedupeKey).HasMaxLength(200);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique()
                .HasFilter("\"ResolvedAtUtc\" IS NULL AND \"DedupeKey\" IS NOT NULL");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuickBattleInvitation>(entity =>
        {
            entity.HasIndex(x => new { x.InviteeUserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.InviterUserId, x.InviteeUserId }).IsUnique();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.InviterUser).WithMany().HasForeignKey(x => x.InviterUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InviteeUser).WithMany().HasForeignKey(x => x.InviteeUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleLineup>(entity =>
        {
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.PositionNo }).IsUnique();
            entity.Property(x => x.PlayerADisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PlayerBDisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.Battle).WithMany(x => x.Lineups).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PlayerA).WithMany().HasForeignKey(x => x.PlayerAId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerABeyblade).WithMany().HasForeignKey(x => x.PlayerABeybladeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerB).WithMany().HasForeignKey(x => x.PlayerBId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerBBeyblade).WithMany().HasForeignKey(x => x.PlayerBBeybladeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleLineupSelection>(entity =>
        {
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_BattleLineupSelection_PositionNo", "\"PositionNo\" > 0");
                t.HasCheckConstraint("CK_BattleLineupSelection_SequenceNo", "\"SequenceNo\" > 0");
            });
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.UserId, x.PositionNo }).IsUnique();
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.BeybladeId }).IsUnique();
            entity.Property(x => x.PlayerDisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BeybladeNameSnapshot).HasMaxLength(520).IsRequired();
            entity.HasOne(x => x.Battle).WithMany(x => x.LineupSelections).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Beyblade).WithMany().HasForeignKey(x => x.BeybladeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleTeamOrderSelection>(entity =>
        {
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_BattleTeamOrderSelection_PositionNo", "\"PositionNo\" > 0");
                t.HasCheckConstraint("CK_BattleTeamOrderSelection_SequenceNo", "\"SequenceNo\" > 0");
            });
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.TournamentEntryId, x.PositionNo }).IsUnique();
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.TournamentEntryId, x.UserId }).IsUnique();
            entity.HasOne(x => x.Battle).WithMany(x => x.TeamOrderSelections).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TournamentEntry).WithMany().HasForeignKey(x => x.TournamentEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleRound>(entity =>
        {
            entity.HasIndex(x => new { x.BattleId, x.RoundNo }).IsUnique();
            entity.Property(x => x.PlayerADisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PlayerBDisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.Battle).WithMany(x => x.Rounds).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Lineup).WithMany().HasForeignKey(x => x.LineupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerA).WithMany().HasForeignKey(x => x.PlayerAId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerB).WithMany().HasForeignKey(x => x.PlayerBId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleRoundEvent>(entity =>
        {
            entity.HasIndex(x => new { x.BattleRoundId, x.EventSequence }).IsUnique();
            entity.HasOne(x => x.BattleRound).WithMany(x => x.Events).HasForeignKey(x => x.BattleRoundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BattleRoundRevision>(entity =>
        {
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.PreviousBattleSnapshot).IsRequired();
            entity.Property(x => x.NewBattleSnapshot).IsRequired();
            entity.HasOne(x => x.BattleRound).WithMany(x => x.Revisions).HasForeignKey(x => x.BattleRoundId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Tournament_TargetEntryCount", "\"TargetEntryCount\" BETWEEN 2 AND 512");
                t.HasCheckConstraint("CK_Tournament_ScoreToWin", "\"ScoreToWin\" > 0");
                t.HasCheckConstraint("CK_Tournament_BeybladesPerPlayer", "\"BeybladesPerPlayer\" > 0");
                t.HasCheckConstraint("CK_Tournament_TeamSize", "(\"Mode\" = 0 AND \"TeamSize\" IS NULL) OR (\"Mode\" = 1 AND \"TeamSize\" IN (2, 3))");
            });
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.RulesSnapshot).IsRequired();
            entity.Property(x => x.CancellationReason).HasMaxLength(500);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.OrganizerUserId, x.UpdatedAtUtc });
            entity.HasOne(x => x.OrganizerUser).WithMany().HasForeignKey(x => x.OrganizerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TournamentEntry>(entity =>
        {
            entity.Property(x => x.RegistrationNumber).HasMaxLength(32);
            entity.Property(x => x.DisplayNameSnapshot).HasMaxLength(192).IsRequired();
            entity.Property(x => x.TeamName).HasMaxLength(100);
            entity.HasIndex(x => new { x.TournamentId, x.RegistrationNumber }).IsUnique();
            entity.HasIndex(x => new { x.TournamentId, x.SchedulePosition }).IsUnique();
            entity.HasIndex(x => new { x.TournamentId, x.IndividualUserId }).IsUnique();
            entity.HasOne(x => x.Tournament).WithMany(x => x.Entries).HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.IndividualUser).WithMany().HasForeignKey(x => x.IndividualUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TournamentEntryMember>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint("CK_TournamentEntryMember_MemberOrder", "\"MemberOrder\" > 0"));
            entity.Property(x => x.DisplayNameSnapshot).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.TournamentId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TournamentEntryId, x.MemberOrder }).IsUnique();
            entity.HasOne(x => x.Tournament).WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TournamentEntry).WithMany(x => x.Members).HasForeignKey(x => x.TournamentEntryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TournamentInvitation>(entity =>
        {
            entity.HasIndex(x => new { x.InvitedUserId, x.Status, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.TournamentId, x.Status });
            entity.HasOne(x => x.Tournament).WithMany(x => x.Invitations).HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TournamentEntry).WithMany().HasForeignKey(x => x.TournamentEntryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.InvitedUser).WithMany().HasForeignKey(x => x.InvitedUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InvitedByUser).WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TournamentMatch>(entity =>
        {
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TournamentMatch_RoundNumber", "\"RoundNumber\" > 0");
                t.HasCheckConstraint("CK_TournamentMatch_MatchNumber", "\"MatchNumber\" > 0");
                t.HasCheckConstraint("CK_TournamentMatch_SequenceNumber", "\"SequenceNumber\" > 0");
                t.HasCheckConstraint("CK_TournamentMatch_SideAReference", "\"SideASourceReferenceId\" > 0");
                t.HasCheckConstraint("CK_TournamentMatch_ByeSide", "NOT \"IsBye\" OR \"SideBSourceReferenceId\" IS NULL");
            });
            entity.Property(x => x.ResolutionReason).HasMaxLength(500);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.TournamentId, x.SequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.TournamentId, x.Bracket, x.RoundNumber, x.MatchNumber }).IsUnique();
            entity.HasIndex(x => new { x.TournamentId, x.Status });
            entity.HasOne(x => x.Tournament).WithMany(x => x.Matches).HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SideAEntry).WithMany().HasForeignKey(x => x.SideAEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SideBEntry).WithMany().HasForeignKey(x => x.SideBEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.WinnerEntry).WithMany().HasForeignKey(x => x.WinnerEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LoserEntry).WithMany().HasForeignKey(x => x.LoserEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.WinnerToMatch).WithMany().HasForeignKey(x => x.WinnerToMatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LoserToMatch).WithMany().HasForeignKey(x => x.LoserToMatchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TournamentMatchParticipant>(entity =>
        {
            entity.HasIndex(x => new { x.TournamentMatchId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TournamentMatchId, x.TournamentEntryId, x.UserId }).IsUnique();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.TournamentMatch).WithMany(x => x.Participants).HasForeignKey(x => x.TournamentMatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TournamentEntry).WithMany().HasForeignKey(x => x.TournamentEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

    }
}
