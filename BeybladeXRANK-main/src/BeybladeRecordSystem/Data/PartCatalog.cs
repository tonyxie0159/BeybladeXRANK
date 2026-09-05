using BeybladeRecordSystem.Domain.Entities;
using BeybladeRecordSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Data;

public sealed record PartCatalogEntry(PartCategory Category, string Name, bool IntegratesRatchet, PartSystemSeries[] Series);

public static class PartCatalog
{
    // This reviewed, offline catalog is the approved 2026-09-04 name list, not a live scraper.
    public static IReadOnlyList<PartCatalogEntry> Read()
    {
        using var stream = typeof(PartCatalog).Assembly.GetManifestResourceStream(
            "BeybladeRecordSystem.Data.parts-catalog.tsv")
            ?? throw new InvalidOperationException("The embedded parts catalog is missing.");
        using var reader = new StreamReader(stream);
        reader.ReadLine();
        var entries = new List<PartCatalogEntry>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split('\t');
            if (fields.Length != 4) throw new InvalidOperationException("Invalid parts catalog row.");
            var entry = new PartCatalogEntry(Enum.Parse<PartCategory>(fields[0]), fields[1].Trim(),
                bool.Parse(fields[2]), fields[3].Split(',').Select(Enum.Parse<PartSystemSeries>).Distinct().ToArray());
            if (!Enum.IsDefined(entry.Category) || entry.Name.Length is 0 or > 100 ||
                entry.Series.Length == 0 || entry.Series.Any(x => !Enum.IsDefined(x)) ||
                (entry.IntegratesRatchet && entry.Category is not (PartCategory.Blade or PartCategory.Bit)))
                throw new InvalidOperationException("Invalid parts catalog entry.");
            entries.Add(entry);
        }
        if (entries.Select(x => (x.Category, x.Name)).Distinct().Count() != entries.Count)
            throw new InvalidOperationException("The catalog contains duplicate category/name pairs.");
        return entries;
    }

    public static async Task<int> ImportAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var entries = Read();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2790904)", cancellationToken);
        var existing = (await db.Parts.Include(x => x.Series).ToListAsync(cancellationToken))
            .ToDictionary(x => (x.Category, x.Name));
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var entry in entries)
        {
            if (!existing.TryGetValue((entry.Category, entry.Name), out var part))
            {
                part = new Part
                {
                    Category = entry.Category, Name = entry.Name, IntegratesRatchet = entry.IntegratesRatchet,
                    IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
                };
                db.Parts.Add(part);
                added++;
            }
            else if (part.IntegratesRatchet != entry.IntegratesRatchet)
            {
                throw new InvalidOperationException($"Catalog structure conflicts with saved part: {entry.Category}/{entry.Name}.");
            }
            foreach (var series in entry.Series)
                if (!part.Series.Any(x => x.Series == series))
                    part.Series.Add(new PartSeries { Series = series });
        }
        // Existing IsActive and names are never overwritten by a deployment/import.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return added;
    }
}
