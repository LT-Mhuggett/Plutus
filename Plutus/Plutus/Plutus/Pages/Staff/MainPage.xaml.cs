using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using ZXing.Mobile;
using ZXing.Net.Mobile.Forms;
using Plutus.Models;

namespace Plutus.Pages.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        private ZXingScannerPage _scanPage;

        /// <summary>
        /// Basic constructor for MainPage[Staff]
        /// </summary>
		public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void AddEmp_Clicked(object sender, EventArgs e)
        {
            Confirm.CommandParameter = new AddEmployeePage();
            Authorise("StaffARU");
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void DeactivateEmp_Clicked(object sender, EventArgs e)
        {
            Confirm.CommandParameter = new EmployeeDeactivationPage();
            Authorise("StaffARU");
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void EmpAccessRights_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("StaffARU"))
            {

                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }

        /// <summary>
        /// This is used to get the Employee to Ensure Audit trails and Authorisation
        /// </summary>
        private async void Authorise(string auth)
        {
            if (App.EmpsLogged.Count != 1)
            {
                VerifyId.IsVisible = true;
                MPage.IsEnabled = false;
                if (Device.Idiom == TargetIdiom.Desktop)
                {
                    EId.Focus();
                    EId.Completed += (o, e) =>
                    {
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            foreach (var tempEmp in App.EmpsLogged)
                            {
                                if (tempEmp.Id == EId.Text)
                                {
                                    if (Authorisation.IsAuthorised(auth, tempEmp))
                                    {
                                        App.LastAuthUser = tempEmp;
                                        await Navigation.PushAsync((Page)Confirm.CommandParameter);
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        return;
                                    }
                                }
                            }
                            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                        });
                    };
                    Confirm.Clicked += (o, e) =>
                    {
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            foreach (var tempEmp in App.EmpsLogged)
                            {
                                if (tempEmp.Id == EId.Text)
                                {
                                    if (Authorisation.IsAuthorised(auth, tempEmp))
                                    {
                                        App.LastAuthUser = tempEmp;
                                        await Navigation.PushAsync((Page)Confirm.CommandParameter);
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        return;
                                    }
                                }
                            }
                            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                        });
                    };
                }
                else
                {
                    var opt = new MobileBarcodeScanningOptions
                    {
                        DelayBetweenContinuousScans = 3000,
                        UseNativeScanning = true,
                        TryHarder = true,
                        TryInverted = true
                    };
                    _scanPage = new ZXingScannerPage(opt, null);
                    _scanPage.OnScanResult += (result) =>
                    {
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            foreach (var tempEmp in App.EmpsLogged)
                            {
                                if (tempEmp.Id == result.Text)
                                {
                                    if (Authorisation.IsAuthorised(auth, tempEmp))
                                    {
                                        App.LastAuthUser = tempEmp;
                                        await Navigation.PushAsync((Page)Confirm.CommandParameter);
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        return;
                                    }
                                }
                            }
                            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                        });
                    };
                }
            }
            else
            {
                EmployeeModel emp = App.EmpsLogged.First();
                if (Authorisation.IsAuthorised(auth, emp))
                {
                    App.LastAuthUser = emp;
                    await Navigation.PushAsync((Page)Confirm.CommandParameter);
                    return;
                }
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
            }
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}