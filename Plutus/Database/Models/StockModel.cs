using System;

namespace Database.Models
{
    [Serializable]
    public class StockModel : NotifyModelChanged, IAuditable
    {
        #region Fields
        private int _quantity;

        #region Relationships
        private string _itemId;
        private ItemModel _item;

        private string _storeId;
        private StoreModel _store;
        #endregion
        #endregion

        #region Properties
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value);
        }

        #region Relationships
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
