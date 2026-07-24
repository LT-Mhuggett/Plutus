using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Pages.MainTill;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.SearchHandlers
{
    public class ItemSearchHandler : SearchHandler
    {
        public IList<Item> Items { get; set; }
        public Type SelectedItemNavigationTarget { get; set; }

        protected override void OnQueryChanged(string oldValue, string newValue)
        {
            base.OnQueryChanged(oldValue, newValue);

            if (string.IsNullOrEmpty(newValue))
                ItemsSource = null;
            else
                ItemsSource = Items.Where(i => i.IdOne.ToLower().Contains(newValue.ToLower()) ||
                                               i.Name.ToLower().Contains(newValue.ToLower()) ||
                                               i.Desc.ToLower().Contains(newValue.ToLower()) ||
                                               i.Brand.ToLower().Contains(newValue.ToLower()))
                                   .ToList();
        }

        protected override async void OnItemSelected(object item)
        {
            base.OnItemSelected(item);

            await Task.Delay(1000);

            ShellNavigationState state = (App.Current.MainPage as Shell).CurrentState;
            await Shell.Current.GoToAsync($"{GetNavigationTarget()}?name={((Item)item).Name}");
        }

        string GetNavigationTarget()
        {
            throw new NotImplementedException();
        }
    }
}
