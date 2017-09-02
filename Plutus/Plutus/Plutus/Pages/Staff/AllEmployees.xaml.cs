using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class AllEmployees : ContentPage
    {
        public ObservableCollection<EmployeeModel> Items { get; set; }

        public AllEmployees()
        {
            InitializeComponent();

            Items = new ObservableCollection<EmployeeModel>();
            foreach (var emp in App.DbContext.GetAllEmps().ToList())
            {
                Items.Add(emp);
            }

            BindingContext = this;
        }

        async void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;
            ((ListView)sender).SelectedItem = null;
        }

        private async void ChangeAccessRights_Clicked(object sender, EventArgs e)
        {
            var tempEmp = (EmployeeModel)((MenuItem)sender).CommandParameter;
            await App.Current.MainPage.Navigation.PushModalAsync(new RiRePage(tempEmp));
        }

        private async void ChangePersonelDetails_Clicked(object sender, EventArgs e)
        {
            var tempEmp = (EmployeeModel)((MenuItem)sender).CommandParameter;
            await App.Current.MainPage.Navigation.PushAsync(new AddEmployeePage(tempEmp));
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}