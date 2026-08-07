using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// MAUI retrofit WP2b: a tenant's VAT bands as an effective-dated history, so ingest can ask
    /// "was this rate legal at the moment of sale?" rather than "is it legal now?".
    ///
    /// This exists because tills go offline. One that is disconnected across a government rate
    /// change keeps charging the rate it last cached; when it reconnects, its queued sales must be
    /// judged against the rates that were in force WHEN THEY HAPPENED — accepting them silently
    /// files a wrong return, and rewriting them silently changes what the customer was charged.
    ///
    /// Tenant-owned (per the TenantOwned array + a real TenantId), because rates are jurisdictional
    /// and a multi-tenant platform will eventually hold more than one country's.
    ///
    /// ⚠ The BAND is the identity, not the rate: the standard band moving 20% → 17.5% is one band
    /// changing value, and the old rate must stop being valid. Two rows, same Band, different
    /// EffectiveFromUtc.
    /// </summary>
    public class VatRatePoint
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>Stable key for the band — "standard" | "reduced" | "zero" | "exempt", or a
        /// tenant's own. This is the IDENTITY that survives a rate change.</summary>
        public string Band { get; set; }
        /// <summary>What the customer sees, e.g. "20%" or "Zero rated (books)".</summary>
        public string? DisplayName { get; set; }
        /// <summary>
        /// UK VAT classification (<see cref="Plutus.SharedKernel.VatClass"/> as an int).
        ///
        /// ⚠ THIS IS NOT DERIVABLE FROM THE RATE. Zero-rated and exempt are both 0% to the
        /// customer but are different in law: zero-rated is a taxable supply with input-tax
        /// recovery; exempt is not taxable and BLOCKS recovery of attributable input tax. A
        /// system that stores only "0%" can never produce a partial-exemption figure.
        /// </summary>
        public int Class { get; set; }
        /// <summary>Basis points: 2000 = 20%. Integer, like all money-adjacent values here.</summary>
        public int RateBp { get; set; }
        public DateTime EffectiveFromUtc { get; set; }
        /// <summary>Free text for the audit trail — e.g. "Budget 2026, standard rate cut".</summary>
        public string? Note { get; set; }
    }
}
