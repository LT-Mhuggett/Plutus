using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class InputAlert : ContentView
	{
        public EventHandler ConfirmButtonEHandler { get; set; }

        public string InputResult { get; set; }

		public InputAlert (string TitleText, string PlaceholderText, string ConfirmButText, string ValidationText)
		{
			InitializeComponent ();

            TitleL.Text = TitleText;
            InputE.Placeholder = PlaceholderText;
            ConfirmBut.Text = ConfirmButText;
            ValidationL.Text = ValidationText;

            ConfirmBut.Clicked += ConfirmBut_Clicked;
            InputE.TextChanged += InputE_TextChanged;
		}

        private void InputE_TextChanged(object sender, TextChangedEventArgs e)
        {
            InputResult = InputE.Text;
        }

        private void ConfirmBut_Clicked(object sender, EventArgs e)
        {
            ConfirmButtonEHandler?.Invoke(this, e);
        }

        public static readonly BindableProperty IsValidationLVisibleProp = BindableProperty.Create(
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
            get
            {
                return (bool)GetValue(IsValidationLVisibleProp);
            }
            set
            {
                SetValue(IsValidationLVisibleProp, value);
            }
        }
    }
}