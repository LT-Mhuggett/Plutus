using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    internal static class Loading
    {
        public static void TogleLoading(this ScrollView mainView, ContentView view, ActivityIndicator activInd)
        {
            mainView.IsEnabled = !mainView.IsEnabled;
            view.IsVisible = !view.IsVisible;
            activInd.IsRunning = !activInd.IsRunning;
        }

        public static void TogleLoading(this StackLayout mainView, ContentView view, ActivityIndicator activInd)
        {
            mainView.IsEnabled = !mainView.IsEnabled;
            view.IsVisible = !view.IsVisible;
            activInd.IsRunning = !activInd.IsRunning;
        }
    }
}
