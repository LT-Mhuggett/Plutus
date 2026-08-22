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
        /// ⚠⚠ **THE SEAM WORKED, AND THE SUBCLASS IS GONE (step 11b, 2026-08-22).** This property was
        /// added so that logic could ask a QUESTION instead of testing a TYPE: the app asked
        /// `is BasketReturnItem` in roughly fourteen places, and each one had to be found by hand the
        /// day the subclass went. Because they all moved here first, that day was a small change
        /// rather than a hunt.
        ///
        /// ⚠ The two things this comment said had to move before the subclass could, both moved:
        /// `Reason` and `ReturnSaleId` are on `BasketItem`, and `BasketDataTemplateSelector` picks
        /// the row template from this flag rather than from the runtime type. That selector is worth
        /// remembering — it tested the derived type FIRST, and the order was load-bearing: reversed,
        /// every return would have rendered with the SALE template, right money and wrong words.
        ///
        /// ⚠ Set through `BasketItem.MarkAsReturn`, which takes the reason and the origin sale with
        /// it. ⚠ Both are optional there, deliberately: the till demands a reason before it will
        /// proceed and `CheckoutCommit` filters blank ones, so the guard stays where it always was.
        /// </summary>
        bool IsReturn { get; }
    }
}
