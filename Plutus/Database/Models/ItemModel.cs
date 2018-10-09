using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class ItemModel : INotifyPropertyChanged, IAuditable, IBase<string>
    {
        private ItemModel item;
        
        public string Id { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Desc { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal Cost { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal ExPrice { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal Price { get; set; }
        public byte[] Image { get; set; }

        [ForeignKey("VatIdFK")]
        public int VatId { get; set; }
        [ForeignKey("CatIdFK")]
        public int CatId { get; set; }
        
        public List<Discount_Item> DisItems { get; set; }
        public List<TransactionModel> Transactions { get; set; }
        public List<RefundModel> Refunds { get; set; }
        public List<SavedItemModel> SavedItems { get; set; }
        public List<CheckoutItemChangeModel> CheckoutItemChanges { get; set; }
        public StockModel Stock { get; set; }
        public TaxModel Vat { get; set; }
        public CategoryModel Cat { get; set; }

        [NotMapped]
        public char GroupKey { get; set; }

        /**
         * Properties to incorperate Basket Model
         */
        [NotMapped]
        private int _amount { get; set; }
        [NotMapped]
        private bool _return { get; set; }
        [NotMapped]
        public string Reason { get; set; }
        [NotMapped]
        public string SaleId { get; set; }


        public ItemModel()
        {
        }

        public ItemModel ShallowCopy()
        {
            return (ItemModel) this.MemberwiseClone();
        }

        [NotMapped]
        public int Amount
        {
            get => _amount;
            set
            {
                if (_amount == value) return;
                _amount = value;
                OnPropertyChanged(nameof(Amount));
            }
        }

        [NotMapped]
        public bool Return
        {
            get => _return;
            set
            {
                if(_return==value) return;
                _return=value;
                OnPropertyChanged(nameof(Return));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
