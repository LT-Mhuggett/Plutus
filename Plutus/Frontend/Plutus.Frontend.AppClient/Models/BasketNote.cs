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

        // ⚠ PENCE — see `BasketItem`. `Price`/`PriceExTax` are pounds-shaped views (step 11b).
        private long _pricePence;
        private long _priceExTaxPence;
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
        /// <summary>Money on a note or alteration, in integer pence — the source of truth.
        /// ⚠ NEGATIVE on a discount: it is money coming off, and the sign is load-bearing.</summary>
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

        /// <summary>Pounds-shaped VIEW of <see cref="PricePence"/>. ⚠ Not a second store — see
        /// `BasketItem.Price` for why the rows must keep seeing a decimal.</summary>
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
        #endregion
        #endregion
        public string Tax { get; }

        /// <summary>A note is never goods coming back — it is not goods at all.</summary>
        public bool IsReturn => false;

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
