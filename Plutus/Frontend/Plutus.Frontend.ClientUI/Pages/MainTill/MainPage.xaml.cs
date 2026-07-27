using CommunityToolkit.Mvvm.Messaging;
using Plutus.Frontend.ClientUI.Core.Messages;

namespace Plutus.Frontend.ClientUI.Pages.MainTill;
public partial class MainPage
{
	private bool _loaded = false;
	public MainPage()
	{
		InitializeComponent();

		if (DeviceInfo.Idiom == DeviceIdiom.Phone)
			Shell.Current.CurrentItem = PhoneTabs;
	}

	private void TapGestureRecognizer_Tapped(object sender, EventArgs e)
	{

	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		if (_loaded) return;

		_loaded = true;
		WeakReferenceMessenger.Default.Send(new MainUILoadedMessage());
	}
}