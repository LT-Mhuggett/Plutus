using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Foundation;
using UIKit;
using System.Threading;
using Xamarin.Forms;
using Plutus.iOS;

[assembly: Dependency(typeof(CloseAppImplementation))]
namespace Plutus.iOS
{
    class CloseAppImplementation : Plutus.Helpers.Interface.ICloseApp
    {
        public void CloseApp()
        {
            Thread.CurrentThread.Abort();
        }
    }
}