using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.FirstTimeStartUp
{
	public partial class SetupView : ContentPage
	{
        private StackOrientation _orientationBase = StackOrientation.Horizontal;
        public SetupView ()
		{
			InitializeComponent ();
		}

        /// <summary>
        /// Run Base OnSizeAllocated and ensure that left and right columns are in best position,
        /// if <c>width < height</c> = Right column below Left;
        /// else Right column to Right of Left;
        /// </summary>
        /// <param name="width">Current width of Window</param>
        /// <param name="height">Current height of Window</param>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if(width < height)
            {
                if (_orientationBase == StackOrientation.Horizontal)
                {
                    Grid.SetRow(RightDataColumn, 2);
                    Grid.SetColumn(RightDataColumn, 0);
                    Grid.SetColumnSpan(RightDataColumn, 2);
                    Grid.SetColumnSpan(LeftDataColumn, 2);
                    _orientationBase = StackOrientation.Vertical;
                }
            }
            else
            {
                if (_orientationBase==StackOrientation.Vertical)
                {
                    Grid.SetColumnSpan(LeftDataColumn, 1);
                    Grid.SetColumnSpan(RightDataColumn, 1);
                    Grid.SetColumn(RightDataColumn, 1);
                    Grid.SetRow(RightDataColumn, 1);
                    _orientationBase = StackOrientation.Horizontal;
                }
            }
        }
    }
}