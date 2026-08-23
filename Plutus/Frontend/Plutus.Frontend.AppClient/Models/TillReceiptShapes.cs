using System;

namespace Plutus.Frontend.AppClient.Models
{
    /// <summary>
    /// **The shop's details, as they appear at the top of a receipt.**
    ///
    /// ⚠⚠ THE LAST OF THE LEGACY MODELS TO LEAVE THE TILL — L5/L6, 2026-08-23. `StoreModel` is an EF
    /// entity from the legacy SQLite schema with audit columns and a `StoreAbbr` nothing reads. The
    /// printing path used **nine** of its fields and never once loaded one from a database: every
    /// caller either took it as an argument or coalesced it to `new StoreModel()`.
    ///
    /// ⚠ SO THIS IS A SHAPE, NOT A SOURCE. Where the details actually come from is the PLATFORM —
    /// `StoreInfoCache`, since cutover step 20 — and a receipt with none is a receipt with a blank
    /// header, never a crash. Every consumer already coalesces null.
    /// </summary>
    [Serializable]
    public sealed class StoreDetails
    {
        public string StoreName { get; set; }
        public string VatIN { get; set; }
        public string ContactNumber { get; set; }

        public string AdLine1 { get; set; }
        public string AdLine2 { get; set; }
        public string City { get; set; }
        public string PostCode { get; set; }
        public string Country { get; set; }

        /// <summary>⚠ The shop's logo, printed above the address when there is one. `byte[]`, as the
        /// printer wants it — no image type on a model the printer will hand straight to a driver.</summary>
        public byte[] Logo { get; set; }

        /// <summary>
        /// The address as one line, or empty.
        ///
        /// ⚠ COMPUTED, WHERE THE LEGACY MODEL STORED IT. A stored FullAddress can disagree with the
        /// parts it was built from — and on a receipt that is the shop's address being wrong in
        /// print. Every caller already guards `IsNullOrWhiteSpace`, so empty is a handled answer.
        /// </summary>
        public string FullAddress
        {
            get
            {
                var parts = new[] { AdLine1, AdLine2, City, PostCode, Country };
                return string.Join(", ", Array.FindAll(parts, p => !string.IsNullOrWhiteSpace(p)));
            }
        }
    }

    /// <summary>
    /// One way of paying, as the checkout offers it.
    ///
    /// ⚠ Replaces `PaymentMethodModel` — five fields of an EF entity, built inline by
    /// `GenPaymentMethods` from the till's own tender list. Nothing read it from a database.
    /// </summary>
    [Serializable]
    public sealed class TenderOption
    {
        public string Name { get; set; }

        /// <summary>⚠ Can this tender give CHANGE? Cash can; a card cannot, and offering it would
        /// hand money out of the drawer against a card payment.</summary>
        public bool IsChangeable { get; set; }

        /// <summary>Can this tender give cashback?</summary>
        public bool IsCashBackable { get; set; }

        /// <summary>A fee this tender adds, if the tenant charges one.</summary>
        public decimal Charge { get; set; }

        public decimal MinimumCharge { get; set; }
    }

    /// <summary>
    /// The discount a basket alteration records.
    ///
    /// ⚠ ONLY `Id` WAS EVER READ off the legacy `DiscountModel` here — the money and the reason live
    /// on `BasketAlteration` itself, as plain settable properties, deliberately (see its header: a
    /// parked basket round-trips through Newtonsoft and positional records fail on a real recall).
    ///
    /// ⚠ It is a REAL CATALOGUE ID, not a local one, which is why it is kept at all: `CheckoutCommit`
    /// sends it so the platform can tell which published discount was applied.
    /// </summary>
    [Serializable]
    public sealed class TillDiscount
    {
        /// <summary>⚠ A REAL CATALOGUE ID, not a local one — `CheckoutCommit` sends it so the
        /// platform can tell which published discount was applied.</summary>
        public int Id { get; set; }

        /// <summary>What the operator picked, as it reads on the receipt.</summary>
        public string Name { get; set; }

        /// <summary>Percentage or fixed amount — the legacy encoding, kept because `Alterations`
        /// and `CheckoutCommit` both branch on it and changing the meaning is a money change.</summary>
        public int Type { get; set; }

        /// <summary>The magnitude, read against `Type`. ⚠ Decimal, matching what the till already
        /// does with it — the BASKET's money is integer pence, and the conversion stays where it is.</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// One payment taken during a checkout — a tender, its amount, and any change.
    ///
    /// ⚠ Replaces `PaymentMethod_SaleModel`, an EF entity with audit columns, used here purely as
    /// scratch inside the payment loop. Nothing persisted it: the SALE is committed through
    /// `CheckoutCommit` in pence, and this is the dialog's working state on the way there.
    /// </summary>
    [Serializable]
    public sealed class TakenPayment
    {
        public TenderOption TempPayMethod { get; set; }

        /// <summary>⚠ Pounds, because the dialog was written in pounds. The COMMITTED figure is
        /// integer pence (`CheckoutCommit`) — this is display arithmetic, and the conversion happens
        /// where it always did.</summary>
        public decimal Amount { get; set; }

        public decimal Change { get; set; }
    }

    /// <summary>
    /// The checkout's working sale — what has been paid so far, while the payment dialog is open.
    ///
    /// ⚠⚠ NOT WHAT GETS SAVED, AND NOT WHAT GETS PRINTED. The sale is committed by `CheckoutCommit`
    /// as an `IngestSaleRequest` in pence, and the receipt is rendered from `ReceiptSale`. This is
    /// scratch: `SaleModel` was an EF entity standing in for a tuple.
    /// </summary>
    [Serializable]
    public sealed class CheckoutSale
    {
        public DateTime DateOfSale { get; set; }
        public decimal Total { get; set; }

        /// <summary>⚠ The ex-VAT companion to `Total`. The PAIR is what makes VAT derivable — the
        /// same reason `TillItem` carries Price and ExPrice together rather than a rate.</summary>
        public decimal TotalExTax { get; set; }
        public System.Collections.Generic.List<TakenPayment> PaySales { get; set; } = new();
    }

    /// <summary>
    /// A parked basket, as the Retrieve list shows it.
    ///
    /// ⚠ Two fields. `SavedTransactionModel` was an EF entity with audit columns and the basket's
    /// whole JSON payload; this list only ever showed a name and kept an id to recall by. The
    /// payload itself lives in the v2 store (`SavedBasket`), which is what `ParkedBasket` reads.
    /// </summary>
    [Serializable]
    public sealed class ParkedBasketRef
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}