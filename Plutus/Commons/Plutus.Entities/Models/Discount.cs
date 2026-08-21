using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Discount : Base<int>, IDiscount
    {
        #region Properties
        [Exportable]
        [Column("Name")] 
        [Required]
        public string Name { get; set; }

        [Exportable]
        public bool AllApplicable { get; set; }

        [Exportable]
        public bool CanUseWithOtherDiscounts { get; set; }

        [Exportable]
        public bool AutoApply { get; set; }

        [Exportable]
        public int Type { get; set; }

        [Exportable]
        public decimal Amount { get; set; }
        [Exportable]
        public int UsesPerTransaction { get; set; }
        [Exportable]
        public int RequiredNumOfItems { get; set; }
        [NotMapped]
        [DefaultValue(false)]
        public bool Changed { get; set; }

        #region The schedule — "Wednesday Warhammer" (Discount plan, 2026-08-20)

        // ⚠⚠ THE SAME SHAPE AS `RbacRoleAssignment`, COLUMN FOR COLUMN, and deliberately so: a byte
        // day-mask with bit 0 = Sunday, a LOCAL wall-clock window, and UTC validity bounds. That
        // combination is already mapped against this MySQL in production, and reusing the vocabulary
        // means "Wednesdays 09:00–17:00" means one thing in this system rather than two.
        //
        // ⚠ NOT `[Exportable]`. These are platform columns on a legacy entity, and the legacy export
        // has a fixed shape — the same reason `Item.BinnedAtUtc` and `Item.StockUntracked` carry no
        // attribute. Adding them to the export would change a CSV somebody else's tooling reads.
        //
        // ⚠ ALL NULLABLE, so every row that already exists keeps meaning exactly what it meant: no
        // mask is every day, no window is all day, no validity is "until switched off".
        //
        // ⚠⚠ THE PER-JOIN `StartDateTime`/`EndDateTime` ON `Discount_Category`/`Discount_Item` STAY
        // UNREAD. The schedule lives here, in ONE place. Two date windows that can disagree is
        // precisely the drift till-design C2 exists to prevent, and a reader who "completes" those
        // columns later would create it.

        /// <summary>Bit 0 = Sunday … bit 6 = Saturday; null = any day. ⚠ Sunday = 0 to match .NET's
        /// <c>DayOfWeek</c>, JavaScript's <c>getDay()</c> and the RBAC mask. A mask built Monday-first
        /// shifts every rule in the shop by one day, silently.</summary>
        public byte? DaysOfWeekMask { get; set; }

        /// <summary>⚠ LOCAL wall-clock, unlike <see cref="ValidFromUtc"/>. "Wednesdays 09:00–17:00" is
        /// a fact about the shop floor; a promotion's end date is an instant.</summary>
        public TimeOnly? WindowStartLocal { get; set; }
        public TimeOnly? WindowEndLocal { get; set; }

        /// <summary>⚠ A UTC INSTANT. Mixing these with the window above is the mistake the naming
        /// exists to make visible.</summary>
        public DateTime? ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }

        /// <summary>
        /// Is this rule switched on? A promotion is paused by clearing this, never by deleting the row.
        ///
        /// ⚠ Defaults to TRUE in the migration so every discount that already exists keeps working —
        /// a new column that silently switched off a shop's discounts would be the worst kind of
        /// deploy. ⚠ Read by the v1 rules feed only; the legacy `/api/Discount/Index` list the tills
        /// use for the MANUAL picker does not filter on it, so a paused rule can still be applied
        /// deliberately, by hand, with a reason and under the operator's ceiling.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Relationships

        [Exportable]
        [Required]
        public Guid BusinessId { get; set; }
        public virtual Business Business { get; set; }

        #region Collections
        public virtual ICollection<Discount_Category> DisCategoryList { get; set; }
        public virtual ICollection<Discount_Item> DisItemList { get; set; }
        public virtual ICollection<Transaction_Discount> Transaction_Discounts { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
