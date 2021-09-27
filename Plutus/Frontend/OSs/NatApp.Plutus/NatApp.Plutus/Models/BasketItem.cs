using Database.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NatApp.Plutus.Models
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
