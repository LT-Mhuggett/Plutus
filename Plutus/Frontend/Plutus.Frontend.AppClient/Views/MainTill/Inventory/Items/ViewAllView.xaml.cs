using Plutus.Frontend.AppClient.Helpers.Extensions.XAML.ListViewWithContextMenu;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items
{
    public partial class ViewAllView : ContentPage
    {
        public ViewAllView()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
           ((ViewAllViewModel)BindingContext).InitItems();
        }
    }
}