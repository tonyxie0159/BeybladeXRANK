using BeybladeRecordSystem.Data;
using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.DataMigration;

// Explicit projections keep the frozen legacy SQLite format readable after new
// PostgreSQL-only snapshot columns are added to the current EF model.
public static class LegacyLineupReader
{
    public static Task<List<Beyblade>> ReadBeybladesAsync(AppDbContext db) =>
        db.Beyblades.AsNoTracking().OrderBy(x => x.Id).Select(x => new Beyblade
        {
            Id = x.Id, UserId = x.UserId, Name = x.Name, IsDeleted = x.IsDeleted,
            CreatedAtUtc = x.CreatedAtUtc, UpdatedAtUtc = x.UpdatedAtUtc
        }).ToListAsync();

    public static Task<List<BattleLineup>> ReadLineupsAsync(AppDbContext db) =>
        db.BattleLineups.AsNoTracking().OrderBy(x => x.Id).Select(x => new BattleLineup
        {
            Id = x.Id, BattleId = x.BattleId, SequenceNo = x.SequenceNo, PositionNo = x.PositionNo,
            PlayerAId = x.PlayerAId, PlayerADisplayNameSnapshot = x.PlayerADisplayNameSnapshot,
            PlayerABeybladeId = x.PlayerABeybladeId, PlayerABeybladeNameSnapshot = x.PlayerABeybladeNameSnapshot,
            PlayerBId = x.PlayerBId, PlayerBDisplayNameSnapshot = x.PlayerBDisplayNameSnapshot,
            PlayerBBeybladeId = x.PlayerBBeybladeId, PlayerBBeybladeNameSnapshot = x.PlayerBBeybladeNameSnapshot,
            IsCurrent = x.IsCurrent
        }).ToListAsync();

    public static Task<List<BattleLineupSelection>> ReadSelectionsAsync(AppDbContext db) =>
        db.BattleLineupSelections.AsNoTracking().OrderBy(x => x.Id).Select(x => new BattleLineupSelection
        {
            Id = x.Id, BattleId = x.BattleId, SequenceNo = x.SequenceNo, UserId = x.UserId,
            PositionNo = x.PositionNo, BeybladeId = x.BeybladeId,
            PlayerDisplayNameSnapshot = x.PlayerDisplayNameSnapshot,
            BeybladeNameSnapshot = x.BeybladeNameSnapshot, SubmittedAtUtc = x.SubmittedAtUtc
        }).ToListAsync();
}
