#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Cash
{
    /// <summary>Cash module (WP7.2, architecture §9.2): cash sessions as idempotent events
    /// through the ingest discipline; the portal banking view reads them next to the
    /// recorded cash tenders.</summary>
    public static class CashModule
    {
        public static IServiceCollection AddPlutusCash(this IServiceCollection services)
        {
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Cash sessions require the MySqlDbContext (server build).");
                return new CashEventService(ctx);
            });
            return services;
        }
    }

    public sealed class CashEventRequest
    {
        public Guid EventId { get; set; }          // client-minted UUIDv7
        public Guid DeviceId { get; set; }         // optional; must match the token did if both present
        public string Type { get; set; }           // OpenFloat | PaidIn | PaidOut | XSnapshot | ZClose
        public DateOnly BusinessDay { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public long AmountPence { get; set; }
        public long? CountedPence { get; set; }
        public string Reason { get; set; }
        public Guid? OperatorUserId { get; set; }
    }

    public sealed class CashOutcome
    {
        public int Status { get; init; }
        public object Body { get; init; }
    }

    /// <summary>
    /// Idempotent cash-event ingest (same discipline as sales): a replayed eventId returns the
    /// stored outcome; a SECOND Z for the same till+day (different id) is a 409 — one Z per
    /// business day, enforced server-side on top of the till's local rule. X/Z ingest computes
    /// the expected drawer (float + cash takings + paid-ins − paid-outs) and freezes counted /
    /// expected / variance into the event row.
    /// </summary>
    public sealed class CashEventService
    {
        private readonly MySqlDbContext _db;
        public CashEventService(MySqlDbContext db) => _db = db;

        public async Task<CashOutcome> IngestAsync(CashEventRequest req, Guid tenantId, Guid deviceId, string actingUser)
        {
            if (req == null || req.EventId == Guid.Empty)
                return new CashOutcome { Status = 400, Body = new { detail = "eventId is required." } };
            if (!Enum.TryParse<CashEventType>(req.Type, true, out var type))
                return new CashOutcome { Status = 400, Body = new { detail = "type must be OpenFloat|PaidIn|PaidOut|XSnapshot|ZClose." } };
            if (type is CashEventType.PaidIn or CashEventType.PaidOut && string.IsNullOrWhiteSpace(req.Reason))
                return new CashOutcome { Status = 400, Body = new { detail = "reason is required for paid-in/paid-out." } };
            if (type is CashEventType.OpenFloat or CashEventType.PaidIn or CashEventType.PaidOut && req.AmountPence < 0)
                return new CashOutcome { Status = 400, Body = new { detail = "amountPence must be non-negative." } };
            if (type is CashEventType.XSnapshot or CashEventType.ZClose && req.CountedPence == null)
                return new CashOutcome { Status = 400, Body = new { detail = "countedPence is required for X/Z." } };

            _db.CurrentUser = actingUser;

            // idempotent replay
            var existing = await _db.CashEvents.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == req.EventId);
            if (existing != null)
                return new CashOutcome { Status = 200, Body = Shape(existing) };

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
            var tillId = device?.TillId ?? Guid.Empty;

            var dayEvents = await _db.CashEvents.AsNoTracking()
                .Where(e => e.TillId == tillId && e.BusinessDay == req.BusinessDay)
                .ToListAsync();

            // ⚠⚠ WHICHEVER CAME LAST WINS — no longer "does a ZClose exist". A supervisor can reverse
            // a close (`ZReopen`, Matt 2026-08-11), and both events stay on the record. Left on
            // `Any(ZClose)` this would refuse a REOPENED day for ever, which is the trap the reopen
            // exists to escape; and it would refuse the reopen itself, locking the escape hatch
            // inside the thing it unlocks.
            //
            // ⚠ TIES GO TO CLOSED. Two events on one timestamp is a clock artefact, and the safe
            // reading of an ambiguous drawer is that it is shut — a wrongly-open day quietly adds
            // sales to banked takings; a wrongly-shut one asks somebody to press reopen again.
            if (CashDay.IsClosed(dayEvents) && type != CashEventType.ZReopen)
                return new CashOutcome
                {
                    Status = 409,
                    Body = new
                    {
                        detail = "The session for this business day is already Z-closed. "
                               + "A supervisor can reopen it from the till (Cash → Reopen the day).",
                    },
                };

            // ⚠ And a reopen only makes sense against a day that IS closed. Refusing it otherwise
            // keeps the ledger honest: a ZReopen with no ZClose behind it would read as though a
            // close had been reversed that never happened.
            if (type == CashEventType.ZReopen && !CashDay.IsClosed(dayEvents))
                return new CashOutcome
                {
                    Status = 409,
                    Body = new { detail = "That business day is not closed, so there is nothing to reopen." },
                };

            var cashEvent = new CashEvent
            {
                Id = req.EventId, TenantId = tenantId, TillId = tillId, DeviceId = deviceId,
                BusinessDay = req.BusinessDay, OccurredAtUtc = req.OccurredAtUtc, ReceivedAtUtc = DateTime.UtcNow,
                Type = type, AmountPence = req.AmountPence, CountedPence = req.CountedPence,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
                OperatorUserId = req.OperatorUserId,
            };

            if (type is CashEventType.XSnapshot or CashEventType.ZClose)
            {
                var expected = await ExpectedDrawerAsync(tillId, req.BusinessDay, dayEvents);
                cashEvent.ExpectedPence = expected;
                cashEvent.VariancePence = req.CountedPence.Value - expected;
            }

            try
            {
                _db.CashEvents.Add(cashEvent);
                await _db.SaveChangesAsync();
                return new CashOutcome { Status = 201, Body = Shape(cashEvent) };
            }
            catch (DbUpdateException)
            {
                // duplicate id race — re-read the stored outcome
                _db.ChangeTracker.Clear();
                var raced = await _db.CashEvents.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == req.EventId);
                if (raced != null) return new CashOutcome { Status = 200, Body = Shape(raced) };
                throw;
            }
        }

        /// <summary>float + cash takings (Σ cash tenders net of change on the till's recorded
        /// sales for the day) + paid-ins − paid-outs.</summary>
        public async Task<long> ExpectedDrawerAsync(Guid tillId, DateOnly day, IReadOnlyList<CashEvent> dayEvents = null)
        {
            dayEvents ??= await _db.CashEvents.AsNoTracking()
                .Where(e => e.TillId == tillId && e.BusinessDay == day).ToListAsync();

            long floatPence = dayEvents.Where(e => e.Type == CashEventType.OpenFloat).Sum(e => e.AmountPence);
            long paidIn = dayEvents.Where(e => e.Type == CashEventType.PaidIn).Sum(e => e.AmountPence);
            long paidOut = dayEvents.Where(e => e.Type == CashEventType.PaidOut).Sum(e => e.AmountPence);

            var cashTakings = await (
                from t in _db.SaleTenders.AsNoTracking()
                join s in _db.SalesV2.AsNoTracking() on t.SaleId equals s.Id
                where s.TillId == tillId && s.BusinessDay == day && t.TenderType == TenderType.Cash
                select t.AmountPence - t.ChangePence).SumAsync(v => (long?)v) ?? 0;

            return floatPence + cashTakings + paidIn - paidOut;
        }

        private static object Shape(CashEvent e) => new
        {
            eventId = e.Id,
            tillId = e.TillId,
            businessDay = e.BusinessDay.ToString("yyyy-MM-dd"),
            type = e.Type.ToString(),
            amountPence = e.AmountPence,
            countedPence = e.CountedPence,
            expectedPence = e.ExpectedPence,
            variancePence = e.VariancePence,
        };
    }
}
