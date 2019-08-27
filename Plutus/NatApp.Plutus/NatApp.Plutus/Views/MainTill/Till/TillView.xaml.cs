using NatApp.Plutus.ViewModels.MainTill.Till;
using Syncfusion.SfPicker.XForms;
using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views.MainTill.Till
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class TillView : ContentPage
    {
        public TillView()
        {
            InitializeComponent();
        }

        private void Quantity_Completed(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty((BindingContext as TillViewModel).ItemId))
                (BindingContext as TillViewModel).ManualAddCommand.Execute(null);
        }

        private void AlterationsSelected(object sender, Syncfusion.SfPicker.XForms.SelectionChangedEventArgs e)
        {
            if (e.NewValue != null)
                (BindingContext as TillViewModel).AlterTransactionCommand.Execute((sender as SfPicker).SelectedIndex);
        }
    }
}