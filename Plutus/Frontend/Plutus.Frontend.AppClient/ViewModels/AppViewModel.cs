using Database.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Frontend.AppClient.ViewModels
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

        /// <summary>
        /// WP8: the operator signed in from the PORTAL roster, and what they may do.
        ///
        /// ⚠ Null on a till still running the legacy local login — the two paths coexist until
        /// WP2's cutover, so anything gating on permissions must handle null rather than assume.
        /// Ask it with <c>SignedInOperator.Can(code, amountPence)</c>: it applies the time window
        /// against this till's clock AND the staleness tier, so a genuinely-held permission is
        /// still refused when the roster is too old to be trusted with it.
        /// </summary>
        internal Plutus.Client.Core.SignedInOperator SignedInOperator { get; set; }
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
        }
    }
}
