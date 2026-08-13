using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    // ⚠ `MemberNumbers` — the FORMAT and the check character — moved to
    // `Plutus.SharedKernel/MemberNumbers.cs` on 2026-08-13, because a till has to answer "is this
    // scan a member card, and which one?" offline at the scanner, and MAUI may not reference a
    // backend module. See that file's header for the split, and the C1 register in
    // `Build/till-design.md`.
    //
    // What is left here is the half that CANNOT travel: allocation needs a database and a
    // tenant-wide counter. Callers in this module get it via `using Plutus.SharedKernel;` above.

    /// <summary>Hands out the next membership number for a tenant.</summary>
    public static class MemberNoAllocator
    {
        private const int MaxAttempts = 5;

        /// <summary>
        /// Allocates and PERSISTS the next number for <paramref name="tenantId"/> (the counter row
        /// is saved here so the sequence is claimed before the caller's own save). Retries on a
        /// concurrent allocation — the counter's value is its concurrency token, so a race makes the
        /// loser re-read and take the next number rather than duplicate one.
        ///
        /// ⚠ **Server-only, and a till may never do this offline** — two disconnected tills would
        /// mint the same number. Binding default 20/21 in `MAUI-retrofit.md`: adding a member at a
        /// till is online-only, on every till, for exactly this reason.
        /// </summary>
        public static async Task<string> NextAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                var counter = await db.MemberNoCounters.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
                if (counter == null)
                {
                    // First allocation for this tenant: start past any number already in use, so a
                    // half-backfilled tenant (or a restored dataset) can never re-issue one.
                    var highest = await HighestSequenceAsync(db, tenantId, ct);
                    counter = new MemberNoCounter { TenantId = tenantId, Next = highest + 1 };
                    db.MemberNoCounters.Add(counter);
                }

                var sequence = counter.Next;
                counter.Next = sequence + 1;
                try
                {
                    await db.SaveChangesAsync(ct);
                    return MemberNumbers.Format(sequence);
                }
                catch (DbUpdateException) when (attempt < MaxAttempts)
                {
                    // Someone else allocated (concurrency token mismatch) or inserted the counter
                    // first — drop our tracked copy and try again with fresh state.
                    foreach (var entry in db.ChangeTracker.Entries<MemberNoCounter>().ToList())
                        entry.State = EntityState.Detached;
                }
            }
        }

        /// <summary>The largest sequence already issued to this tenant (0 when none).</summary>
        public static async Task<long> HighestSequenceAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            var numbers = await db.Customers.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.MemberNo != null)
                .Select(c => c.MemberNo)
                .ToListAsync(ct);

            long highest = 0;
            foreach (var n in numbers)
            {
                if (n.Length <= 1) continue;
                if (long.TryParse(n[..^1], out var seq) && seq > highest) highest = seq;
            }
            return highest;
        }
    }
}
