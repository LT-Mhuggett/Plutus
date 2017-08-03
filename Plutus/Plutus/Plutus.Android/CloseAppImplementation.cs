using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Xamarin.Forms;
using Plutus.Droid;

[assembly: Dependency(typeof(CloseAppImplementation))]
namespace Plutus.Droid
{
    class CloseAppImplementation : Plutus.Helpers.Interface.ICloseApp
    {
        public void CloseApp()
        {
            var activity = (Activity)Forms.Context;
            activity.FinishAffinity();
        }
    }
}