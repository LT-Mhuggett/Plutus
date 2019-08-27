using NatApp.Plutus.Collections;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.ViewModels;
using Plugin.Iconize;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Views
{
    /// <summary>
    /// This is required as a fix for the current Iconizer UWP redrawing issue that causes icons to disappear
    /// </summary>
    public sealed class FixIconizeNavigationView : IconNavigationPage
    {
        public FixIconizeNavigationView() : base()
        {
            Init();
        }

        public FixIconizeNavigationView(Page root) : base(root)
        {
            Init();
        }

        private void Init()
        {
            BarBackgroundColor = Color.Accent;
            BarTextColor = Color.White;
            Pushed += (sender, e) => InitializeToolbarIconRefresh(e.Page);
            ((TabbedPage)RootPage).CurrentPageChanged += (sender, e) => RefreshToolbarItems();
            App.GetViewModel().ToolbarItemChanged += RefreshToolbarItems;
        }

        private void InitializeToolbarIconRefresh(Page page)
        {
            if (!(page is TabbedPage tabbedPage)) return;

            foreach (var child in tabbedPage.Children)
            {
                child.Appearing += (sender, e) => { RefreshToolbarItems(); };
            }
        }

        private void RefreshToolbarItems()
        {
            App.GetViewModel().ToolbarItemsChanged = false;
            foreach (var item in App.Current.MainPage.ToolbarItems)
                if (item is IconToolbarItem iconItem)
                {
                    var ogVis = iconItem.IsVisible;
                    iconItem.IsVisible = false;
                    iconItem.IsVisible = ogVis;
                }
        }
    }
}
