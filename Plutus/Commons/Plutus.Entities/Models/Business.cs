using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Business : Base<Guid>, IBusiness
    {
        #region Properties


        [Exportable(ExportLevels.NonUserFriendly)]
        public byte[]? Logo { get; set; }

        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        [Required]
        public string NameAbbr { get; set; }

        [Exportable]
        public decimal? RecMarkup { get; set; }

        [Exportable]
        public string VatIN { get; set; }

        // ── WP-FY: the company year and the VAT periods, 2026-08-21 ─────────────────────────────
        //
        // ⚠⚠ MATT: *"I need to be able to set the company year in the portal. And the VAT periods.
        // This then needs to be reflected in the reports, specifically the VAT reports needs to
        // match the months it reports on."*
        //
        // ⚠⚠ THE PERIODS WERE QUERY PARAMETERS WITH DEFAULTS, NOT SETTINGS. `vat-corrections` took
        // `basis` and `staggerEndMonth` off the query string, and **`VatReturn` did not take them at
        // all** — its quarter picker was hard-wired to CALENDAR quarters (Q1 = Jan–Mar). A business
        // on any stagger but the first could not produce its own return period on that screen, at
        // all, and nothing on it admitted that.
        //
        // ⚠ ON THE BUSINESS, NOT ON THE STORE. A VAT return is filed by the business; a per-store
        // setting would invite two stores to disagree about one return. It sits beside `VatIN` for
        // the same reason — these are the facts HMRC knows this company by.
        //
        // ⚠⚠ NULLABLE, AND NULL MEANS "NEVER SET" — not "set to the default". The report has to be
        // able to say *"quarterly, stagger 1 — the default, not yet set in the portal"*, because a
        // shop reading a VAT return needs to know whether the period it is looking at is the one
        // they actually file on or the one nobody has told us about. A non-nullable column with a
        // default would make those two indistinguishable forever.

        /// <summary>Month the company's financial year starts, 1–12. Null = never set.</summary>
        public int? FinancialYearStartMonth { get; set; }

        /// <summary>
        /// Day of that month the year starts, 1–31. Null = never set; treated as 1.
        ///
        /// ⚠ A DAY AS WELL AS A MONTH, because a year end tied to the incorporation date is normal
        /// for a small company and the UK tax year itself starts on the 6th. Month alone would be
        /// right for most and quietly wrong for the rest.
        /// </summary>
        public int? FinancialYearStartDay { get; set; }

        /// <summary>
        /// How this business files VAT: <c>quarter</c> or <c>month</c>. Null = never set.
        ///
        /// ⚠ THE SAME VOCABULARY THE ENDPOINT ALREADY USES (`vat-corrections`'s `basis`), so the
        /// stored value and the query override are the same strings and nothing has to translate.
        /// </summary>
        public string? VatBasis { get; set; }

        /// <summary>
        /// The month a VAT QUARTER ENDS, 1–12 — HMRC's stagger group. 3 = Mar/Jun/Sep/Dec (stagger
        /// 1), 1 = Jan/Apr/Jul/Oct (stagger 2), 2 = Feb/May/Aug/Nov (stagger 3). Null = never set.
        ///
        /// ⚠ THE MONTH IT ENDS, NOT THE STAGGER NUMBER. `VatPeriodOf` already works in end-months
        /// and the arithmetic is easier to check against a calendar; storing "stagger 2" would mean
        /// a lookup table between here and there that could only ever be wrong.
        /// </summary>
        public int? VatStaggerEndMonth { get; set; }

        /// <summary>
        /// Who last changed the VAT period settings, and when.
        ///
        /// ⚠⚠ THIS IS AN AUDIT FIELD ON A MONEY SETTING, and it earns its place: **changing a
        /// stagger re-buckets every historical return**. The same takings, filed against different
        /// periods, produce different numbers on different returns — so "when did this change and
        /// who changed it" is the first question anybody asks when two VAT reports of the same
        /// period disagree.
        /// </summary>
        public DateTime? VatSettingsChangedAtUtc { get; set; }

        /// <summary>The user id that last changed them. ⚠ See <see cref="VatSettingsChangedAtUtc"/>.</summary>
        public Guid? VatSettingsChangedBy { get; set; }
        #region Relationships
        #region Collections
        public virtual ICollection<Category> Categories { get; set; }
        public virtual ICollection<Discount> Discounts { get; set; }
        public virtual ICollection<Employee> Employees { get; set; }
        public virtual ICollection<Item> Items { get; set; }
        public virtual ICollection<Role> Roles { get; set; }
        public virtual ICollection<Store> Stores { get; set; }
        public virtual ICollection<Tax> Taxes { get; set; }
        #endregion 
        #endregion
        #endregion
    }
}
