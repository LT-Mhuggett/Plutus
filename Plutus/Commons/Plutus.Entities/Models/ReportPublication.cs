using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Which reports the portal has published to a till — ruling 5b(a), 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"Portal shows which reports a till can show."* This is where that choice is stored.
    /// The superset of what CAN be published is <c>SharedKernel.ReportCatalogue</c>; this table records
    /// which of those keys a given shop wants on a given till.
    ///
    /// ⚠⚠ **NO ROW MEANS EVERY REPORT**, and that is deliberate rather than lazy. A tenant who has never
    /// opened the portal screen must see exactly the menu they saw yesterday — defaulting to "nothing
    /// published" would empty the Reports tab on every till in every shop the moment this deployed. The
    /// rule lives in <c>ReportCatalogue.PublishedOr</c> so both clients and the server cannot disagree
    /// about it.
    ///
    /// ⚠ An **empty** <see cref="KeysJson"/> array is different from no row: it is a shop deliberately
    /// saying "this till shows no reports". Collapsing the two would make one of them impossible to
    /// express, which is why the default is signalled by absence and not by <c>[]</c>.
    ///
    /// ⚠ TWO LEVELS, and the more specific wins: <see cref="TillId"/> null is the tenant's default for
    /// every till; a row with a <see cref="TillId"/> overrides it for that one till. A shop with a back
    /// office and a counter till wants different menus on them, and making every till identical would
    /// force the safest choice on all of them.
    ///
    /// ⚠ The permission half is separate and independent — see <c>SharedKernel.ReportPermissions</c>.
    /// **The publish decides the menu, the permission decides the door.** A till published a report its
    /// operator may not read shows nothing, never a refusal.
    /// </summary>
    public class ReportPublication
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        /// <summary>
        /// The till this applies to, or <see langword="null"/> for the tenant-wide default.
        ///
        /// ⚠ Unique per (TenantId, TillId) — see the model configuration. Two rows for one till would
        /// make "which menu does this till show" a question with two answers.
        /// </summary>
        public Guid? TillId { get; set; }

        /// <summary>
        /// A JSON array of report keys from <c>ReportCatalogue.Keys</c>.
        ///
        /// ⚠⚠ THESE ARE WIRE VALUES AND THEY ARE STORED. Renaming a key in the catalogue silently
        /// unpublishes it for every tenant that had it ticked, which is why the catalogue's own test pins
        /// the shipped set. **Add, never rename.**
        ///
        /// ⚠ Stored as JSON rather than as a child table on purpose: it is read whole, written whole, and
        /// never queried by member. A join table would buy nothing and would need its own migration every
        /// time a report was added.
        /// </summary>
        public string KeysJson { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public string UpdatedBy { get; set; }
    }
}
