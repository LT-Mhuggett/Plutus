#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;

namespace Plutus.Payments
{
    /// <summary>
    /// WP7.1 / D13: matches provider capture events against recorded sale tenders (by
    /// ProviderRef). A capture with no matching tender is an ORPHANED PAYMENT — money taken,
    /// sale not recorded — and stays in the unresolved queue until the sale arrives (late
    /// outbox drain) or a human intervenes. Run opportunistically at event ingest and on
    /// demand from the portal.
    /// </summary>
    public sealed class PaymentReconciliationService
    {
        private readonly MySqlDbContext _db;
        public PaymentReconciliationService(MySqlDbContext db) => _db = db;

        /// <summary>Try to match every unresolved capture; returns (matched, stillUnresolved).</summary>
        public async Task<(int Matched, int Unresolved)> ReconcileAsync()
        {
            var unresolved = await _db.PaymentEvents
                .Where(p => p.ResolvedAtUtc == null)
                .ToListAsync();
            var matched = 0;

            foreach (var evt in unresolved)
            {
                var tender = await (
                    from t in _db.SaleTenders.AsNoTracking()
                    where t.ProviderRef == evt.ProviderRef
                    select new { t.SaleId, t.AmountPence }).FirstOrDefaultAsync();
                if (tender == null) continue;

                evt.MatchedSaleId = tender.SaleId;
                evt.ResolvedAtUtc = DateTime.UtcNow;
                matched++;
            }
            if (matched > 0) await _db.SaveChangesAsync();
            return (matched, unresolved.Count - matched);
        }
    }
}
