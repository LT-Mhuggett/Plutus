using AutoMapper;
using Database.Models;
using NatApp.Plutus.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NatApp.Plutus.ViewModels
{
    public class AppViewModel : BaseViewModel
    {
        #region Private Fields
        private IList<EmployeeModel> _employees;
        private StoreModel _store;
        private bool _toolbarItemsChanged;

        #region Loading
        private string _currentLoadingItem;
        #endregion
        #endregion

        #region Properties
        public Guid SessionId { get; }

        internal IList<EmployeeModel> Employees
        {
            get => _employees;
            set { SetProperty(ref _employees, value); }
        }

        internal string EmployeeId
        {
            get
            {
                if (Employees.Count > 1)
                    throw new NotImplementedException();
                else
                    return Employees.Last().Id;
            }
        }

        internal StoreModel Store
        {
            get => _store;
            set { SetProperty(ref _store, value); }
        }
        internal bool ToolbarItemsChanged
        {
            get => _toolbarItemsChanged;
            set
            {
                if (!value)
                    SetProperty(ref _toolbarItemsChanged, value);
                else
                    SetProperty(ref _toolbarItemsChanged, value, onChanged: () => ToolbarItemChanged());
            }
        }
        #region Loading
        public string CurrentLoadingItem
        {
            get => _currentLoadingItem;
            set { SetProperty(ref _currentLoadingItem, value); }
        }

        public bool CurrentLoadingItemIsBlank
        {
            get => string.IsNullOrEmpty(_currentLoadingItem);
        }

        internal IMapper GetMapper { get; }
        #endregion
        #endregion

        #region Custom Events
        public delegate void ToolbarItemChangedEvent();
        public event ToolbarItemChangedEvent ToolbarItemChanged;
        #endregion

        public AppViewModel()
        {
            _employees = new List<EmployeeModel>();
            SessionId = Guid.NewGuid();
            var config = new MapperConfiguration(cfg =>
            {
                //Base -> Child
                cfg.CreateMap<BasketItem, BasketReturnItem>();
                //Child -> Base
                cfg.CreateMap<BasketReturnItem, BasketItem>();
            });
            GetMapper = config.CreateMapper();
        }
    }
}
