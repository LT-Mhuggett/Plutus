using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class SavedTransactionModel : BaseModel<string>
    {
        #region Fields
        private string _name;
        private string _data;
        #endregion

        #region Properties
        [Exportable]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public string Data
        {
            get => _data;
            set => SetProperty(ref _data, value);
        }
        #endregion
    }
}
