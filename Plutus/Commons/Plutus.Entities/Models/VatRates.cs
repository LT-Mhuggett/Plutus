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

    /// <summary>
    /// WP2c-exempt: which VAT BAND a legacy tax row belongs to.
    ///
    /// THE PROBLEM THIS SOLVES. Items are priced against the legacy <c>Taxes</c> table, whose rows
    /// carry a name and a multiplier and nothing else. A band could therefore only ever be inferred
    /// from the rate — and that inference cannot distinguish ZERO-RATED from EXEMPT, because both
    /// are 0%. So a shop that sells both had no way to say which was which, and the difference is
    /// real money: exempt supplies block recovery of input tax attributable to them (partial
    /// exemption, HMRC Notice 706), zero-rated supplies do not.
    ///
    /// This makes the mapping EXPLICIT and portal-owned, instead of guessed from a row's name.
    /// Absent a row here, resolution falls back to the rate — which stays correct for every
    /// unambiguous band, so nothing needs mapping until a tenant actually sells exempt supplies.
    ///
    /// Tenant-owned. Keyed on the legacy <c>Taxes.IdOne</c> (an int) because that is what an item
    /// carries and what a till already knows.
    /// </summary>
    public class VatBandTaxMap
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>The legacy <c>Taxes.IdOne</c> an item's <c>TaxId</c> points at.</summary>
        public int LegacyTaxId { get; set; }
        /// <summary>The <see cref="VatRatePoint.Band"/> key this tax row means.</summary>
        public string Band { get; set; }
    }
}
