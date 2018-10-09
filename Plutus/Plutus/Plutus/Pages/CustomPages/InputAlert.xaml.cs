using System;
using System.Collections.Generic;
using Plutus.Helpers.Extensions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class InputAlert : ContentView
	{
        public EventHandler ConfirmButtonEHandler { get; set; }

        public List<string> InputResults { get; set; }
        public List<Tuple<Label, Entry, Label>> ViewElements { get; set; } = new List<Tuple<Label, Entry, Label>>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="titleText"></param>
        /// <param name="viewElements">Tuple<label, placeholder, validation, isPass></param>
        /// <param name="confirmButText"></param>
        public InputAlert(string titleText, Tuple<string, string, string, bool>[] viewElements, string confirmButText)
        {
            InitializeComponent();

            MainLayout.Children.Add(new Label { Text = titleText });

            foreach(Tuple<string, string, string, bool> viewData in viewElements)
            {
                this.ViewElements.Add(CreateLabelEntry(viewData));
            }
            var confButton = new Button { Text = confirmButText };
            confButton.Clicked += ConfirmBut_ClickedAsync;
            MainLayout.Children.Add(confButton);

            var i = 0;
            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    InputResults[i] = (item as Entry).Text;
                    i++;
                }
            }
        }
        public InputAlert(string titleText, Tuple<string, string, string, bool>[] viewElements, string confirmButText, bool cash, decimal toPay)
        {

        }

        public Tuple<Label, Entry, Label> CreateLabelEntry(Tuple<string, string, string, bool> elementValues)
        {
            Label label = new Label { Text = elementValues.Item1 };
            Entry entry = new Entry { Placeholder = elementValues.Item2, IsPassword = elementValues.Item4 };
            entry.TextChanged += Entry_TextChanged;
            Label labelValid = new Label { Text = elementValues.Item3, IsVisible = false };
            return Tuple.Create(label, entry, labelValid);
        }

        private void Entry_TextChanged(object sender, TextChangedEventArgs e)
        {
            var updatedEntry = (sender as Entry);
            var i = 0;
            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    if (item == updatedEntry)
                    {
                        InputResults[i] = (item as Entry).Text;
                    }
                    i++;
                }
            }
        }

        public InputAlert(string titleText, string placeholderText, string confirmButText, string validationText,
	        bool cashBack, decimal toPay)
	    {
	        InitializeComponent();

	        TitleL.Text = titleText;
	        InputE.Placeholder = placeholderText;
	        ConfirmBut.Text = confirmButText;
	        ValidationL.Text = validationText;
	        if (cashBack)
	        {
	            PayExact.CommandParameter = toPay;
	            PayExact.Clicked += PayExact_Clicked;
	            PayExact.Text = App.Translate.ProvideValue("PayFull");
	            PayExact.IsVisible = true;
	        }
	        else
	        {
	            PayExact.CommandParameter = toPay;
	            PayExact.Clicked += PayExact_Clicked;
	            PayExact.Text = App.Translate.ProvideValue("PayFull");
	            PayExact.IsVisible = true;
	            CashOptions.IsVisible = true;
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
            var passElements = ViewElements.FindAll(elements => elements.Item2.IsPassword);
            if (passElements.Count > 0)
            {
                if (!await passElements[0].Item2.Text.PasswordCheck(passElements[1].Item2.Text))
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
	        get => (bool) GetValue(IsValidationLVisibleProp);
	        set => SetValue(IsValidationLVisibleProp, value);
	    }
	}
}