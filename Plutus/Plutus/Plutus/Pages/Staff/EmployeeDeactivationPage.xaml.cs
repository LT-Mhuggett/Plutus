using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Staff
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class EmployeeDeactivationPage : ContentPage
	{
        List<EmployeeModel> Emps = new List<EmployeeModel>();
		public EmployeeDeactivationPage ()
		{
			InitializeComponent ();

            Emps = App.DbContext.GetAllEmps().Where(e => e.Active.Equals(true)).ToList();
            foreach(var emp in Emps)
            {
                EmpList.Items.Add($"{emp.LName.ToUpper()}, {emp.FName}");
            }
		}

        private async void ConfirmDeactiv_Clicked(object sender, EventArgs e)
        {
            if (EmpList.SelectedIndex > -1)
            {
                if (Emps[EmpList.SelectedIndex].Id != App.LastAuthUser.Id)
                {
                    var emp = Emps[EmpList.SelectedIndex];
                    var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("Deactiv_Mesg"), $"{emp.LName.ToUpper()}, {emp.FName}"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                    if (!quit) return;
                    emp.Active = false;
                    if(!App.DbContext.Save())
                    {
                        await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                        return;
                    }
                    await DisplayAlert(App.Translate.ProvideValue("Success"), String.Format(App.Translate.ProvideValue("EmpDeactSuc"),$"{emp.LName.ToUpper()} {emp.FName}"), App.Translate.ProvideValue("OK"));
                    await Navigation.PopAsync();
                }
                else
                {
                    await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("DeactAuthUser"), App.Translate.ProvideValue("OK"));
                }
            }
        }
    }
}