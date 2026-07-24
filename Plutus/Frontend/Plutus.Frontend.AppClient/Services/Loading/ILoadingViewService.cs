using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Services.Loading
{
    public interface ILoadingViewService
    {
        void InitLoadingPage(ContentPage loadingIndicatorView);

        void ShowLoadingPage();

        void HideLoadingPage();

    }
}
