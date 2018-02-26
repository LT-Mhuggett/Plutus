using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Helpers.Extensions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class InputAlert : ContentView
	{
        public EventHandler ConfirmButtonEHandler { get; set; }

        public string InputResult { get; set; }

	    public InputAlert(string titleText, string placeholderText, string confirmButText, string validationText,
	        bool cash, decimal toPay)
	    {
	        InitializeComponent();

	        TitleL.Text = titleText;
	        InputE.Placeholder = placeholderText;
	        ConfirmBut.Text = confirmButText;
	        ValidationL.Text = validationText;
	        if (cash)
	            CashOptions.IsVisible = true;
	        else
	        {
	            PayExact.CommandParameter = toPay;
	            PayExact.Clicked += PayExact_Clicked;
	            PayExact.Text = App.Translate.ProvideValue("PayFull");
	            PayExact.IsVisible = true;
	        }
	        ConfirmBut.Clicked += ConfirmBut_ClickedAsync;
	        InputE.TextChanged += InputE_TextChanged;

	    }

	    public InputAlert(string titleText, string placeholderText, string confirmButText, string validationText,
	        bool isPass)
	    {
	        InitializeComponent();

	        TitleL.Text = titleText;
	        InputE.Placeholder = placeholderText;
	        ConfirmBut.Text = confirmButText;
	        ValidationL.Text = validationText;
	        InputE.IsPassword = isPass;
	        InputEConf.IsPassword = isPass;
	        InputEConf.IsVisible = isPass;
	        ConfirmBut.Clicked += ConfirmBut_ClickedAsync;
	        InputE.TextChanged += InputE_TextChanged;
	    }

	    private async void Value_Clicked(object sender, EventArgs e)
	    {
	        var value = await InputE.Text.ToDecimal(App.Translate.ProvideValue("EnterCorrectValue")) ??
	                    await ((Button) sender).CommandParameter.ToString()
	                        .ToDecimal(App.Translate.ProvideValue("EnterCorrectValue"));
	        value += await ((Button) sender).CommandParameter.ToString()
	            .ToDecimal(App.Translate.ProvideValue("EnterCorrectValue"));
            InputE.Text = value.ToString();
	    }

        private void InputE_TextChanged(object sender, TextChangedEventArgs e)
        {
            InputResult = InputE.Text;
        }

        private async void ConfirmBut_ClickedAsync(object sender, EventArgs e)
        {
            if (InputEConf.IsVisible)
            {
                if (!await InputE.Text.PasswordCheck(InputEConf.Text))
                {
                    return;
                }
            }
            ConfirmButtonEHandler?.Invoke(this, e);
        }

	    private void PayExact_Clicked(object sender, EventArgs e)
	    {
	        InputE.Text = ((Button) sender).CommandParameter.ToString();
	        ConfirmButtonEHandler?.Invoke(this, e);
	    }

	    private static readonly BindableProperty IsValidationLVisibleProp = BindableProperty.Create(
            nameof(IsValidationLVisibleProp),
            typeof(bool),
            typeof(InputAlert),
            false,
            BindingMode.OneWay,
            null,
            (bindable, value, newValue) =>
            {
                if ((bool)newValue)
                {
                    ((InputAlert)bindable).ValidationL.IsVisible = true;
                }
                else
                {
                    ((InputAlert)bindable).ValidationL.IsVisible = false;
                }
            }
        );

        public bool IsValidationLVisable
        {
            get => (bool)GetValue(IsValidationLVisibleProp);
            set => SetValue(IsValidationLVisibleProp, value);
        }
    }
}