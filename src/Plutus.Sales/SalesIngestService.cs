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

            // ⚠⚠ ANSWER AN ALREADY-INGESTED SALE WITHOUT ATTEMPTING THE INSERT — added 2026-08-22.
            //
            // The catch below already made a duplicate idempotent, so this changes no OUTCOME. What
            // it changes is the cost of getting there: the old path opened a transaction, built the
            // whole graph, hit the PK, took a `DbUpdateException`, rolled back, and re-read — and EF
            // logged that failure at `fail` level with a full stack trace every single time.
            //
            // ⚠ FOR THE WEBSTORE THAT IS NOT AN EDGE CASE, IT IS EVERY CYCLE. The reconciler
            // deliberately re-reads an overlap window so an edited order is picked up, so it re-submits
            // recent orders by design: one Kapow order produced 94 duplicate-key failures in a single
            // day, all of them handled, all of them logged as errors. A log that cries wolf 94 times a
            // day is a log nobody reads on the day it matters.
            //
            // ⚠ THE CATCH STAYS AND MUST STAY. This is a check-then-act, so two callers racing the same
            // saleId can still both pass it; the exception path is what makes that safe. This only
            // removes the cost from the case we can see coming.
            //
            // ⚠⚠ A RECORDED SALE ONLY — **NOT** `ReadExistingOutcomeAsync`, WHICH ALSO ANSWERS FOR
            // QUARANTINED ONES. That difference is the whole safety of this shortcut. A recorded sale
            // is terminal: replaying it can never produce a different answer. A QUARANTINED one is the
            // opposite — `POST /webstores/{id}/retry` re-submits parked sales precisely BECAUSE the
            // answer may have changed (an operator has since bound the missing SKU), and it is
            // `IngestAsync` re-running validation that turns one into a recorded sale. Short-circuiting
            // on quarantine would leave that heal returning 202 for ever, and the retry endpoint marks
            // `ResolvedAtUtc` on that reply — a sale marked healed that was never actually ingested.
            var recordedAlready = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.SaleId);
            if (recordedAlready != null)
                return IngestOutcome.Recorded(200, req.SaleId, recordedAlready.ReceivedAtUtc);

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

            // Cutover step 17. ⚠ THE ONLY PLACE THIS CAN BE ENFORCED FOR THE WHOLE ESTATE: a till
            // knows what IT has refunded, never what another till has. Matt's binding default 12 —
            // *"You should not be able to refund MORE than the price paid for it."*
            var refundProblem = await ValidateRefundCapAsync(req, tenantId);
            if (refundProblem != null)
                return await QuarantineAsync(req, tenantId, refundProblem, receivedAt);

            // ⚠⚠ IS THIS TILL'S BUSINESS DAY ALREADY Z-CLOSED? Matt, 2026-08-11: *"I was able to
            // make a sale with the till closed."* He was, and the platform ACCEPTED IT — 201
            // Recorded, straight onto a day whose takings had already been counted and banked.
            //
            // The day-closed rule existed only on the CASH-EVENT path (`CashModule`, which 409s
            // every event after a ZClose). Sales never asked. So the Z-read, the banking and the
            // platform's figures for that day could disagree for ever, with nothing flagging it —
            // the variance surfaces weeks later as a discrepancy nobody can attribute.
            //
            // ⚠ THE TILL NOW REFUSES THIS TOO (`CheckoutCommit`), and this is deliberately the
            // SECOND gate rather than the only one. The till's is what protects the OPERATOR — it
            // refuses before any money is taken, with the basket intact. This one is what protects
            // the LEDGER, because a till cannot be trusted to be the only thing enforcing a rule
            // about the platform's own books: an older build, a replayed queue or a second device
            // on the same till all reach here without passing through that check.
            //
            // ⚠ QUARANTINED, NOT REJECTED. The sale is REAL — a customer paid for goods and walked
            // out with them — so it must not vanish. Quarantine keeps the money visible and puts it
            // in front of a person, which is the only correct outcome: either the day was closed
            // too early and the sale belongs to it, or the till was wrong and somebody has to say
            // which day it counts for. ⚠ `OutboxPusher` treats 202 as TERMINAL and will not retry,
            // so nothing loops.
            var closedProblem = await ValidateDayNotClosedAsync(req, tenantId, tillId);
            if (closedProblem != null)
                return await QuarantineAsync(req, tenantId, closedProblem, receivedAt);

            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                _db.SalesV2.Add(sale);
                _db.OutboxEvents.Add(BuildOutbox(sale));

                // ⚠ RECORDING THE REFUND IS HALF THE FEATURE, and it was missing entirely: only the
                // WEBSTORE ever wrote `SaleAdjustments`, so a till refund left no trace against the
                // sale it came from. Every "how much has been refunded" question — the cap above,
                // `GET /api/v1/sales/{id}`'s `adjustments`, the portal's drill-down — read a table
                // that till refunds never populated, and therefore always answered "none". A cap
                // enforced against a history nobody writes is decorative.
                foreach (var adjustment in RefundAdjustmentsOf(req, tenantId))
                    _db.SaleAdjustments.Add(adjustment);

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

        /// <summary>`Reason` is capped at 500 in the schema; a longer note would 500 the ingest.</summary>
        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

        /// <summary>The origin sale a line is giving goods back to, or null when it is an ordinary
        /// sale line. Lives in the line's meta, where `SaleAssembler` puts it.</summary>
        private static Guid? OriginOf(IngestLine line)
        {
            if (string.IsNullOrWhiteSpace(line?.DiscountsJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(line.DiscountsJson);
                if (doc.RootElement.TryGetProperty("return", out var r) &&
                    r.ValueKind == JsonValueKind.Object &&
                    r.TryGetProperty("originSaleId", out var o) &&
                    Guid.TryParse(o.GetString(), out var id) && id != Guid.Empty)
                    return id;
            }
            catch (JsonException) { }
            return null;
        }

        /// <summary>
        /// One <see cref="SaleAdjustment"/> per returned LINE, so the refund history is per item
        /// rather than per sale — which is what makes the per-line half of the cap possible at all.
        /// </summary>
        private static IEnumerable<SaleAdjustment> RefundAdjustmentsOf(IngestSaleRequest req, Guid tenantId)
        {
            foreach (var line in req.Lines)
            {
                if (OriginOf(line) is not Guid origin) continue;

                yield return new SaleAdjustment
                {
                    Id = Uuid7.New(),
                    TenantId = tenantId,
                    Type = AdjustmentType.Refund,
                    OriginalSaleId = origin,
                    AdjustmentSaleId = req.SaleId,
                    ItemId = line.ItemId,
                    // ⚠ POSITIVE magnitudes throughout. A return's line gross and qty are negative
                    // on the wire; a refund history that stores them signed makes every SUM() cancel
                    // out against the sales it is meant to be limiting.
                    Qty = Math.Abs(line.Qty),
                    AmountPence = Math.Abs(line.LineGrossPence),
                    // ⚠ NOT NULL in the schema, and a null here 500s the whole ingest rather than
                    // failing gracefully — which is how this was found. It is also the audit answer
                    // to "why did this money go back", so an empty one is a real gap, not cosmetic:
                    // the till now sends the operator's typed reason as the sale note.
                    Reason = Truncate(string.IsNullOrWhiteSpace(req.Note) ? "Returned at till" : req.Note, 500),
                    AuthoriserUserId = req.OperatorUserId,
                    CreatedAtUtc = DateTime.UtcNow,
                };
            }
        }

        /// <summary>
        /// Cutover step 17 — would this sale give back more than was ever paid? Null when it is
        /// fine, otherwise the reason to quarantine.
        ///
        /// ⚠ ENFORCED IN TWO SCOPES, because the sale-level one alone is not a cap on anything a
        /// customer would recognise. Per SALE stops the total exceeding what was taken; per ITEM
        /// stops one £30 line being refunded three times inside a £200 sale, which passes the
        /// sale-level test comfortably and is the actual shape of refund fraud. The till can only
        /// do the first (its local history is per origin sale), which is exactly why this exists.
        ///
        /// ⚠ AN ORIGIN THIS PLATFORM HAS NEVER SEEN IS ACCEPTED, deliberately. Quarantine is
        /// TERMINAL — `OutboxPusher` never retries a 202 — and a refund can legitimately reach the
        /// server before the sale it refunds: a different till's outbox drains on its own schedule.
        /// Destroying a real refund to guard against an unverifiable one is the worse trade, and
        /// the adjustment is still recorded so the sale is capped correctly once it arrives.
        /// </summary>
        private async Task<string> ValidateRefundCapAsync(IngestSaleRequest req, Guid tenantId)
        {
            var returns = req.Lines
                .Select(l => (Line: l, Origin: OriginOf(l)))
                .Where(x => x.Origin.HasValue)
                .ToList();

            if (returns.Count == 0) return null;

            // ⚠⚠ FINDING Y (Matt, 2026-08-13): what each tender TOOK, pooled across every origin this
            // basket returns against, and what has already gone back to each. Accumulated in the loop
            // below and judged after it — see the block at the end of this method for why it cannot be
            // done per origin.
            var tookByTender = new Dictionary<byte, long>();
            var refundedByTender = new Dictionary<byte, long>();
            var priorRefundSaleIds = new HashSet<Guid>();

            foreach (var byOrigin in returns.GroupBy(x => x.Origin!.Value))
            {
                var origin = await _db.SalesV2.AsNoTracking()
                    .Include(s => s.Lines)
                    .Include(s => s.Tenders)
                    .FirstOrDefaultAsync(s => s.Id == byOrigin.Key);

                if (origin == null) continue;   // see the remark above — never quarantine on this

                // ⚠ A REFUND IS NOT SOMETHING YOU CAN REFUND, and until 2026-08-10 nothing said so.
                // A refund is stored as its own sale with a NEGATIVE gross, and the cap below takes
                // `Math.Abs(origin.GrossPence)` — so a £13.99 refund looked exactly like a £13.99
                // sale with nothing yet returned against it, and the whole cap waved it through.
                //
                // That is not theoretical: on 2026-08-10 a till offered its own refunds in a
                // "which sale?" picker, an operator tapped the newest entry, and £13.99 left the
                // drawer twice on a £13.99 sale. The till's picker was fixed the same day — this is
                // the gate that does not depend on the till being right, which is the whole reason
                // the cap is enforced in two places (binding default 12).
                if (origin.GrossPence < 0)
                    return $"Sale {byOrigin.Key:D} is itself a refund, so nothing can be returned "
                         + "against it. Refund against the original purchase.";

                // ⚠ Excludes THIS sale's own rows so a re-POST cannot count itself and turn an
                // idempotent retry into an over-refund.
                var priorRows = await _db.SaleAdjustments.AsNoTracking()
                    .Where(a => a.OriginalSaleId == byOrigin.Key && a.AdjustmentSaleId != req.SaleId)
                    .Select(a => new { a.ItemId, a.AmountPence, a.AdjustmentSaleId })
                    .ToListAsync();

                // Finding Y: this origin's tenders join the pool.
                foreach (var t in origin.Tenders)
                {
                    var type = (byte)t.TenderType;
                    var magnitude = Math.Abs(t.AmountPence);
                    tookByTender[type] = tookByTender.TryGetValue(type, out var running)
                        ? running + magnitude : magnitude;
                }

                foreach (var id in priorRows.Where(a => a.AdjustmentSaleId.HasValue)
                                            .Select(a => a.AdjustmentSaleId!.Value))
                    priorRefundSaleIds.Add(id);

                // ── per sale ──
                var alreadyAll = priorRows.Sum(a => Math.Abs(a.AmountPence));
                var requestedAll = byOrigin.Sum(x => Math.Abs(x.Line.LineGrossPence));

                var whole = RefundRules.Authorise(
                    SaleRecordSource.Server, Math.Abs(origin.GrossPence), alreadyAll, requestedAll);

                if (!whole.IsAllowed || whole.WasCapped)
                    return $"Refund against sale {byOrigin.Key:D} would give back {requestedAll}p when only "
                         + $"{whole.RemainingPence}p of that sale remains refundable ({alreadyAll}p already returned "
                         + $"of {Math.Abs(origin.GrossPence)}p taken).";

                // ── per item ──
                foreach (var byItem in byOrigin.GroupBy(x => x.Line.ItemId))
                {
                    var soldPence = origin.Lines
                        .Where(l => l.ItemId == byItem.Key)
                        .Sum(l => Math.Abs(l.LineGrossPence));

                    // An item that was never on the origin sale cannot be returned against it.
                    if (soldPence == 0)
                        return $"Refund against sale {byOrigin.Key:D} includes item {byItem.Key:D}, which was "
                             + "not sold on that sale.";

                    var alreadyItem = priorRows.Where(a => a.ItemId == byItem.Key).Sum(a => Math.Abs(a.AmountPence));
                    var requestedItem = byItem.Sum(x => Math.Abs(x.Line.LineGrossPence));

                    var perItem = RefundRules.Authorise(
                        SaleRecordSource.Server, soldPence, alreadyItem, requestedItem);

                    if (!perItem.IsAllowed || perItem.WasCapped)
                        return $"Refund against sale {byOrigin.Key:D} would give back {requestedItem}p for item "
                             + $"{byItem.Key:D}, but only {perItem.RemainingPence}p of that item remains refundable "
                             + $"({alreadyItem}p already returned of {soldPence}p sold).";
                }
            }

            // ── per TENDER (finding Y, 2026-08-13) ───────────────────────────────────────────────
            //
            // ⚠⚠ THE HOLE THIS CLOSES. Matt: *"when I try to return an item that was split, it wants to
            // put the full amount to that card."* Both tills let it, and this method — which re-runs the
            // sale-level and per-item caps precisely so a modified or buggy till cannot over-refund —
            // had no notion of a tender at all. £2.00 cash + £2.40 card refunded £4.40 to the card was
            // accepted here, recorded, rolled up, and visible in no report as wrong: the card credited
            // £2.40 more than it ever took, the £2 still in the drawer.
            //
            // ⚠ REFUND-ONLY REQUESTS ONLY. A mixed basket's tenders take money IN for the sold lines as
            // well as paying it out for the returned ones, so they cannot be compared against what the
            // origin's tenders took — the numbers are not the same kind of thing. A mixed basket is
            // still covered by the sale-level and per-item caps above.
            if (req.GrossPence >= 0) return null;
            if (tookByTender.Count == 0) return null;   // origin had no recorded tenders: nothing to judge

            // What has already gone back, per tender: the tenders of the PRIOR refund sales.
            // ⚠ POOLED, and deliberately on the strict side. A prior refund that spanned two origins
            // contributes all of its tenders here, which can overstate what a tender has had back and
            // so refuse a little early. Failing closed on a money path is the right way round, and the
            // alternative — attributing a refund sale's tenders across origins by line — is arithmetic
            // nobody could check at a counter.
            if (priorRefundSaleIds.Count > 0)
            {
                var priorTenders = await _db.SaleTenders.AsNoTracking()
                    .Where(t => priorRefundSaleIds.Contains(t.SaleId))
                    .Select(t => new { t.TenderType, t.AmountPence })
                    .ToListAsync();

                foreach (var t in priorTenders)
                {
                    var type = (byte)t.TenderType;
                    var magnitude = Math.Abs(t.AmountPence);
                    refundedByTender[type] = refundedByTender.TryGetValue(type, out var running)
                        ? running + magnitude : magnitude;
                }
            }

            var requestedByTender = new Dictionary<byte, long>();
            foreach (var t in req.Tenders ?? Enumerable.Empty<IngestTender>())
            {
                var type = t.TenderType;
                var magnitude = Math.Abs(t.AmountPence);
                if (magnitude == 0) continue;   // a £0 row is the till's problem, not a cap breach
                requestedByTender[type] = requestedByTender.TryGetValue(type, out var running)
                    ? running + magnitude : magnitude;
            }

            if (requestedByTender.Count == 0) return null;

            var capacities = RefundRules.RefundCapacities(tookByTender, refundedByTender);
            var split = RefundRules.AuthoriseSplit(capacities, requestedByTender);

            if (!split.IsAllowed)
                return $"Refund would put {split.RequestedPence}p back on tender {split.OffendingTenderType}, "
                     + $"which can take at most {split.AllowedPence}p against the sale(s) being returned "
                     + $"({split.Reason})";

            return null;
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
        /// <summary>
        /// Has this till already Z-closed the day this sale claims?
        ///
        /// ⚠ SAME QUERY SHAPE AS `CashModule`'s guard — `TillId` and `BusinessDay`, looking for a
        /// `ZClose`. Two rules that mean the same thing must ask the same question; a subtly
        /// different one here would let cash and sales disagree about whether a day was open.
        ///
        /// ⚠ AN UNKNOWN TILL IS NOT BLOCKED. `tillId` is `Guid.Empty` for a device that has no till
        /// (a legacy pairing, or a bridge posting on behalf of one), and an empty id would match no
        /// cash events — so the check simply passes. Refusing instead would quarantine every sale
        /// from a till the platform has not finished learning about, which is a far bigger outage
        /// than the one this prevents.
        /// </summary>
        private async Task<string> ValidateDayNotClosedAsync(
            IngestSaleRequest req, Guid tenantId, Guid tillId)
        {
            if (tillId == Guid.Empty) return null;

            // ⚠⚠ THE LATEST Z-MARK WINS, and this must agree with `CashModule.IsClosed` exactly. A
            // supervisor can reverse a close (`ZReopen`, Matt 2026-08-11), so asking `Any(ZClose)`
            // here would refuse every sale on a REOPENED day — a till that looked reopened, said it
            // was reopened, and then quarantined every sale it took.
            //
            // ⚠ Both Z types are fetched and the decision is made by ONE shared helper rather than
            // re-derived here. Two rules that mean the same thing must ask the same question; a
            // subtly different copy is how a day ends up open for a float and shut for a sale, which
            // nobody finds until the figures stop matching.
            var zMarks = await _db.CashEvents.AsNoTracking()
                .Where(e => e.TenantId == tenantId &&
                            e.TillId == tillId &&
                            e.BusinessDay == req.BusinessDay &&
                            (e.Type == CashEventType.ZClose || e.Type == CashEventType.ZReopen))
                .ToListAsync();

            if (!CashDay.IsClosed(zMarks)) return null;

            return $"Business day {req.BusinessDay:yyyy-MM-dd} was already closed with a Z read on this "
                 + "till, so this sale cannot be counted against it. The day's takings have already "
                 + "been reconciled. Decide whether the day was closed too early (re-open it and "
                 + "release this sale) or whether this sale belongs to a later day.";
        }

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
