using Plutus.Frontend.AppClient.ViewModels.MainTill.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.Statistics
{
    public partial class StatisticsView : ContentPage
    {
        private StackOrientation _orientationBase = StackOrientation.Horizontal;
        private readonly StatisticsViewModel _vm;

        /// <summary>
        /// ⚠⚠ THE FIGURES WERE READ ONCE, AT SIGN-IN, AND NEVER AGAIN — finding N. `AppShell` builds
        /// every tab up front, so today's takings were fixed at the moment somebody signed in and a
        /// whole day of selling never moved them. **A takings figure silently hours stale is worse
        /// than none: it is the number a manager counts a drawer against**, and a wrong total looks
        /// exactly like a right one.
        ///
        /// ⚠ Now on `LiveScreen`, which reloads on appearing AND on the 60-second cadence. The
        /// hand-rolled version here was correct; item 7 exists because being correct on one screen at
        /// a time taught the others nothing — see `LiveScreen`'s header.
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public StatisticsView()
        {
            InitializeComponent();
            BindingContext = _vm = new StatisticsViewModel(LeftColumn, RightColumn);
            _live = new Services.Sync.LiveScreen(this, _vm.LoadToday);
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width < height)
            {
                if (_orientationBase == StackOrientation.Horizontal)
                {
                    Grid.SetRow(RightColumn, 1);
                    Grid.SetColumn(RightColumn, 0);
                    Grid.SetColumnSpan(RightColumn, 2);
                    Grid.SetColumnSpan(LeftColumn, 2);
                    _orientationBase = StackOrientation.Vertical;
                }
            }
            else
            {
                if (_orientationBase == StackOrientation.Vertical)
                {
                    Grid.SetColumnSpan(LeftColumn, 1);
                    Grid.SetColumnSpan(RightColumn, 1);
                    Grid.SetColumn(RightColumn, 1);
                    Grid.SetRow(RightColumn, 0);
                    _orientationBase = StackOrientation.Horizontal;
                }
            }
        }
    }
}
