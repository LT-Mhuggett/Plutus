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
        public ItemModel Item { get; }

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

        public string Tax => Item.Vat.Name;

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

        /// <summary>⚠ THE ONE TYPE TEST. Every other place asks `IsReturn`; when `BasketReturnItem`
        /// is finally collapsed into a flag, this is the line that changes — see `IBasketRecord`.</summary>
        public virtual bool IsReturn => this is BasketReturnItem;

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
        #endregion
        #endregion


        public BasketItem(ItemModel item, int quantity = 1)
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
