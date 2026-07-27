using Database.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.AppClient.Models
{
    [Serializable]
    public class BasketNote : IBasketRecord, INotifyPropertyChanged
    {
        #region Private Fields
        private int _quantity;
        private decimal _price;
        private decimal _priceExTax;
        #endregion
        #region Properties
        #region Public
        public NoteModel Note { get; }
        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                OnPropertyChanged();
            }
        }
        public string Name => Note.Note;
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
        #endregion
        #endregion
        public string Tax { get; }

        public BasketNote(NoteModel note, decimal price = default, decimal priceExTax = default)
        {
            Note = note;
            Price = price;
            PriceExTax = priceExTax;
            Quantity = 1;
        }

        #region InortifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName]string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
