using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RiRePage : ContentPage
    {
        public ObservableCollection<AuthActions> Items { get; set; }

        public RiRePage()
        {
            InitializeComponent();

            Items = new ObservableCollection<AuthActions>();
            var tempList = App.DbContext.GetAllActions();
            foreach(var item in tempList)
            {
                Items.Add(item);
            }

            BindingContext = this;
        }

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            foreach (var item in Items)
            {
                if (!item.Active) continue;
                AddEmployeePage.NewEmployee.Actions.Add(item);
            }
            await Navigation.PopModalAsync();
        }
    }
}