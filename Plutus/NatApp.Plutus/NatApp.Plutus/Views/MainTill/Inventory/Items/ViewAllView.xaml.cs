using NatApp.Plutus.Helpers.Extensions.XAML.ListViewWithContextMenu;
using NatApp.Plutus.ViewModels.MainTill.Inventory.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views.MainTill.Inventory.Items
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewAllView : ContentPage
    {
        public ViewAllView()
        {
            InitializeComponent();

            ItemsListView.ItemGenerator = new ItemGeneratorExt(ItemsListView);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ((ViewAllViewModel)BindingContext).InitItems();
        }
    }
}