using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Client.Storage;

/// <summary>
/// MAUI retrofit WP2/WP3: the till's local operations — identity, catalogue lookup, effective
/// pricing, and the sale commit path.
///
/// It implements <see cref="IOutboxStore"/>, which is the point: WP1 already built and tested the
/// pusher and its retry policy against that interface, so wiring a real SQLite queue underneath
/// required no change to the engine at all.
/// </summary>
public sealed class TillStore : IOutboxStore
{
    private readonly TillDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public TillStore(TillDbContext db, Func<DateTime>? utcNow = null)
    {
        _db = db;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ── identity / meta ──

    public async Task<string?> GetMetaAsync(string key, CancellationToken ct = default) =>
        (await _db.Meta.AsNoTracking().FirstOrDefaultAsync(m => m.Key == key, ct))?.Value;

    public async Task SetMetaAsync(string key, string? value, CancellationToken ct = default)
    {
        var row = await _db.Meta.FirstOrDefaultAsync(m => m.Key == key, ct);
        if (row == null) _db.Meta.Add(new MetaEntry { Key = key, Value = value });
        else row.Value = value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> GetGuidMetaAsync(string key, CancellationToken ct = default) =>
        Guid.TryParse(await GetMetaAsync(key, ct), out var g) ? g : null;

    public async Task<int?> GetIntMetaAsync(string key, CancellationToken ct = default) =>
        int.TryParse(await GetMetaAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    // ── catalogue ──

    /// <summary>Resolve a scanned code: the item's own IdOne first, then an alias. Binned items
    /// are excluded — an offline till must stop selling something the portal has withdrawn.</summary>
    public async Task<CatalogueItem?> FindByBarcodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var item = await _db.CatalogueItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.IdOne == code && !i.Removed, ct);
        if (item != null) return item;

        var alias = await _db.Barcodes.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
        if (alias == null) return null;
        return await _db.CatalogueItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == alias.ItemId && !i.Removed, ct);
    }

    /// <summary>
    /// The price to charge right now: the latest scheduled price whose effective moment has
    /// passed, else the item's standing price.
    ///
    /// Evaluated AT LOOKUP TIME rather than applied by a sync job, so a price scheduled for 02:00
    /// takes effect at 02:00 on a till that has been offline for a week — the till doesn't need to
    /// hear from anyone for a planned change to happen on time.
    /// </summary>
    public async Task<long> EffectivePricePenceAsync(Guid itemId, DateTime? atUtc = null, CancellationToken ct = default)
    {
        var at = atUtc ?? _utcNow();
        var scheduled = await _db.PriceSchedule.AsNoTracking()
            .Where(p => p.ItemId == itemId && p.EffectiveFromUtc <= at)
            .OrderByDescending(p => p.EffectiveFromUtc)
            .FirstOrDefaultAsync(ct);
        if (scheduled != null) return scheduled.PricePence;

        var item = await _db.CatalogueItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        return item?.PricePence ?? 0;
    }

    /// <summary>Apply a page of the changes feed. Tombstones (removed) are honoured, not skipped —
    /// otherwise a binned item stays sellable on every offline till indefinitely.</summary>
    public async Task ApplyCatalogueChangesAsync(IEnumerable<CatalogueItem> changes, long newVersion, CancellationToken ct = default)
    {
        foreach (var incoming in changes)
        {
            var existing = await _db.CatalogueItems.FirstOrDefaultAsync(i => i.Id == incoming.Id, ct);
            if (existing == null) _db.CatalogueItems.Add(incoming);
            else
            {
                existing.IdOne = incoming.IdOne;
                existing.Name = incoming.Name;
                existing.Kind = incoming.Kind;
                existing.PricePence = incoming.PricePence;
                existing.VatRateBp = incoming.VatRateBp;
                existing.CategoryId = incoming.CategoryId;
                existing.BandData = incoming.BandData;
                existing.Removed = incoming.Removed;
                existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
            }
        }
        await _db.SaveChangesAsync(ct);
        await SetMetaAsync(MetaKeys.CatalogueVersion, newVersion.ToString(CultureInfo.InvariantCulture), ct);
    }

    // ── the sale commit path (WP3) ──

    /// <summary>
    /// Commit a completed sale locally: allocate the next DeviceSeq and write the row — in ONE
    /// transaction, because the sale and its outbox entry are the same row and must never diverge.
    ///
    /// ⚠ ORDERING (retrofit risk #2, decided): this happens BEFORE the receipt prints. Printing is
    /// physical and irreversible; if it came first and the commit failed, a customer would hold a
    /// receipt for a sale that was never queued. A print failure after commit is a reprint problem
    /// — recoverable. The other way round is money that no longer exists.
    /// </summary>
    public async Task<LocalSale> CommitSaleAsync(IngestSaleRequest sale, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var seq = (long.TryParse(await GetMetaAsync(MetaKeys.DeviceSeq, ct), out var last) ? last : 0) + 1;
        sale.DeviceSeq = seq;

        var row = new LocalSale
        {
            SaleId = sale.SaleId,
            DeviceSeq = seq,
            BusinessDay = sale.BusinessDay.ToString("yyyy-MM-dd"),
            OccurredAtUtc = sale.OccurredAtUtc,
            PayloadJson = JsonSerializer.Serialize(sale, PlutusApiClient.Json),
            Status = (int)OutboxStatus.Pending,
        };
        _db.LocalSales.Add(row);

        var meta = await _db.Meta.FirstOrDefaultAsync(m => m.Key == MetaKeys.DeviceSeq, ct);
        if (meta == null) _db.Meta.Add(new MetaEntry { Key = MetaKeys.DeviceSeq, Value = seq.ToString(CultureInfo.InvariantCulture) });
        else meta.Value = seq.ToString(CultureInfo.InvariantCulture);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    /// <summary>Prune delivered sales past the rolling window. NEVER touches Pending, Failed or
    /// Quarantined: those are money still owed, or money a human has to look at.</summary>
    public async Task<int> PrunePushedAsync(TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = _utcNow() - window;
        var stale = await _db.LocalSales
            .Where(s => s.Status == (int)OutboxStatus.Pushed && s.PushedAtUtc != null && s.PushedAtUtc < cutoff)
            .ToListAsync(ct);
        _db.LocalSales.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }

    // ── IOutboxStore (drives the WP1 pusher unchanged) ──

    public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int max, CancellationToken ct = default) =>
        await _db.LocalSales.AsNoTracking()
            .Where(s => s.Status == (int)OutboxStatus.Pending)
            .OrderBy(s => s.DeviceSeq)
            .Take(max)
            .Select(s => new OutboxEntry
            {
                SaleId = s.SaleId, DeviceSeq = s.DeviceSeq, PayloadJson = s.PayloadJson,
                Status = (OutboxStatus)s.Status, PushedAtUtc = s.PushedAtUtc,
                ServerResponseJson = s.ServerResponseJson, Attempts = s.Attempts,
            })
            .ToListAsync(ct);

    public async Task UpdateAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        var row = await _db.LocalSales.FirstOrDefaultAsync(s => s.SaleId == entry.SaleId, ct);
        if (row == null) return;
        row.Status = (int)entry.Status;
        row.PushedAtUtc = entry.PushedAtUtc;
        row.ServerResponseJson = entry.ServerResponseJson;
        row.Attempts = entry.Attempts;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync(OutboxStatus status, CancellationToken ct = default) =>
        await _db.LocalSales.CountAsync(s => s.Status == (int)status, ct);

    public async Task<DateTime?> OldestPendingAtUtcAsync(CancellationToken ct = default) =>
        await _db.LocalSales.Where(s => s.Status == (int)OutboxStatus.Pending)
            .OrderBy(s => s.DeviceSeq)
            .Select(s => (DateTime?)s.OccurredAtUtc)
            .FirstOrDefaultAsync(ct);
}
