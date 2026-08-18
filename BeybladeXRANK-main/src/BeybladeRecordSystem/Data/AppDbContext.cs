using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Beyblade> Beyblades => Set<Beyblade>();
    public DbSet<Battle> Battles => Set<Battle>();
    public DbSet<BattleLineup> BattleLineups => Set<BattleLineup>();
    public DbSet<BattleRound> BattleRounds => Set<BattleRound>();
    public DbSet<BattleRoundEvent> BattleRoundEvents => Set<BattleRoundEvent>();
    public DbSet<BattleRoundRevision> BattleRoundRevisions => Set<BattleRoundRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.Account).IsUnique();
            entity.Property(x => x.Account).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Beyblade>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.User).WithMany(x => x.Beyblades).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Battle>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint("CK_Battle_DifferentPlayers", "PlayerAId <> PlayerBId"));
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.PlayerA).WithMany().HasForeignKey(x => x.PlayerAId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerB).WithMany().HasForeignKey(x => x.PlayerBId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleLineup>(entity =>
        {
            entity.HasIndex(x => new { x.BattleId, x.SequenceNo, x.PositionNo }).IsUnique();
            entity.HasOne(x => x.Battle).WithMany(x => x.Lineups).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PlayerABeyblade).WithMany().HasForeignKey(x => x.PlayerABeybladeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PlayerBBeyblade).WithMany().HasForeignKey(x => x.PlayerBBeybladeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleRound>(entity =>
        {
            entity.HasIndex(x => new { x.BattleId, x.RoundNo }).IsUnique();
            entity.HasOne(x => x.Battle).WithMany(x => x.Rounds).HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Lineup).WithMany().HasForeignKey(x => x.LineupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BattleRoundEvent>(entity =>
        {
            entity.HasIndex(x => new { x.BattleRoundId, x.EventSequence }).IsUnique();
            entity.HasOne(x => x.BattleRound).WithMany(x => x.Events).HasForeignKey(x => x.BattleRoundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BattleRoundRevision>(entity =>
        {
            entity.HasOne(x => x.BattleRound).WithMany(x => x.Revisions).HasForeignKey(x => x.BattleRoundId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

    }
}
