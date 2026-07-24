using Database.Attributes;
using Database.Models.Interface;

namespace Database.Models
{
    public class BaseModel<T> : NotifyModelChanged, IBase<T>
    {
        #region Fields
        private T _id;
        #endregion

        #region Properties
        [Exportable]
        public T Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }
        #endregion
    }
}
