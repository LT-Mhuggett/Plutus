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
        private decimal _price;
        private decimal _priceExTax;
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

        public decimal Price
        {
            get => _price;
            set
            {
                _price = value;
                OnPropertyChanged();
            }
        }

        public decimal PriceExTax
        {
            get => _priceExTax;
            set
            {
                _priceExTax = value;
                OnPropertyChanged();
            }
        }

        public string Tax => Item.Vat.Name;

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
