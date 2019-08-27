using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class ItemModel : BaseModel<string>, IAuditable
    {
        private ItemModel item;
        #region Fields
        private string _name;
        private string _brand;
        private string _desc;
        private decimal _cost;
        private decimal _exPrice;
        private decimal _price;
        private byte[] _image;
        private int _amount;
        #region Relationships
        private int _vatId;
        private int _catId;
        private StockModel _stock;
        private TaxModel _vat;
        private CategoryModel _cat;
        #endregion
        #endregion

        #region Properties
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public string Brand
        {
            get => _brand;
            set => SetProperty(ref _brand, value);
        }
        public string Desc
        {
            get => _desc;
            set => SetProperty(ref _desc, value);
        }
        public decimal Cost
        {
            get => _cost;
            set => SetProperty(ref _cost, value);
        }
        public decimal ExPrice
        {
            get => _exPrice;
            set => SetProperty(ref _exPrice, value);
        }
        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }
        public byte[] Image
        {
            get => _image;
            set => SetProperty(ref _image, value);
        }

        [NotMapped]
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        #region Relationships
        [ForeignKey("VatIdFK")]
        public int VatId
        {
            get => _vatId;
            set => SetProperty(ref _vatId, value);
        }
        [ForeignKey("CatIdFK")]
        public int CatId
        {
            get => _catId;
            set => SetProperty(ref _catId, value);
        }
        public virtual StockModel Stock
        {
            get => _stock;
            set => SetProperty(ref _stock, value);
        }
        public virtual TaxModel Vat
        {
            get => _vat;
            set => SetProperty(ref _vat, value);
        }
        public virtual CategoryModel Cat
        {
            get => _cat;
            set => SetProperty(ref _cat, value);
        }

        #region Collections
        public virtual ICollection<Discount_Item> DisItems { get; set; }
        public virtual ICollection<TransactionModel> Transactions { get; set; }
        public virtual ICollection<RefundModel> Refunds { get; set; }
        public virtual ICollection<SavedItemModel> SavedItems { get; set; }
        public virtual ICollection<CheckoutItemChangeModel> CheckoutItemChanges { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
