using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Views.MainTill.Inventory.Items;
using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Inventory
{
    public class InventoryViewModel : BaseViewModel
    {
        public InventoryViewModel(StackLayout leftStackLayout, StackLayout rightStackLayout)
        {
            Title = "InventMgmt".Translate();
            Icon = "md-all-inbox";

            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create(string.Format("ViewAllArg".Translate(), "Items"), "OpenViewAllItemsCommand" ),
                Tuple.Create($"{"Add".Translate()} {"Item".Translate()}", "OpenAddItemCommand")
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
        private async void ExecuteOpenViewAllItems()
        {
            if (IsBusy)
                return;
            App.SetLoading(IsBusy = true);

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new ViewAllView());
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteOpenAddItem()
        {
            if (IsBusy)
                return;
            App.SetLoading(IsBusy = true);

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new AddEditView());
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}
