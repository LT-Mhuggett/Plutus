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
        public ObservableCollection<Emp_AuthActions> Items { get; set; }

        /// <summary>
        /// Basic constructor for RiRePage
        /// initalises Items from DB AuthActions
        /// </summary>
        public RiRePage(EmployeeModel Emp)
        {
            InitializeComponent();

            Items = new ObservableCollection<Emp_AuthActions>();
            var tempList = App.DbContext.Get<AuthActions>().ToList();
            if (Emp.EmpAuths.Count > 0)
            {
                if (Emp.EmpAuths.Count == tempList.Count)
                {
                    foreach (var auth in Emp.EmpAuths.OrderBy(a => a.Auth.Name))
                    {
                        Items.Add(auth);
                    }
                }
                else
                {
                    foreach (var item in tempList.OrderBy(a => a.Name))
                    {
                        var tempAuth = Emp.EmpAuths.FirstOrDefault(a => a.Auth.Id.Equals(item.Id));
                        if (tempAuth != null)
                            Items.Add(tempAuth);
                        else
                        {
                            Emp_AuthActions temp = new Emp_AuthActions() { Auth = item, Emp = Emp };
                            Items.Add(temp);
                        }
                    }
                }
                Confirm.Clicked += async (object sender, EventArgs e) =>
                  {
                      if (!App.DbContext.Save())
                      {
                          await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                          return;
                      }
                      await Navigation.PopModalAsync();
                  };
            }
            else
            {
                foreach (var item in tempList.OrderBy(a => a.Name))
                {
                    Emp_AuthActions temp = new Emp_AuthActions() { Auth = item, Emp = Emp };
                    Items.Add(temp);
                }
                Confirm.Clicked += async (object sender, EventArgs e) =>
                {
                    await Navigation.PopModalAsync();
                };
            }

            BindingContext = this;
        }
    }
}