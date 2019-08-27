using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class TaxModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private double _rate;
        #endregion

        #region Properties
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public double Rate
        {
            get => _rate;
            set => SetProperty(ref _rate, value);
        }

        #region Relationships
        public virtual ICollection<ItemModel> Items { get; set; }
        #endregion
        #endregion
    }
}
