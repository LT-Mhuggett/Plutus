using Plutus.Entities.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.ClientUI.Domain.Models
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
        public Item Item { get; }

        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                OnPropertyChanged();
            }
        }

        public string Name
        {
            get => Item.Name;
        }

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

        public string Tax
        {
            // null-guarded (BugFix plan, Bug 3 latent): Tax navigation may be unloaded
            // after a saved basket is deserialized.
            get => Item?.Tax?.Name ?? string.Empty;
        }
        #endregion
        #endregion


        public BasketItem(Item item, int quantity = 1)
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
