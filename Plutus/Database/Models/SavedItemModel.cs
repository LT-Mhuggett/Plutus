using System;

namespace Database.Models
{
    [Serializable]
    public class SavedItemModel : BaseModel<int>
    {
        #region Fields
        private int _amount;

        #region Relationships
        private string _itemId;
        private ItemModel _item;

        private int _savedTransId;
        private SavedTransactionModel _savedTrans;
        #endregion
        #endregion

        #region Properties
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
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

        public int SavedTransId
        {
            get => _savedTransId;
            set => SetProperty(ref _savedTransId, value);
        }
        public virtual SavedTransactionModel SavedTrans
        {
            get => _savedTrans;
            set => SetProperty(ref _savedTrans, value);
        }
        #endregion
        #endregion
    }
}
