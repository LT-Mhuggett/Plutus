using Database.Attributes;
using Database.Enums;
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
        private string _stockDisplay = "—";
        private TaxModel _vat;
        private CategoryModel _cat;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Exportable]
        public string Brand
        {
            get => _brand;
            set => SetProperty(ref _brand, value);
        }
        [Exportable]
        public string Desc
        {
            get => _desc;
            set => SetProperty(ref _desc, value);
        }
        [Exportable]
        public decimal Cost
        {
            get => _cost;
            set => SetProperty(ref _cost, value);
        }
        [Exportable]
        public decimal ExPrice
        {
            get => _exPrice;
            set => SetProperty(ref _exPrice, value);
        }
        [Exportable]
        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
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

        #region Auditable
        [Exportable]
        public DateTime Created
        {
            get => _created;
            set => SetProperty(ref _created, value);
        }
        [Exportable]
        public DateTime Modified
        {
            get => _modified;
            set => SetProperty(ref _modified, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public string ModifiedBy
        {
            get => _modifiedBy;
            set => SetProperty(ref _modifiedBy, value);
        }
        #endregion
        #region Relationships
        [Exportable]
        [ForeignKey("VatIdFK")]
        public int VatId
        {
            get => _vatId;
            set => SetProperty(ref _vatId, value);
        }
        [Exportable]
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

        /// <summary>
        /// What the Stock column shows — a NUMBER when one is known, "∞" when the item is
        /// deliberately untracked, "—" when this screen has no count.
        ///
        /// ⚠ IT EXISTS BECAUSE BINDING TO <see cref="Stock"/> RENDERED BLANK ON EVERY ROW. `Stock`
        /// is a legacy EF navigation property; the v2 catalogue feed carries no quantity, so
        /// nothing populates it on a portal-provisioned till — and MAUI bindings fail SILENTLY, so
        /// `{Binding Stock.Quantity}` produced an empty cell rather than an error. An empty cell in
        /// a stock column reads as ZERO, which is a confident wrong answer about every item in the
        /// shop.
        ///
        /// ⚠ NOT MAPPED TO THE DATABASE — it is display state on a model that is otherwise an
        /// entity, which is a compromise, made because this model goes when the inventory screen is
        /// reshaped (cutover step 25). ⚠ It must NEVER be written back.
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string StockDisplay
        {
            get => _stockDisplay;
            set => SetProperty(ref _stockDisplay, value);
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
        public virtual ICollection<CheckoutItemChangeModel> CheckoutItemChanges { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
