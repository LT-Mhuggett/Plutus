using Database.Attributes;
using Database.Enums;
using System;

namespace Database.Models
{
    [Serializable]
    public class StockModel : NotifyModelChanged, IAuditable
    {
        #region Fields
        private int _quantity;

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relationships
        private string _itemId;
        private ItemModel _item;

        private string _storeId;
        private StoreModel _store;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value);
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
        public string ItemId
        {
            get => _itemId;
            set => SetProperty(ref _itemId, value);
        }
        public virtual ItemModel Item
        {
            get => _item;
            set => SetProperty(ref _item, value);
        }

        [Exportable]
        public string StoreId
        {
            get => _storeId;
            set => SetProperty(ref _storeId, value);
        }
        public virtual StoreModel Store
        {
            get => _store;
            set => SetProperty(ref _store, value);
        }
        #endregion
        #endregion

    }
}
