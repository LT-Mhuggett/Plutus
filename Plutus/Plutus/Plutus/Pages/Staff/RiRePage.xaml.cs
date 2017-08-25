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

        /// <summary>
        /// Basic constructor for RiRePage
        /// initalises Items from DB AuthActions
        /// </summary>
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

        /// <summary>
        /// This method adds all items that are marked active to the employee Actions list in page AddEmployeePage
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
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