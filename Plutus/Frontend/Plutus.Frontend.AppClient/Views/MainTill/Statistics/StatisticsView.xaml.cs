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

        public StatisticsView()
        {
            InitializeComponent();
            BindingContext = _vm = new StatisticsViewModel(LeftColumn, RightColumn);
        }

        /// <summary>
        /// ⚠ RELOAD THE FIGURES — they were read ONCE, at sign-in, and never again. `AppShell` builds
        /// every tab up front, so today's takings were fixed at the moment somebody signed in and a
        /// whole day of selling never moved them. A takings figure that is silently hours stale is
        /// worse than none: it is the number a manager counts a drawer against.
        ///
        /// ⚠ Same fault as the cash tab's stuck "(waiting to send)" (Matt, 2026-08-11) — this screen
        /// just had no visible clue, because a wrong total looks exactly like a right one.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm.LoadToday();
            Services.Sync.TillCadence.Ticked += OnTicked;
        }

        /// <summary>
        /// ⚠ UNSUBSCRIBE, ALWAYS — `Ticked` is STATIC, and a page that stays attached is kept alive
        /// for the life of the process along with every till lookup it makes each minute.
        /// </summary>
        protected override void OnDisappearing()
        {
            Services.Sync.TillCadence.Ticked -= OnTicked;
            base.OnDisappearing();
        }

        /// <summary>
        /// ⚠ Arrives on the cadence loop's thread. `LoadToday` does its own work off-thread and
        /// marshals its own redraw, and it cannot throw — so this hands straight over.
        /// </summary>
        private void OnTicked() => _vm.LoadToday();

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