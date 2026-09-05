using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Data;

internal static class PartModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Beyblade>(entity =>
        {
            entity.Ignore(x => x.Configuration);
            entity.Property(x => x.UpperName).HasMaxLength(200);
            entity.HasIndex(x => new { x.UserId, x.UpperName }).IsUnique()
                .HasFilter("\"UpperName\" IS NOT NULL AND NOT \"IsDeleted\"");
        });
        builder.Entity<Part>(entity =>
        {
            entity.HasIndex(x => new { x.Category, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Part_Category", "\"Category\" BETWEEN 0 AND 7");
                t.HasCheckConstraint("CK_Part_Name", "LENGTH(TRIM(\"Name\")) > 0 AND \"Name\" = TRIM(\"Name\")");
                t.HasCheckConstraint("CK_Part_IntegratesRatchet", "NOT \"IntegratesRatchet\" OR \"Category\" IN (0, 2)");
            });
        });
        builder.Entity<PartSeries>(entity =>
        {
            entity.HasKey(x => new { x.PartId, x.Series });
            entity.ToTable(t => t.HasCheckConstraint("CK_PartSeries_Series", "\"Series\" BETWEEN 0 AND 2"));
            entity.HasOne(x => x.Part).WithMany(x => x.Series).HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BeybladeConfiguration>(entity =>
        {
            entity.HasIndex(x => new { x.BeybladeId, x.VersionNo }).IsUnique();
            entity.HasIndex(x => new { x.BeybladeId, x.PartsKey }).IsUnique();
            entity.Property(x => x.PartsKey).HasMaxLength(65).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_Configuration_Version", "\"VersionNo\" > 0"));
            // Composite references prevent a lineup from attaching another Beyblade's configuration.
            entity.HasAlternateKey(x => new { x.Id, x.BeybladeId });
            entity.HasOne(x => x.Beyblade).WithMany(x => x.Configurations)
                .HasForeignKey(x => x.BeybladeId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BeybladeConfigurationPart>(entity =>
        {
            entity.HasKey(x => new { x.ConfigurationId, x.PartId });
            entity.Property(x => x.PartNameSnapshot).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.Configuration).WithMany(x => x.Parts)
                .HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BattleLineupSelection>().HasOne(x => x.BeybladeConfiguration).WithMany()
            .HasForeignKey(x => new { x.BeybladeConfigurationId, x.BeybladeId })
            .HasPrincipalKey(x => new { x.Id, x.BeybladeId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BattleLineup>().HasOne(x => x.PlayerAConfiguration).WithMany()
            .HasForeignKey(x => new { x.PlayerAConfigurationId, x.PlayerABeybladeId })
            .HasPrincipalKey(x => new { x.Id, x.BeybladeId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BattleLineup>().HasOne(x => x.PlayerBConfiguration).WithMany()
            .HasForeignKey(x => new { x.PlayerBConfigurationId, x.PlayerBBeybladeId })
            .HasPrincipalKey(x => new { x.Id, x.BeybladeId }).OnDelete(DeleteBehavior.Restrict);
    }
}
