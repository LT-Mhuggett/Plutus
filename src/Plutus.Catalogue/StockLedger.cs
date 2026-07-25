#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Catalogue
{
    /// <summary>
    /// WP5.1 core: append a typed movement and maintain the materialised level in the same
    /// change set (the caller commits). Level == Σ ledger is the invariant everything else
    /// (property tests, rebuild) protects.
    /// </summary>
    public sealed class StockLedgerService
    {
        private readonly MySqlDbContext _db;
        public StockLedgerService(MySqlDbContext db) => _db = db;

        /// <summary>The store's default STORE-type location, created on first use
        /// (single-location stores get it transparently; warehouses arrive in WP5.2+).</summary>
        public async Task<StockLocation> EnsureStoreLocationAsync(Guid tenantId, int storeId, CancellationToken ct = default)
        {
            var location = await _db.StockLocations.IgnoreQueryFilters().FirstOrDefaultAsync(
                l => l.TenantId == tenantId && l.StoreId == storeId && l.Type == StockLocationType.Store, ct);
            if (location != null) return location;
            location = new StockLocation
            {
                Id = Uuid7.New(), TenantId = tenantId, StoreId = storeId,
                Type = StockLocationType.Store, Name = $"Store {storeId}",
            };
            _db.StockLocations.Add(location);
            return location;
        }

        /// <summary>Adds the movement and applies its delta to the level row (both tracked,
        /// not saved — commit belongs to the caller so consumers stay atomic).</summary>
        public async Task<StockMovement> ApplyAsync(
            Guid tenantId, StockLocation location, string itemIdOne, Guid itemId,
            StockMovementType type, int qtyDelta, string reason, Guid? refId, Guid? actor,
            CancellationToken ct = default)
        {
            var movement = new StockMovement
            {
                Id = Uuid7.New(), TenantId = tenantId, StockLocationId = location.Id,
                ItemIdOne = itemIdOne, ItemId = itemId, Type = type, QtyDelta = qtyDelta,
                Reason = reason, RefId = refId, ActorUserId = actor, AtUtc = DateTime.UtcNow,
            };
            _db.StockMovements.Add(movement);

            var level = await _db.StockLevels.IgnoreQueryFilters().FirstOrDefaultAsync(
                          s => s.TenantId == tenantId && s.StockLocationId == location.Id && s.ItemIdOne == itemIdOne, ct)
                      ?? _db.StockLevels.Local.FirstOrDefault(
                          s => s.TenantId == tenantId && s.StockLocationId == location.Id && s.ItemIdOne == itemIdOne);
            if (level == null)
            {
                level = new StockLevel { TenantId = tenantId, StockLocationId = location.Id, ItemIdOne = itemIdOne };
                _db.StockLevels.Add(level);
            }
            level.Quantity += qtyDelta;
            return movement;
        }
    }

    /// <summary>
    /// WP5.1: folds SaleRecorded into the stock ledger — a SALE movement (−qty) per sold
    /// line, a RETURN movement (+qty) per negative-qty line, at the till's store STORE
    /// location. Same drainer contract as the other consumers (shared scoped context, no
    /// SaveChanges, reads before mutations; ProcessedEvents dedupe = effectively-once).
    /// Lines identify their item via the WP2.1 projection metadata (itemIdOne in
    /// DiscountsJson); lines without it and LegacyRef (ETL) sales are skipped.
    /// NOTE: the Phase-2 legacy bridge still decrements the legacy `Stocks` table in
    /// parallel — the two stay consistent because both fold the same events; the ledger
    /// becomes the single authority when the legacy tables retire.
    /// </summary>
    public sealed class StockProjectionConsumer : IEventConsumer
    {
        public const string ConsumerName = "stock-ledger";
        private readonly MySqlDbContext _db;
        public StockProjectionConsumer(MySqlDbContext db) => _db = db;
        public string Name => ConsumerName;

        private sealed class LineMeta
        {
            [JsonPropertyName("itemIdOne")] public string ItemIdOne { get; set; }
        }

        public async Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (e is not SaleRecorded recorded) return;

            var sale = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == recorded.SaleId, ct);
            if (sale == null)
                throw new InvalidOperationException($"SaleRecorded {recorded.SaleId}: SaleV2 row not found.");
            if (sale.LegacyRef != null) return; // historic ETL rows never move stock

            var lines = sale.Lines
                .Select(l => (Line: l, ItemIdOne: ParseItemIdOne(l.DiscountsJson)))
                .Where(x => !string.IsNullOrEmpty(x.ItemIdOne))
                .ToList();
            if (lines.Count == 0) return;

            var storeId = await ResolveStoreAsync(_db, sale.TillId, ct);
            var service = new StockLedgerService(_db);
            var location = await service.EnsureStoreLocationAsync(sale.TenantId, storeId, ct);

            foreach (var (line, itemIdOne) in lines)
            {
                var isReturn = line.Qty < 0;
                await service.ApplyAsync(
                    sale.TenantId, location, itemIdOne, line.ItemId,
                    isReturn ? StockMovementType.Return : StockMovementType.Sale,
                    -line.Qty, // SALE: qty 2 → −2; RETURN: qty −1 → +1
                    null, sale.Id, sale.OperatorUserId, ct);
            }
        }

        private static string ParseItemIdOne(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<LineMeta>(json)?.ItemIdOne; }
            catch (JsonException) { return null; }
        }

        /// <summary>Till → store via the legacy hierarchy (module-local — no cross-module
        /// reference); unknown tills bucket under store 0 like the other projections.</summary>
        private static async Task<int> ResolveStoreAsync(MySqlDbContext db, Guid tillId, CancellationToken ct)
        {
            var till = await db.Till.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tillId, ct);
            return till?.StoreId ?? 0;
        }
    }

    /// <summary>WP5.1 rebuild: recompute every StockLevel from the movement ledger (the
    /// ledger itself is append-only truth and is never touched).</summary>
    public static class StockRebuilder
    {
        public static async Task<int> RebuildLevelsAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            db.CurrentUser = "stock-rebuild";
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var sums = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .GroupBy(m => new { m.StockLocationId, m.ItemIdOne })
                .Select(g => new { g.Key.StockLocationId, g.Key.ItemIdOne, Quantity = g.Sum(m => m.QtyDelta) })
                .ToListAsync(ct);

            var old = await db.StockLevels.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId).ToListAsync(ct);
            db.StockLevels.RemoveRange(old);
            db.StockLevels.AddRange(sums.Select(s => new StockLevel
            {
                TenantId = tenantId, StockLocationId = s.StockLocationId,
                ItemIdOne = s.ItemIdOne, Quantity = s.Quantity,
            }));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return sums.Count;
        }

        /// <summary>Opening balances: one ADJUSTMENT per legacy `Stocks` row (idempotent —
        /// skips items that already carry an opening movement). Run once per store at
        /// adoption; the ledger and the legacy table then evolve in parallel via the two
        /// consumers until legacy retires.
        ///
        /// Adoption-order proofing: the legacy quantity ALREADY reflects every sale the
        /// bridge has applied, so pipeline history must not ALSO enter the ledger —
        ///  (a) pipeline movements written BEFORE an item's opening are double-counts and
        ///      are removed (heals a consumer-replayed-history-then-seed ordering), and
        ///  (b) the stock consumer's offset is advanced to the outbox high-water mark so
        ///      unprocessed history never replays after the seed (fresh-install ordering).
        /// Levels are rebuilt from the ledger when anything was healed.</summary>
        public const string OpeningReason = "opening balance (legacy Stocks)";

        public static async Task<int> SeedOpeningBalancesAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            db.CurrentUser = "stock-open";
            var service = new StockLedgerService(db);
            var stocks = await db.Stocks.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Quantity != 0).ToListAsync(ct);
            var added = 0;

            foreach (var storeGroup in stocks.GroupBy(s => s.IdThree))
            {
                var location = await service.EnsureStoreLocationAsync(tenantId, storeGroup.Key, ct);
                var seeded = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.StockLocationId == location.Id && m.Reason == OpeningReason)
                    .Select(m => m.ItemIdOne).ToListAsync(ct);
                var seededSet = seeded.ToHashSet();

                foreach (var row in storeGroup.Where(r => !seededSet.Contains(r.IdOne)))
                {
                    await service.ApplyAsync(tenantId, location, row.IdOne,
                        DeterministicGuid.ForItem(row.IdTwo, row.IdOne),
                        StockMovementType.Adjustment, row.Quantity, OpeningReason, null, null, ct);
                    added++;
                }
            }

            // Persist the new openings first — the heal below queries them from the DB.
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

            // (a) heal: pipeline movements older than the item's opening are double-counts.
            var openings = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.Reason == OpeningReason)
                .Select(m => new { m.StockLocationId, m.ItemIdOne, m.AtUtc })
                .ToListAsync(ct);
            var openingAt = openings.ToDictionary(o => (o.StockLocationId, o.ItemIdOne), o => o.AtUtc);
            var pipeline = await db.StockMovements.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId &&
                            (m.Type == StockMovementType.Sale || m.Type == StockMovementType.Return))
                .ToListAsync(ct);
            var stale = pipeline.Where(m =>
                openingAt.TryGetValue((m.StockLocationId, m.ItemIdOne), out var at) && m.AtUtc < at).ToList();
            db.StockMovements.RemoveRange(stale);

            // (b) fence: unprocessed history must not replay into the ledger after the seed.
            // Only when openings were actually written — a tenant with no legacy stock keeps
            // full history replay (the pipeline IS its ledger from day one).
            if (added > 0)
            {
                long mark = await db.OutboxEvents.AnyAsync(ct) ? await db.OutboxEvents.MaxAsync(e => e.Id, ct) : 0;
                var offset = await db.ConsumerOffsets
                    .FirstOrDefaultAsync(o => o.ConsumerName == StockProjectionConsumer.ConsumerName, ct);
                if (offset == null)
                    db.ConsumerOffsets.Add(new ConsumerOffset
                    {
                        ConsumerName = StockProjectionConsumer.ConsumerName,
                        LastOutboxId = mark, UpdatedAtUtc = DateTime.UtcNow,
                    });
                else if (offset.LastOutboxId < mark)
                {
                    offset.LastOutboxId = mark;
                    offset.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            if (stale.Count > 0) await RebuildLevelsAsync(db, tenantId, ct);
            return added;
        }
    }
}
