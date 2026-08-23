using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Frontend.AppClient.ViewModels
{
        // ⚠⚠ L9, 2026-08-23 — `Employees` and `EmployeeId` are gone. `EmployeeId` was
        // `Employees.Last().Id` and `Employees` was filled ONLY by the legacy local login, which
        // Matt removed the same day. `SignedInOperator` is the one answer to "who is at this till".
        //
        // ⚠ It used to THROW on a portal-provisioned till (an empty list), out of a plain `void`
        // command handler — tapping the leftmost button on the till screen closed the app. Step 21
        // made null a normal answer; this removes the question.

    public class AppViewModel : BaseViewModel
    {
        #region Private Fields
        private Models.StoreDetails _store;
        private bool _toolbarItemsChanged;

        #region Loading
        private string _currentLoadingItem;
        #endregion
        #endregion

        #region Properties
        public Guid SessionId { get; }

        internal Models.StoreDetails Store
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
                // ⚠⚠ `?.Invoke` BELOW — IT WAS A BARE `ToolbarItemChanged()`, AND IT KILLED THE TILL.
                // An event with no subscriber is null, so raising it threw NullReferenceException from
                // inside a property setter reached via `ObservableCollection.OnCollectionChanged`:
                // unhandled, on the dispatcher, process gone.
                //
                // Matt, 2026-08-18: *"If I try to save a transaction on MAUI, it crashes."*
                // `ExecuteStoreTransaction` mutates the basket collection, the change notification set
                // this flag, and nothing was subscribed at that moment — so the till died on Save.
                //
                // ⚠ ANY collection change while no page is subscribed hit this, which makes it a
                // candidate for the sign-in crash too: `AppShell` construction moves these collections
                // before a page has wired itself up. **Never raise an event without `?.`** — the
                // compiler will not tell you, and the blast radius is the whole app, not the feature.
                if (!value)
                    SetProperty(ref _toolbarItemsChanged, value);
                else
                    SetProperty(ref _toolbarItemsChanged, value, onChanged: () => ToolbarItemChanged?.Invoke());
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
            SessionId = Guid.NewGuid();
        }
    }
}
