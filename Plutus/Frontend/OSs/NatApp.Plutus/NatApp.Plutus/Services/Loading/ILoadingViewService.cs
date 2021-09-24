using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Services.Loading
{
    public interface ILoadingViewService
    {
        void InitLoadingPage(ContentPage loadingIndicatorView);

        void ShowLoadingPage();

        void HideLoadingPage();

    }
}
