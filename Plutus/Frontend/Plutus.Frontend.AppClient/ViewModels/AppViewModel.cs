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

        /// <summary>
        /// The LEGACY local employee's id, or null when there isn't one.
        ///
        /// ⚠ NULL IS A NORMAL ANSWER NOW, AND IT USED TO BE A CRASH. This is `Employees.Last().Id`,
        /// and `Employees` is filled ONLY by the legacy local login — a portal-provisioned till
        /// signs in against the synced roster, sets <see cref="SignedInOperator"/>, and leaves this
        /// collection empty for ever. `Last()` on it threw `InvalidOperationException`, and every
        /// caller is an `async void` command handler with no catch, so tapping the button closed
        /// the application. Returning null lets each caller decide, which is the honest shape: on a
        /// portal till there IS no legacy employee.
        ///
        /// ⚠ NOT THE OPERATOR. Anything that needs to know WHO is doing something — permissions,
        /// attribution on a sale, an audit trail — must use <see cref="SignedInOperator"/>, which
        /// is populated on both paths and carries the platform user id.
        /// </summary>
        internal string EmployeeId
        {
            get
            {
                // Kept: more than one legacy employee signed in at once was never supported, and
                // silently picking the last one would attribute work to the wrong person.
                if (Employees.Count > 1)
                    throw new NotImplementedException();

                return Employees.Count == 1 ? Employees[0].Id : null;
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
