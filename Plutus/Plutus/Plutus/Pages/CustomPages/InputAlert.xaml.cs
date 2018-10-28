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
        public List<Tuple<string, bool>> InputResults { get; set; } = new List<Tuple<string, bool>>();
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

            MainLayout.WidthRequest = App.Current.MainPage.Width / 2;

            MainLayout.Children.Add(new Label { Text = titleText });

            for (int n = 0; n <= viewElements.Length - 1; n++)
            {
                this.ViewElements.Add(CreateLabelEntry(viewElements[n], n, this.MainLayout));
            }

            var confButton = new Button { Text = confirmButText };
            confButton.Clicked += ConfirmBut_ClickedAsync;
            MainLayout.Children.Add(confButton);

            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    InputResults.Add(Tuple.Create((item as Entry).Text, ((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item1));
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="titleText"></param>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <param name="cash"></param>
        /// <param name="toPay"></param>
        public InputAlert(string titleText, Tuple<string, string, Type, string, bool, bool>[] viewElements, string confirmButText, bool cash, decimal toPay)
        {
            InitializeComponent();

            MainLayout.WidthRequest = App.Current.MainPage.Width / 2;

            MainLayout.Children.Add(new Label { Text = titleText });

            if (cash)
                MainLayout.Children.Add(CreateCashGrid(toPay < 0.0m ? true : false));

            var payAllButton = new Button { Text = App.Translate.ProvideValue("PayFull"), CommandParameter = toPay };
            payAllButton.Clicked += PayExact_Clicked;
            MainLayout.Children.Add(payAllButton);

            for (int n = 0; n <= viewElements.Length - 1; n++)
            {
                this.ViewElements.Add(CreateLabelEntry(viewElements[n], n, this.MainLayout));
            }


            var confButton = new Button { Text = confirmButText };
            confButton.Clicked += ConfirmBut_ClickedAsync;
            MainLayout.Children.Add(confButton);

            foreach (var item in MainLayout.Children)
            {
                if (item is Entry)
                {
                    InputResults.Add(Tuple.Create((item as Entry).Text, ((item as Entry).ReturnCommandParameter as Tuple<bool, int>).Item1));
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="elementValues"></param>
        /// <param name="position"></param>
        /// <param name="layout"></param>
        /// <returns></returns>
        public Tuple<Label, Entry, Type, Label> CreateLabelEntry(Tuple<string, string, Type, string, bool, bool> elementValues, int position, StackLayout layout)
        {
            Label label = new Label { Text = elementValues.Item1 };
            Entry entry = new Entry { Placeholder = elementValues.Item2, IsPassword = elementValues.Item5, ReturnCommandParameter = Tuple.Create(elementValues.Item6, position) };
            entry.TextChanged += Entry_TextChanged;
            entry.Completed += Entry_Completed;
            Label labelValid = new Label { Text = elementValues.Item4, IsVisible = false };
            layout.Children.Add(label);
            layout.Children.Add(entry);
            layout.Children.Add(labelValid);
            return Tuple.Create(label, entry, elementValues.Item3, labelValid);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="negative"></param>
        /// <returns></returns>
        public Grid CreateCashGrid(bool negative)
        {
            var button1 = new Button { Text = "5", CommandParameter = negative ? -5 : 5 };
            var button2 = new Button { Text = "10", CommandParameter = negative ? -10 : 10 };
            var button3 = new Button { Text = "20", CommandParameter = negative ? -20 : 20 };
            var button4 = new Button { Text = "50", CommandParameter = negative ? -50 : 50 };

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
            grid.Children.Add(button2, 1, 0);
            grid.Children.Add(button3, 0, 1);
            grid.Children.Add(button4, 1, 1);

            return grid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Entry_TextChanged(object sender, TextChangedEventArgs e)
        {
            var updatedEntry = (sender as Entry);
            InputResults[(updatedEntry.ReturnCommandParameter as Tuple<bool, int>).Item2] =
                Tuple.Create(updatedEntry.Text, InputResults[(updatedEntry.ReturnCommandParameter as Tuple<bool, int>).Item2].Item2);
        }

        private void Entry_Completed(object sender, EventArgs e)
        {
            var entry = (sender as Entry);
            var viewE = ViewElements.Find(en => en.Item2.Equals(entry));

            if (ViewElements.FindAll(ve => ve.Item2 != null).Count - 1 == ViewElements.FindAll(ve => ve.Item2 != null).IndexOf(viewE))
            {
                ConfirmBut_ClickedAsync(null, null);
            }
            else
            {
                ViewElements.FindAll(ve => ve.Item2 != null)[ViewElements.IndexOf(viewE) + 1].Item2.SetFocusAfterDelay(1);
            }
        }

        private async void Value_Clicked(object sender, EventArgs e)
        {
            foreach (var input in ViewElements)
            {
                if (input.Item3 == typeof(decimal))
                {
                    var value = await input.Item2.Text.ToDecimal(App.Translate.ProvideValue("EnterCorrectValue")) ??
                                await ((Button)sender).CommandParameter.ToString()
                                    .ToDecimal(App.Translate.ProvideValue("EnterCorrectValue"));
                    value += await ((Button)sender).CommandParameter.ToString()
                        .ToDecimal(App.Translate.ProvideValue("EnterCorrectValue"));
                    input.Item2.Text = value.ToString();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PayExact_Clicked(object sender, EventArgs e)
        {
            foreach (var input in ViewElements)
            {
                if (input.Item3 == typeof(decimal))
                {
                    input.Item2.Text = ((Button)sender).CommandParameter.ToString();
                    ConfirmButtonEHandler?.Invoke(this, e);
                }
            }
        }
    }
}