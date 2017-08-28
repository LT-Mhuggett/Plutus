using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
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
                    var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("Deactiv?Mesg"), $"{emp.LName.ToUpper()}, {emp.FName}"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                    if (!quit) return;
                    emp.Active = false;
                    App.DbContext.UpdateEmp(emp);
                    if(!await App.DbContext.Save())
                    {
                        await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                        return;
                    }
                    //success message
                }
                else
                {
                    //add what to do if user that is selected is current active user
                }
            }
        }
    }
}