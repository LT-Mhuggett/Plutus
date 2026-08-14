using System;
using System.Collections.Generic;
using System.Text;
using Database.Models;
using Newtonsoft.Json;

namespace Plutus.Frontend.AppClient.Models
{
    [Serializable]
    public class BasketAlteration : BasketNote
    {
        #region Public Properties
        // Setters + [JsonConstructor] added (BugFix plan, Bug 3): with two constructors
        // and get-only properties Newtonsoft could not deserialize a saved basket that
        // contained a discount — recalling it crashed the app.
        public DiscountModel Discount { get; set; }
        public IEnumerable<BasketItem> ItemsAssocitated { get; set; }

        // ── Binding default 22(c): "all discounts need to be tracked — till, logged-in employee
        //    and reason" ────────────────────────────────────────────────────────────────────────
        //
        // ⚠ PLAIN SETTABLE PROPERTIES, NOT A `DiscountAuthority`, and deliberately so. A parked
        // basket is serialised through Newtonsoft (binding default 15), and `DiscountAuthority` is
        // a readonly record struct whose positional members are init-only — exactly the kind of
        // shape that round-trips in a unit test and fails on a real recall. A discounted parked
        // basket has already crashed this app once, over `$type` coupling; these are four values
        // Newtonsoft cannot get wrong.
        //
        // ⚠ The RULE still runs in one place — `SharedKernel.DiscountAudit.Authorise`, at the moment
        // the discount is applied, where a refusal can still be acted on. What is stored here is its
        // ALREADY-VALIDATED output, so nothing re-decides at commit.
        //
        // ⚠ Null on a basket parked before 2026-08-14. `CheckoutCommit` treats that as "not
        // recorded" and sends nothing, rather than inventing an authority nobody gave.

        /// <summary>Why this discount was given, already normalised by `DiscountAudit`.</summary>
        public string DiscountReason { get; set; }

        /// <summary>The signed-in operator who applied it.</summary>
        public Guid RequestedByUserId { get; set; }

        /// <summary>The supervisor who authorised it. ⚠ Null means "no step-up was needed" — a real
        /// answer, not missing data.</summary>
        public Guid? AuthorisedByUserId { get; set; }

        /// <summary>Their name as the roster had it AT THE TIME — staff leave, and an audit trail
        /// that renders "(deleted user)" answers nothing.</summary>
        public string AuthorisedByName { get; set; }
        #endregion

        [JsonConstructor]
        public BasketAlteration(NoteModel note, DiscountModel discount, IEnumerable<BasketItem> itemsAssocitated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = itemsAssocitated;
        }

        public BasketAlteration(NoteModel note, DiscountModel discount, BasketItem itemAssociated, decimal price = 0, decimal priceExTax = 0) : base(note, price, priceExTax)
        {
            Discount = discount;
            ItemsAssocitated = new List<BasketItem>() { itemAssociated };

        }
    }
}
