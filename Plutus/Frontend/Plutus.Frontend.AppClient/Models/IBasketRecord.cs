using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Frontend.AppClient.Models
{
    public interface IBasketRecord
    {
        int Quantity { get; set; }

        string Name { get; }

        /// <summary>
        /// The record's money, INC VAT, in integer pence — the source of truth (step 11b).
        ///
        /// ⚠ NEGATIVE on a discount alteration: money coming off. The sign is load-bearing and
        /// `CheckoutCommit` works in magnitudes deliberately.
        /// </summary>
        long PricePence { get; set; }

        /// <summary>The same price point's ex-VAT half, in integer pence.</summary>
        long PriceExTaxPence { get; set; }

        /// <summary>
        /// The inc-VAT figure in POUNDS.
        ///
        /// ⚠⚠ A VIEW OF <see cref="PricePence"/>, NOT A SECOND STORE. It stays a `decimal` because
        /// the till rows bind it with `StringFormat='{0:C}'` — a `long` behind that formatter renders
        /// £3.30 as **£330.00**, silently, on every row. New code should read
        /// <see cref="PricePence"/>; this exists for the bindings and the legacy callers.
        /// </summary>
        decimal Price { get; set; }

        decimal PriceExTax { get; set; }

        string Tax { get; }

        /// <summary>
        /// Is this record goods going BACK?
        ///
        /// ⚠⚠ THE SEAM FOR COLLAPSING `BasketReturnItem` (step 11b). The app asks
        /// `is BasketReturnItem` in ~28 places; each one is a type test that has to be found and
        /// changed by hand the day the subclass goes. Logic asks THIS instead, so the type test
        /// exists in exactly one implementation and the subclass can be removed underneath it.
        ///
        /// ⚠ It is NOT yet safe to delete `BasketReturnItem`: it carries `ReturnSaleId` and
        /// `Reason`, and `BasketDataTemplateSelector` still picks the row template BY TYPE — a
        /// selector that chose wrongly would render the wrong row silently. Those are the two things
        /// that must move before the subclass can.
        /// </summary>
        bool IsReturn { get; }
    }
}
