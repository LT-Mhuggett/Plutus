using Database.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.AppClient.Models
{
    [Serializable]
    public class BasketItem : IBasketRecord, INotifyPropertyChanged, ICloneable
    {
        #region Variable
        private int _quantity;

        // ⚠ PENCE. The basket's money is integer pence end-to-end (step 11b); `Price` and
        // `PriceExTax` are pounds-shaped VIEWS of these, kept so the till rows' `{0:C}` bindings
        // still render £3.30 rather than £330.00.
        private long _pricePence;
        private long _priceExTaxPence;
        #endregion
        #region Properties
        #region Public
        /// <summary>⚠ A `TillItem`, not the legacy `ItemModel` — L5/L6, 2026-08-23. See `TillItem`:
        /// the basket only ever read five of that entity's fields, and the data feeding it was already
        /// v2. </summary>
        public TillItem Item { get; }

        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                OnPropertyChanged();
            }
        }

        public string Name => Item.Name;

        /// <summary>
        /// The line's unit price INCLUDING VAT, in integer pence — **the source of truth** (step 11b).
        ///
        /// ⚠⚠ THE MONEY IS PENCE NOW, AND <see cref="Price"/> IS A VIEW OF IT. Before this the
        /// basket held `decimal` pounds and `CheckoutCommit` converted at the boundary, which was
        /// lossless only because the prices had ORIGINATED as pence and been divided by 100. That
        /// argument had to be re-made every time somebody touched the path; now there is nothing to
        /// argue about, because nothing converts.
        ///
        /// ⚠ WHY <see cref="Price"/> STAYS A DECIMAL IN POUNDS rather than becoming a long: the till
        /// rows bind it with `StringFormat='{0:C}'`, and a `long` behind that formatter renders £3.30
        /// as **£330.00** — silently, on every row, on a screen nobody would think to re-check. The
        /// plan called for switching the type; keeping a pounds-shaped view removes the trap entirely
        /// instead of relying on somebody noticing it.
        /// </summary>
        public long PricePence
        {
            get => _pricePence;
            set
            {
                _pricePence = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Price));
            }
        }

        /// <summary>The same price point's ex-VAT half, in integer pence.</summary>
        public long PriceExTaxPence
        {
            get => _priceExTaxPence;
            set
            {
                _priceExTaxPence = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceExTax));
            }
        }

        /// <summary>
        /// The inc-VAT unit price in POUNDS — what the rows display and what legacy callers still
        /// pass around.
        ///
        /// ⚠ A PROJECTION OF <see cref="PricePence"/>, not a second store. Setting it rounds AWAY
        /// FROM ZERO into pence, so a value a human typed cannot land between two pence and drift.
        /// </summary>
        public decimal Price
        {
            get => _pricePence / 100m;
            set => PricePence = Plutus.SharedKernel.Pence.FromDecimal(value);
        }

        public decimal PriceExTax
        {
            get => _priceExTaxPence / 100m;
            set => PriceExTaxPence = Plutus.SharedKernel.Pence.FromDecimal(value);
        }

        /// <summary>⚠ `VatName` now, where this was `Item.Vat.Name` — the band's NAME was the only
        /// thing ever read off the `TaxModel` navigation property.</summary>
        public string Tax => Item.VatName;

        private bool _adjusted;

        /// <summary>
        /// Has this line's price been retyped by hand?
        ///
        /// ⚠⚠ THE OPERATOR MUST BE ABLE TO SEE IT. The web till marks an adjusted line with a
        /// `*` and its own row class; MAUI showed the new price and nothing else, so a line at 50p on
        /// a 5 pound item looked exactly like an item that costs 50p. That is finding W's lesson on a
        /// different control: an operator who cannot SEE that money was taken off applies another
        /// discount on top of it.
        ///
        /// ⚠ A DISPLAY FLAG, NOT AN AUDIT TRAIL. What the sale RECORDS about a price override is the
        /// override record the checkout writes; this is only what the row shows. Do not start deciding
        /// anything from it.
        /// </summary>
        public bool Adjusted
        {
            get => _adjusted;
            set
            {
                _adjusted = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Is this line goods going BACK?
        ///
        /// ⚠⚠ **A FLAG, NOT A SUBCLASS, SINCE STEP 11b (2026-08-22).** This was
        /// `this is BasketReturnItem`, and the subclass it tested for is gone. `IBasketRecord`'s own
        /// header called this "the seam": the app asked `is BasketReturnItem` in roughly fourteen
        /// places, each one a type test that had to be found and changed by hand — and two of them,
        /// in `ParkedBasket` and `BasketDataTemplateSelector`, depended on **case order** or on the
        /// runtime type to pick a row template. A selector that chose wrongly would have rendered a
        /// return as a sale, silently, with the money the right way round and the words the wrong
        /// way round.
        ///
        /// ⚠ SET THROUGH <see cref="MarkAsReturn"/>, never assigned loosely — a return carries a
        /// reason and an origin sale with it, and a line flagged as a return with neither is a
        /// refund nothing can be reconciled against.
        /// </summary>
        public bool IsReturn { get; private set; }

        /// <summary>
        /// Why it came back. ⚠ Required by the refund flow before it will proceed — see
        /// `CheckoutCommit`, which refuses a return line with no reason.
        /// </summary>
        public string Reason { get; private set; }

        /// <summary>
        /// The sale this line is going back AGAINST.
        ///
        /// ⚠⚠ THIS IS WHAT MAKES A REFUND RECONCILABLE. `CheckoutCommit` parses it to link the credit
        /// to the original sale, and the cross-till refund cap counts against it. A return without
        /// one is money leaving the drawer with nothing to net it off.
        ///
        /// ⚠ A STRING, because it also holds what an operator TYPED for a sale rung up on another
        /// till — see `CheckoutCommit.OriginSaleIdOf`, which is the only thing that may parse it.
        /// </summary>
        public string ReturnSaleId { get; private set; }

        /// <summary>
        /// Turn this line into a return.
        /// </summary>
        /// <remarks>
        /// ⚠ ONE DOOR. Previously a caller constructed a `BasketReturnItem` and then called
        /// `SetItemReturn` — two steps, and in between the line was a return that knew nothing about
        /// itself. This takes both facts at the moment the line becomes a return.
        ///
        /// ⚠⚠ BUT BOTH ARE OPTIONAL, AND THAT IS ON PURPOSE — it is what the old subclass allowed.
        /// `ExecuteReturnSelected` demands a reason before it will proceed and `CheckoutCommit`
        /// filters blank ones out of the sale note, so **the guard lives at the till and at commit,
        /// not here**. Requiring them at this point would be stricter than the platform has ever
        /// been, and it would break `ParkedBasket` restoring a blob parked before the reason was
        /// captured. Moving a guard while collapsing a type is how a refactor changes behaviour it
        /// promised not to.
        /// </remarks>
        public void MarkAsReturn(string reason = null, string returnSaleId = null)
        {
            IsReturn = true;
            Reason = reason;
            ReturnSaleId = returnSaleId;

            // ⚠ The row template is chosen from `IsReturn`, and the grid re-asks on a change
            // notification — without these the line keeps the SALE template until something else
            // happens to refresh it.
            OnPropertyChanged(nameof(IsReturn));
            OnPropertyChanged(nameof(Reason));
            OnPropertyChanged(nameof(ReturnSaleId));
        }

        /// <summary>
        /// When this line SELLS a gift card, the code being loaded — WP13.
        ///
        /// ⚠ THE LINE IS THE SALE OF STORED VALUE, NOT A PAYMENT. A card being SPENT is held in page
        /// state and appears as a tender; a card being SOLD is a line like any other, priced at its
        /// face value, and this is what says which card to activate at commit.
        ///
        /// ⚠ Its VAT is decided by the tenant's voucher treatment (`SharedKernel.GiftCardVat`), NOT
        /// by the catalogue band on the `GIFT-CARD` row — the row is a carrier, and the treatment is
        /// what HMRC cares about.
        ///
        /// ⚠ Null on every ordinary line, and on a basket parked before 2026-08-16.
        /// </summary>
        public string GiftCardCode { get; set; }

        /// <summary>
        /// The V2 CATALOGUE's category for this line — what a scheduled discount targets.
        ///
        /// ⚠⚠ IT CANNOT LIVE ON <see cref="Item"/>, AND THAT IS WHY IT IS HERE. The legacy
        /// <c>ItemModel</c> the basket carries has an <b>int</b> `CatId` pointing at the NatApp
        /// `Categories` table, which is EMPTY and permanently so on a portal till. The v2 catalogue's
        /// category is a Guid (`CatalogueItemDto.CategoryId` → `LocalItem.CategoryId`), and there is
        /// nowhere on the legacy model to put it.
        ///
        /// ⚠ Without this a category-targeted rule would match NOTHING on this till while working
        /// perfectly on the web till — silently, on every basket. That is the exact shape of the
        /// Gold-member money difference (retrofit step 27): both tills "using the shared rule", one of
        /// them never able to answer the question.
        ///
        /// ⚠ Null is safe: a rule that targets a category simply does not match, so the line is
        /// charged the shelf price rather than guessed at. Null on a basket parked before 2026-08-20.
        /// </summary>
        public Guid? CategoryId { get; set; }

        /// <summary>
        /// The barcode the operator actually SCANNED, when the item has more than one and it was not
        /// the item's own (multi-barcode, 2026-08-20).
        ///
        /// ⚠⚠ A SNAPSHOT, NEVER AN IDENTITY. <c>Item.Id</c> holds the CANONICAL code and is what the
        /// sale line, the price lookup, the stock movement and the deterministic item GUID all key
        /// on. This exists only so that when a supplier's barcode migration goes wrong somebody can
        /// ask which code the tills actually read.
        ///
        /// ⚠ A plain settable property, like the audit fields on `BasketAlteration` and for the same
        /// reason: a parked basket round-trips through Newtonsoft, and a shape it cannot rebuild is a
        /// recall that crashes. Null on every ordinary line and on any basket parked before this.
        /// </summary>
        public string ScannedBarcode { get; set; }
        #endregion
        #endregion


        public BasketItem(TillItem item, int quantity = 1)
        {
            Item = item;
            Quantity = quantity;
            Price = item.Price;
            PriceExTax = item.ExPrice;
        }

        public void IncrementQuantity(int increment = 1)
        {
            Quantity += increment;
        }

        public void DecrementQuantity(int decrement = 1)
        {
            Quantity -= decrement;
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        #endregion
    }
}
