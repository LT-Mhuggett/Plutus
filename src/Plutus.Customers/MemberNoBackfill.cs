using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;   // MemberNumbers moved here 2026-08-13 — see MemberNumbers.cs

namespace Plutus.Customers
{
    /// <summary>
    /// FE2 one-off data lift: give every pre-existing customer a membership number, oldest first
    /// (so number order matches join order). IDEMPOTENT — only customers with no number are
    /// touched, so it is safe on every boot and self-heals a part-finished run.
    ///
    /// Runs after Migrate() as plain C# (like <see cref="LoyaltyTierBackfill"/>) rather than as
    /// migration SQL: the numbers need the check-character logic, and this way it is testable.
    /// </summary>
    public static class MemberNoBackfill
    {
        /// <returns>how many customers were given a number</returns>
        public static async Task<int> ApplyAsync(MySqlDbContext db, CancellationToken ct = default)
        {
            // Cross-tenant maintenance pass, not a request — hence IgnoreQueryFilters.
            var pending = await db.Customers.IgnoreQueryFilters()
                .Where(c => c.MemberNo == null || c.MemberNo == "")
                .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                .ToListAsync(ct);
            if (pending.Count == 0) return 0;

            if (string.IsNullOrEmpty(db.CurrentUser)) db.CurrentUser = "member-no-backfill";

            var counters = await db.MemberNoCounters.ToListAsync(ct);
            foreach (var byTenant in pending.GroupBy(c => c.TenantId))
            {
                var counter = counters.FirstOrDefault(x => x.TenantId == byTenant.Key);
                if (counter == null)
                {
                    // start past anything already issued (a partly-numbered tenant stays consistent)
                    var highest = await MemberNoAllocator.HighestSequenceAsync(db, byTenant.Key, ct);
                    counter = new MemberNoCounter { TenantId = byTenant.Key, Next = highest + 1 };
                    db.MemberNoCounters.Add(counter);
                }

                foreach (var customer in byTenant)
                {
                    customer.MemberNo = MemberNumbers.Format(counter.Next);
                    counter.Next++;
                }
            }

            await db.SaveChangesAsync(ct);
            return pending.Count;
        }
    }
}
