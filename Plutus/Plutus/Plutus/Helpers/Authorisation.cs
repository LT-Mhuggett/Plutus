using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models;
using System.Reflection;
using Xamarin.Forms;
using ZXing.Mobile;
using System.Linq;
using ZXing.Net.Mobile.Forms;

namespace Plutus.Helpers
{
    public class Authorisation
    {
        public static ZXingScannerPage _scanPage { get; private set; }
        public static List<AuthActions> list { get; set; }

        public static bool IsAuthorised(string Action, string RightNeeded, EmployeeModel eTemp)
        {
            if (list == null)
            {
                list = App.DbContext.GetAllActions();
            }
            foreach (var item in eTemp.EmpAuths)
            {
                if (item.Auth.Name == Action)
                {
                    var propertyInfo = typeof(Emp_AuthActions).GetProperties().Where(p => p.Name == RightNeeded).Single();
                    var test = propertyInfo.GetValue(item, null);
                    if ((bool)test)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsAuthorised(string Action, string RightNeeded, decimal amount, EmployeeModel eTemp)
        {
            foreach (var item in eTemp.EmpAuths)
            {
                if (item.Auth.Name == Action)
                {
                    if (item.Auth.Amount < amount)
                    {
                        continue;
                    }
                    var propertyInfo = typeof(Emp_AuthActions).GetProperties().Where(p => p.Name == RightNeeded).Single();
                    var test = propertyInfo.GetValue(item, null);
                    if ((bool)test)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        public async static void CheckAuthentication(StackLayout VerifyId, StackLayout MPage, Entry EId, Button Confirm, string authName, string authReq, Action action)
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
                                    if (IsAuthorised(authName, authReq, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
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
                                    if (Authorisation.IsAuthorised(authName, authReq, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
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
                                    if (Authorisation.IsAuthorised(authName, authReq, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                        });
                    };
                }
            }
            else
            {
                EmployeeModel emp = App.EmpsLogged.First();
                if (Authorisation.IsAuthorised(authName, authReq, emp))
                {
                    App.LastAuthUser = emp;
                    action();
                    return;
                }
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
            }
        }

        public async static void CheckAuthentication(StackLayout VerifyId, StackLayout MPage, Entry EId, Button Confirm, string authName, string authReq, decimal Amount, Action action)
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
                                    if (IsAuthorised(authName, authReq, Amount, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
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
                                    if (Authorisation.IsAuthorised(authName, authReq, Amount, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
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
                                    if (Authorisation.IsAuthorised(authName, authReq, Amount, tempEmp))
                                    {
                                        EId.Text = null;
                                        VerifyId.IsVisible = false;
                                        MPage.IsEnabled = true;
                                        App.LastAuthUser = tempEmp;
                                        action();
                                        return;
                                    }
                                }
                            }
                            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                        });
                    };
                }
            }
            else
            {
                EmployeeModel emp = App.EmpsLogged.First();
                if (Authorisation.IsAuthorised(authName, authReq, emp))
                {
                    App.LastAuthUser = emp;
                    action();
                    return;
                }
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
            }
        }
    }
}
