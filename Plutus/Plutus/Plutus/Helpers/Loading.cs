using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    class Loading
    {
        public static void TogleLoading(ContentView view, ActivityIndicator activInd)
        {
            view.IsVisible = !view.IsVisible;
            activInd.IsRunning = !activInd.IsRunning;
        }
    }
}
