using NatApp.Plutus.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoadingIndicatorView : ContentPage
	{
		public LoadingIndicatorView (AppViewModel appViewModel)
		{
			InitializeComponent ();

            BindingContext = appViewModel;
		}
	}
}