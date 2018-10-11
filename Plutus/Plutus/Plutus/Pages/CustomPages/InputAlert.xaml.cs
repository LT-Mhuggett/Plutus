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
        public string InputResult { get; set; }
        public List<Tuple<string, bool>> InputResults { get; set; }
        public List<Tuple<Label, Entry, Type, Label>> ViewElements { get; set; } = new List<Tuple<Label, Entry, Type, Label>>();
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="titleText"></param>
        /// <param name="viewElements">Tuple<label, placeholder, validation, isPass></param>
        /// <param name="confirmButText"></param>
        public InputAlert(string titleText, Tuple<string, string, Type, string, bool, bool>[] viewElements, string confirmButText)
        {
            InitializeComponent();

            MainLayout.Children.Add(new Label { Text = titleText });

            for(int n = 0; n <= viewElements.Length-1; n++)
            {
                this.ViewElements.Add(CreateLabelEntry(viewElements[n], n));
            }

            var confButton = new Button { Text = confirmButText };
            confButton.Clicked += ConfirmBut_ClickedAsync;
            MainLayout.Children.Add(confButton);
            
            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    InputResults[((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item2] = 
                        Tuple.Create((item as Entry).Text, ((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item1);
                }
            }
        }
        public InputAlert(string titleText, Tuple<string, string, Type, string, bool, bool>[] viewElements, string confirmButText, bool cash, decimal toPay)
        {
            InitializeComponent();

            MainLayout.Children.Add(new Label { Text = titleText });

            for (int n = 0; n <= viewElements.Length - 1; n++)
            {
                this.ViewElements.Add(CreateLabelEntry(viewElements[n], n));
            }

            if(cash)
                MainLayout.Children.Add(CreateCashGrid());

            var payAllButton = new Button { Text = App.Translate.ProvideValue("PayFull"), CommandParameter = toPay };
            payAllButton.Clicked += PayExact_Clicked;
            MainLayout.Children.Add(payAllButton);

            var confButton = new Button { Text = confirmButText };
            confButton.Clicked += ConfirmBut_ClickedAsync;
            MainLayout.Children.Add(confButton);

            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    InputResults[((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item2] =
                        Tuple.Create((item as Entry).Text, ((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item1);
                }
            }
        }

        public Tuple<Label, Entry, Type, Label> CreateLabelEntry(Tuple<string, string, Type, string, bool, bool> elementValues, int position)
        {
            Label label = new Label { Text = elementValues.Item1 };
            Entry entry = new Entry { Placeholder = elementValues.Item2, IsPassword = elementValues.Item5, ReturnCommandParameter = Tuple.Create(elementValues.Item6, position) };
            entry.TextChanged += Entry_TextChanged;
            Label labelValid = new Label { Text = elementValues.Item4, IsVisible = false };
            return Tuple.Create(label, entry, elementValues.Item3, labelValid);
        }

        public Grid CreateCashGrid()
        {
            var button1 = new Button { Text = "5", CommandParameter = 5 };
            var button2 = new Button { Text = "10", CommandParameter = 10 };
            var button3 = new Button { Text = "20", CommandParameter = 20 };
            var button4 = new Button { Text = "50", CommandParameter = 50 };

            button1.Clicked += Value_Clicked;
            button2.Clicked += Value_Clicked;
            button3.Clicked += Value_Clicked;
            button4.Clicked += Value_Clicked;

            var grid = new Grid
            {
                RowDefinitions = {
                    new RowDefinition{Height = new GridLength(60)},
                    new RowDefinition{Height = new GridLength(60)}
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition{Width = new GridLength(1, GridUnitType.Star)},
                    new ColumnDefinition{Width = new GridLength(1, GridUnitType.Star)}
                }
            };

            grid.Children.Add(button1, 0, 0);
            grid.Children.Add(button2, 0, 1);
            grid.Children.Add(button3, 0, 0);
            grid.Children.Add(button4, 1, 0);

            return grid;
        }

        private void Entry_TextChanged(object sender, TextChangedEventArgs e)
        {
            var updatedEntry = (sender as Entry);
            InputResults[(updatedEntry.ReturnCommandParameter as Tuple<bool, int>).Item2] =
                Tuple.Create(updatedEntry.Text, InputResults[(updatedEntry.ReturnCommandParameter as Tuple<bool, int>).Item2].Item2);
        }
        /*
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
        */

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