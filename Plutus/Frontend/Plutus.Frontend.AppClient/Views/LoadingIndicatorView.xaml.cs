using Plutus.Frontend.AppClient.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views
{
	public partial class LoadingIndicatorView : ContentPage
	{
		public LoadingIndicatorView (AppViewModel appViewModel)
		{
			InitializeComponent ();

            BindingContext = appViewModel;
		}
	}
}