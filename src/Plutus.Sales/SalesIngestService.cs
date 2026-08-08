using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Sales
{
    // ---- ingest DTOs (architecture §4.1). tenantId/deviceId come from the token, not here. ----
    public class IngestLine
    {
        public Guid ItemId { get; set; }
        public int Qty { get; set; }
        public long UnitPricePence { get; set; }
        public long DiscountPence { get; set; }
        public long LineGrossPence { get; set; }
        public int VatRateBp { get; set; }
        public long VatAmountPence { get; set; }
        public long? OverriddenFromPence { get; set; }
        public string DiscountsJson { get; set; }
    }

    public class IngestTender
    {
        public byte TenderType { get; set; }
        public long AmountPence { get; set; }
        public long ChangePence { get; set; }
        public string ProviderRef { get; set; }
    }

    public class IngestSaleRequest
    {
        public Guid SaleId { get; set; }
        public Guid DeviceId { get; set; }     // optional; must match the token's did if both present
        public long DeviceSeq { get; set; }
        public byte Channel { get; set; }
        public DateOnly BusinessDay { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public long GrossPence { get; set; }
        public long VatPence { get; set; }
        public string Note { get; set; }
        public Guid? OperatorUserId { get; set; }
        public List<IngestLine> Lines { get; set; }
        public List<IngestTender> Tenders { get; set; }
    }

    /// <summary>Result envelope: HTTP status + the body the controller returns.</summary>
    public sealed class IngestOutcome
    {
        public int Status { get; init; }
        public object Body { get; init; }
        public static IngestOutcome Recorded(int status, Guid saleId, DateTime receivedAtUtc) =>
            new() { Status = status, Body = new { status = "recorded", saleId, receivedAtUtc } };
        public static IngestOutcome Quarantined(int status, Guid saleId) =>
            new() { Status = status, Body = new { status = "quarantined", saleId } };
        public static IngestOutcome Bad(int status, string detail) =>
            new() { Status = status, Body = new { detail } };
    }

    /// <summary>
    /// T1.4 idempotent sale ingest. One DB transaction: validate invariants (T1.3) → quarantine
    /// (202) if unfixable, else insert Sale+lines+tenders + an OutboxEvents(SaleRecorded) row +
    /// bump Device.LastSeenSeq → 201. A duplicate saleId re-reads the stored outcome → 200.
    /// Provider-agnostic idempotency: on any save conflict we re-read rather than decode a
    /// vendor error code.
    /// </summary>
    public sealed class SalesIngestService
    {
        private readonly MySqlDbContext _db;
        public SalesIngestService(MySqlDbContext db) => _db = db;

        public async Task<IngestOutcome> IngestAsync(
            IngestSaleRequest req, Guid tenantId, Guid deviceId, string actingUser)
        {
            if (req == null || req.SaleId == Guid.Empty || req.Lines == null || req.Lines.Count == 0)
                return IngestOutcome.Bad(400, "saleId and at least one line are required.");

            var receivedAt = DateTime.UtcNow;
            _db.CurrentUser = actingUser;

            // TillId is server-authoritative — derived from the enrolled device (Guid.Empty for
            // operator/web-POS tokens with no device).
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
            var tillId = device?.TillId ?? Guid.Empty;

            var lines = req.Lines.Select((l, i) => new SaleLine
            {
                Id = Uuid7.New(), TenantId = tenantId, SaleId = req.SaleId, LineNo = i + 1,
                ItemId = l.ItemId, ItemIdOne = ExtractItemIdOne(l.DiscountsJson),
                Qty = l.Qty, UnitPricePence = l.UnitPricePence, DiscountPence = l.DiscountPence,
                LineGrossPence = l.LineGrossPence, VatRateBp = l.VatRateBp,
                VatBand = ExtractVatBand(l.DiscountsJson), VatAmountPence = l.VatAmountPence,
                OverriddenFromPence = l.OverriddenFromPence, DiscountsJson = l.DiscountsJson,
            }).ToList();

            // WP2c-exempt: fill in the band for any line whose till didn't state one, from the
            // item's tax row. ⚠ This is what makes the zero-vs-exempt split hold across EVERY
            // channel — including a till on a platform that doesn't exist yet — instead of resting
            // on each client remembering. A stated band is never overwritten: the till knows things
            // the catalogue doesn't (a single-purpose gift-card line is standard-rated by the
            // voucher treatment, not by its catalogue row).
            await VatBandStamp.StampAsync(_db, lines);

            var tenders = (req.Tenders ?? new List<IngestTender>()).Select(t => new SaleTender
            {
                Id = Uuid7.New(), TenantId = tenantId, SaleId = req.SaleId,
                TenderType = (TenderType)t.TenderType, AmountPence = t.AmountPence,
                ChangePence = t.ChangePence, ProviderRef = t.ProviderRef,
            }).ToList();

            SaleV2 sale;
            try
            {
                sale = SaleV2.Create(req.SaleId, tenantId, tillId, deviceId, req.DeviceSeq,
                    (SaleChannel)req.Channel, req.BusinessDay, req.OccurredAtUtc, receivedAt,
                    req.GrossPence, req.VatPence, lines, tenders, operatorUserId: req.OperatorUserId, note: req.Note);
            }
            catch (InvalidSaleException ex)
            {
                return await QuarantineAsync(req, tenantId, ex.Message, receivedAt);
            }

            // WP2b: was every line's VAT rate legal AT THE MOMENT OF SALE? A till offline across a
            // rate change pushes sales computed at a stale cached rate; accepting them silently
            // files a wrong return, and rewriting them silently changes what the customer was
            // actually charged. So quarantine for review, same as any other unfixable disagreement.
            var vatProblem = await ValidateVatRatesAsync(req, tenantId);
            if (vatProblem != null)
                return await QuarantineAsync(req, tenantId, vatProblem, receivedAt);

            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                _db.SalesV2.Add(sale);
                _db.OutboxEvents.Add(BuildOutbox(sale));
                if (device != null) device.LastSeenSeq = Math.Max(device.LastSeenSeq, req.DeviceSeq);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return IngestOutcome.Recorded(201, sale.Id, receivedAt);
            }
            catch (DbUpdateException)
            {
                // Likely a duplicate (TenantId, Id): re-read the stored outcome (idempotent 200).
                _db.ChangeTracker.Clear();
                var existing = await ReadExistingOutcomeAsync(tenantId, req.SaleId);
                if (existing != null) return existing;
                throw;
            }
        }

        /// <summary>The web till carries the barcode in the line's DiscountsJson metadata
        /// (`{"itemIdOne":"…"}`); pull it onto SaleLine.ItemIdOne so new sales are item-reportable.</summary>
        private static string ExtractItemIdOne(string discountsJson)
        {
            if (string.IsNullOrWhiteSpace(discountsJson)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(discountsJson);
                return doc.RootElement.TryGetProperty("itemIdOne", out var v) ? v.GetString() : null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        /// <summary>
        /// WP2c-exempt: the VAT BAND the line was rung up under, from the same metadata blob
        /// (`{"vatBand":"exempt"}`).
        ///
        /// ⚠ THIS IS NOT REDUNDANT WITH `VatRateBp`. Zero-rated and exempt supplies both declare
        /// 0bp and are different in law — exempt blocks recovery of attributable input tax, zero
        /// rated doesn't. Without the band recorded here the two are indistinguishable the instant
        /// the sale is written, and no report can ever separate them again.
        ///
        /// Null from a till that doesn't send it (every till before this shipped, and MAUI until it
        /// does). Reports fall back to snapping the rate, which is right for everything except
        /// telling two 0% bands apart.
        /// </summary>
        private static string ExtractVatBand(string discountsJson)
        {
            if (string.IsNullOrWhiteSpace(discountsJson)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(discountsJson);
                if (!doc.RootElement.TryGetProperty("vatBand", out var v)) return null;
                var band = v.GetString();
                // Cap to the column width rather than letting a long value blow up the insert for
                // the whole sale — a truncated band would be worse than none.
                return string.IsNullOrWhiteSpace(band) || band.Length > 40 ? null : band;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        /// <summary>The web till puts the line's ex-VAT UNIT price in the same metadata blob
        /// (`{"exUnitPence":…}`). Unit prices are what the band rule is defined on, so discounts
        /// and quantities cannot disturb it.</summary>
        private static long? ExtractExUnitPence(string discountsJson)
        {
            if (string.IsNullOrWhiteSpace(discountsJson)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(discountsJson);
                return doc.RootElement.TryGetProperty("exUnitPence", out var v) && v.TryGetInt64(out var ex)
                    ? ex : (long?)null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        /// <summary>
        /// WP2b VAT-rate-change compliance. Returns null when every line is fine, or the reason to
        /// quarantine.
        ///
        /// ⚠ CORRECTED 2026-08-08. The first version compared each line's DECLARED `VatRateBp`
        /// against a set of clean band values. That is wrong against how this platform declares
        /// VAT: the web till derives the rate from the price pair, so ordinary lines legitimately
        /// arrive at 1998–2002bp, and exact matching would have quarantined normal trade the
        /// moment any tenant's bands were seeded. The rule is the price-pair tolerance the
        /// catalogue guard already uses — see <c>VatRateHistory</c>.
        ///
        /// Three outcomes, and only ONE of them blocks:
        ///  • a rate in force explains the pair → fine, the overwhelmingly common case;
        ///  • only a RETIRED (or not-yet-effective) rate of this tenant's own bands explains it →
        ///    quarantine: that is a till pricing on a stale band, which is the whole point;
        ///  • nothing explains it → ACCEPT. That is legacy off-band damage, which the owner
        ///    decided is surfaced by the VatIntegrity report and never blocks trading.
        ///
        /// A tenant with NO configured history skips entirely — switching a compliance guard on
        /// must not become an outage for every unseeded tenant.
        ///
        /// Gift-card lines are exempt throughout: their VAT is pinned by the tenant's voucher
        /// treatment (activation 0 or 2000bp; a single-purpose REDEMPTION is a negative
        /// standard-rated line), which is a different rule from the catalogue's bands.
        /// </summary>
        private async Task<string> ValidateVatRatesAsync(IngestSaleRequest req, Guid tenantId)
        {
            var history = await _db.VatRatePoints.AsNoTracking()
                .Select(p => new Plutus.SharedKernel.VatRate(p.Band, p.RateBp, p.EffectiveFromUtc))
                .ToListAsync();
            if (history.Count == 0) return null; // unseeded tenant — skip, don't quarantine

            foreach (var line in req.Lines)
            {
                if (string.Equals(ExtractItemIdOne(line.DiscountsJson), GiftCardItemIdOne, StringComparison.OrdinalIgnoreCase))
                    continue;

                var ex = ExtractExUnitPence(line.DiscountsJson);
                if (ex == null) continue; // no pair to reason about (older client) — never guess

                var verdict = Plutus.SharedKernel.VatRateHistory.Assess(
                    history, line.UnitPricePence, ex.Value, req.OccurredAtUtc);

                if (verdict.Verdict == Plutus.SharedKernel.VatLineVerdict.StaleBand)
                    return $"Line priced at {verdict.ExplainedByBp}bp, which was not in force at " +
                           $"{req.OccurredAtUtc:u} (in force: {string.Join(", ", verdict.InForceBp.OrderBy(r => r))}bp). " +
                           "The till may have been offline across a VAT-rate change.";
            }
            return null;
        }

        /// <summary>The provisioned gift-card catalogue row (see GiftCardSaleItem) — its VAT is
        /// governed by the voucher treatment, not the catalogue bands.</summary>
        private const string GiftCardItemIdOne = "GIFT-CARD";

        private async Task<IngestOutcome> QuarantineAsync(
            IngestSaleRequest req, Guid tenantId, string reason, DateTime receivedAt)
        {
            // Idempotent: a re-POST of an already-quarantined sale returns the same 202.
            var existing = await ReadExistingOutcomeAsync(tenantId, req.SaleId);
            if (existing != null) return existing;

            try
            {
                _db.SaleQuarantine.Add(new SaleQuarantine
                {
                    Id = Uuid7.New(), TenantId = tenantId, SaleId = req.SaleId,
                    PayloadJson = JsonSerializer.Serialize(req), Reason = Trunc(reason, 500),
                    ReceivedAtUtc = receivedAt,
                });
                await _db.SaveChangesAsync();
                return IngestOutcome.Quarantined(202, req.SaleId);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                var existing2 = await ReadExistingOutcomeAsync(tenantId, req.SaleId);
                if (existing2 != null) return existing2;
                throw;
            }
        }

        private async Task<IngestOutcome> ReadExistingOutcomeAsync(Guid tenantId, Guid saleId)
        {
            // Idempotency is keyed by the global saleId (UUIDv7) PK, so the re-read must bypass
            // the tenant query filter — otherwise a same-saleId conflict can't be re-read back
            // and the duplicate would surface as a 500 instead of the idempotent 200/202.
            var recorded = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == saleId);
            if (recorded != null) return IngestOutcome.Recorded(200, saleId, recorded.ReceivedAtUtc);
            var quarantined = await _db.SaleQuarantine.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(q => q.SaleId == saleId);
            if (quarantined) return IngestOutcome.Quarantined(202, saleId);
            return null;
        }

        private static OutboxEvent BuildOutbox(SaleV2 sale)
        {
            var evt = new SaleRecorded(Uuid7.New(), sale.TenantId, sale.OccurredAtUtc,
                sale.Id, sale.DeviceId, sale.DeviceSeq, sale.BusinessDay);
            return new OutboxEvent
            {
                EventId = evt.EventId, TenantId = sale.TenantId, EventType = nameof(SaleRecorded),
                PayloadJson = JsonSerializer.Serialize(evt), CreatedAtUtc = DateTime.UtcNow,
            };
        }

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
