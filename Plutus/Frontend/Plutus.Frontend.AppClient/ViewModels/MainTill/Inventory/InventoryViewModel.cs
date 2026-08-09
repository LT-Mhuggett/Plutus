using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory
{
    public class InventoryViewModel : BaseViewModel
    {
        public InventoryViewModel(StackLayout leftStackLayout, StackLayout rightStackLayout)
        {
            Title = "InventMgmt".Translate();
            Icon = "md-all-inbox";

            // ⚠ "ADD ITEM" IS HIDDEN (2026-08-10). It could not work, in both directions at once.
            //
            // It writes the new item into the LEGACY local database (`Helpers.Database.Database`),
            // which no longer feeds anything: the platform has no item-WRITE endpoint on the till
            // client — `PlutusApiClient` reads the catalogue and nothing more — so a till-created
            // item reaches no report, no other till and no VAT return. And since the basket now
            // resolves items from the v2 catalogue, a locally-created item cannot even be SOLD on
            // the machine that made it. An operator would type a full item in and find it missing.
            //
            // Items are the PORTAL's to create (till-design "Portal decides, till obeys"). MAUI's
            // parity target for inventory is WP10; until then this is honestly absent rather than
            // dishonestly present. Recorded in `Build/legacy-removal.md` (L2).
            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create(string.Format("ViewAllArg".Translate(), "Items"), "OpenViewAllItemsCommand" ),
            };

            for (int i = 0; i < buttons.Count; i++)
            {
                var button = new Button { Text = buttons[i].Item1 };
                button.SetBinding(Button.CommandProperty, buttons[i].Item2);
                if (i % 2 == 0)
                    leftStackLayout.Children.Add(button);
                else
                    rightStackLayout.Children.Add(button);
            }
        }

        #region Commands
        Command _openViewAllItemsCommand;

        public Command OpenViewAllItemsCommand
        {
            get => _openViewAllItemsCommand ?? (_openViewAllItemsCommand = new Command(ExecuteOpenViewAllItems));
        }

        Command _openAddItemCommand;

        public Command OpenAddItemCommand
        {
            get => _openAddItemCommand ?? (_openAddItemCommand = new Command(ExecuteOpenAddItem));
        }
        #endregion

        #region Execute Commands
        /// <summary>
        /// ⚠ NO LOADING OVERLAY AROUND A NAVIGATION, and this is what broke the Inventory screen's
        /// LAYOUT — reported 2026-08-10: the item list drew over the tab bar, at the wrong size,
        /// with no way back out.
        ///
        /// It read `App.SetLoading(IsBusy = true);` and then immediately awaited `PushAsync`. The
        /// overlay is a MODAL page, so that is a modal push and a navigation push issued against the
        /// same window in the same instant. MAUI does not serialise the two stacks, and the WinUI
        /// handler resolves the overlapping transitions into one corrupted layout.
        ///
        /// ⚠ It was also a CROSS-SCREEN contract: nothing here ever lowered the overlay — the
        /// FINALLY only clears `IsBusy`. The screen being opened was expected to call
        /// `SetLoading(false)` on its way in, so if it failed to load, or threw first, the overlay
        /// stayed over the whole app for the rest of the session. A screen must own its own spinner.
        /// `ViewAllViewModel.InitItems` raises and lowers one around its own read, which is where it
        /// belongs.
        /// </summary>
        private async void ExecuteOpenViewAllItems()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new ViewAllView());
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here is an unhandled exception, not a failed command.
                Services.Analytics.CrashLog.Write("InventoryViewModel.OpenViewAllItems", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>⚠ Unreachable — "Add item" has no button (see the constructor). Kept only until
        /// `Build/legacy-removal.md` L2 is actioned.</summary>
        private async void ExecuteOpenAddItem()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new AddEditView());
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("InventoryViewModel.OpenAddItem", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}
