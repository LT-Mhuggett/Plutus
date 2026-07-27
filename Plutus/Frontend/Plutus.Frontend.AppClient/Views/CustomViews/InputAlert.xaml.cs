using CustomViews.Control;
using Microsoft.Maui.Devices;
using CustomViews.Structs;
using Plutus.Frontend.AppClient.Behaviors;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Pages.CustomViews
{
    public partial class InputAlert : ContentView
    {
        #region Properties
        public EventHandler ConfirmButtonEHandler { get; set; }
        public Dictionary<uint, string> InputResults { get; set; } = new Dictionary<uint, string>();
        public List<ViewElement> ViewElements { get; set; } = new List<ViewElement>();
        public ValidationGroupBehavior ValidationGroup;
        private readonly decimal _targetAmount;
        public Button ConfBut;
        public Button CancelBut;
        #endregion

        #region Constructors
        /// <summary>
        /// Creates an InuptAlert window with unlimited amount of viewElements
        /// </summary>
        /// <param name="viewElements"><see cref="CreateLabelEntry(Tuple{string, string, IEnumerable{IValidator}, bool, bool}, StackLayout)"/></param>
        /// <param name="confirmButText">Text for button</param>
        /// <param name="title">Title for the view; Can be nullable</param>
        public InputAlert(IEnumerable<ViewElementData> viewElements, string confirmButText, string title = null, string cancelButText = null)
        {
            InitializeComponent();

            if (title != null)
                MainLayout.Children.Add(new Label()
                {
                    Text = title,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    FontSize = new Label().FontSize,
                    FontAttributes = FontAttributes.Bold
                });

            //Create entire form validation group
            ValidationGroup = new ValidationGroupBehavior();
            MainLayout.Behaviors.Add(ValidationGroup);

            //Loop through all queue and create elements for form
            for (int n = 0; n < viewElements.Count(); n++)
            {
                ViewElements.Add(CreateLabelEntry(viewElements.Skip(n).First(), MainLayout));

                if (ViewElements.Count > 1)
                    SetOnComplete(ViewElements.ElementAt(ViewElements.Count - 2).Entry, ViewElements.Last().Entry);

                if (viewElements.Skip(n).Any())
                    SetOnComplete(ViewElements.Last().Entry);
            }


            //Create confirm button
            ConfBut = new Button { Text = confirmButText };
            ConfBut.Clicked += ConfirmBut_Clicked;
            ConfBut.SetBinding(IsEnabledProperty, "ValidationGroup.IsValid");
            MainLayout.Children.Add(ConfBut);

            //Cancel button
            if (cancelButText != null)
            {
                CancelBut = new Button { Text = cancelButText };
                CancelBut.Clicked += CancelBut_Clicked;
                MainLayout.Children.Add(CancelBut);
            }

            //init input results 
            foreach (var view in MainLayout.Children)
            {
                if (view is IdentifiableEntry entry)
                    InputResults.Add(entry.UserDefinedId, entry.Text ?? "");
            }
        }

        /// <summary>
        /// Creates an InputAlert window with unlimited ViewElements, with a view inbetween
        /// </summary>
        /// <param name="viewElementsBefore">ViewElements before View<see cref="CreateLabelEntry(Tuple{string, string, IEnumerable{IValidator}, bool, bool}, StackLayout)"/></param>
        /// <param name="view">View to place in</param>
        /// <param name="viewElementsAfter">ViewElements after View<see cref="CreateLabelEntry(Tuple{string, string, IEnumerable{IValidator}, bool, bool}, StackLayout)"/></param>
        /// <param name="confirmButText">Confirm button text</param>
        /// <param name="title">Title of window</param>
        public InputAlert(
            IEnumerable<ViewElementData> viewElementsBefore,
            View view,
            IEnumerable<ViewElementData> viewElementsAfter,
            string confirmButText, string title = null, string cancelButText = null)
        {
            InitializeComponent();

            if (title != null)
                MainLayout.Children.Add(new Label()
                {
                    Text = title,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    FontSize = new Label().FontSize,
                    FontAttributes = FontAttributes.Bold
                });

            //Create entire form validation group
            ValidationGroup = new ValidationGroupBehavior();
            MainLayout.Behaviors.Add(ValidationGroup);


            if (viewElementsBefore != default)
            {
                //Loop through all queue and create elements for form
                for (int n = 0; n < viewElementsBefore.Count(); n++)
                {
                    ViewElements.Add(CreateLabelEntry(viewElementsBefore.Skip(n).First(), MainLayout));

                    if (ViewElements.Count() > 1)
                        SetOnComplete(ViewElements.ElementAt(ViewElements.Count - 2).Entry, ViewElements.Last().Entry);

                    if (viewElementsBefore.Skip(n).Any())
                        SetOnComplete(ViewElements.Last().Entry);
                }
            }

            //Add the View to MainLayout
            MainLayout.Children.Add(view);

            if (viewElementsAfter != default)
            {
                //Loop through all queue and create elements for form
                for (int n = 0; n < viewElementsAfter.Count(); n++)
                {
                    ViewElements.Add(CreateLabelEntry(viewElementsAfter.Skip(n).First(), MainLayout));

                    if (ViewElements.Count() > 1)
                        SetOnComplete(ViewElements.ElementAt(ViewElements.Count - 2).Entry, ViewElements.Last().Entry);

                    if (viewElementsAfter.Skip(n).Any())
                        SetOnComplete(ViewElements.Last().Entry);
                }
            }

            //Create confirm button
            ConfBut = new Button { Text = confirmButText };
            ConfBut.Clicked += ConfirmBut_Clicked;
            ConfBut.SetBinding(IsEnabledProperty, "ValidationGroup.IsValid");
            MainLayout.Children.Add(ConfBut);

            //Cancel button
            if (cancelButText != null)
            {
                CancelBut = new Button { Text = cancelButText };
                CancelBut.Clicked += CancelBut_Clicked;
                MainLayout.Children.Add(CancelBut);
            }

            ValidationGroup.Update();

            //init input results 
            foreach (var tmpView in MainLayout.Children)
            {
                if (tmpView is IdentifiableEntry element)
                    InputResults.Add(element.UserDefinedId, element.Text ?? "");
            }
        }

        /// <summary>
        /// Creates an InuptAlert window with unlimited amount of viewElements,
        /// also generates a Grid for cash transactions and pay all option
        /// </summary>
        /// <remarks>
        /// First viewElemnt must be the decimal return value
        /// </remarks>
        /// <param name="viewElements"><see cref="CreateLabelEntry(Tuple{string, string, IEnumerable{IValidator}, bool, bool}, StackLayout)"/></param>
        /// <param name="confirmButText">Text for Button</param>
        /// <param name="cash">Is this a cash transaction</param>
        /// <param name="toPay">Amount to pay or be returned</param>
        /// <param name="title">Title for the view; Can be nullable</param>
        public InputAlert(IEnumerable<ViewElementData> viewElements, string confirmButText, bool cash, decimal toPay, string title = null, string cancelButText = null)
        {
            InitializeComponent();

            if (title != null)
                MainLayout.Children.Add(new Label()
                {
                    Text = title,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    FontSize = new Label().FontSize,
                    FontAttributes = FontAttributes.Bold
                });

            //Create entire form validation group
            ValidationGroup = new ValidationGroupBehavior();
            MainLayout.Behaviors.Add(ValidationGroup);

            if (cash)
                MainLayout.Children.Add(CreateCashGrid(toPay < 0.0m));
            var payAllBut = new Button { Text = "PayFull".Translate(), CommandParameter = _targetAmount = toPay };
            payAllBut.Clicked += PayExact_Clicked;
            MainLayout.Children.Add(payAllBut);

            //Loop through all queue and create elements for form
            for (int n = 0; n < viewElements.Count(); n++)
            {
                ViewElements.Add(CreateLabelEntry(viewElements.Skip(n).First(), MainLayout));

                if (ViewElements.Count > 1)
                    SetOnComplete(ViewElements.ElementAt(ViewElements.Count - 2).Entry, ViewElements.Last().Entry);

                if (viewElements.Skip(n).Any())
                    SetOnComplete(ViewElements.Last().Entry);
            }

            //Create confirm button
            ConfBut = new Button { Text = confirmButText };
            ConfBut.Clicked += ConfirmBut_Clicked;
            ConfBut.SetBinding(IsEnabledProperty, "ValidationGroup.IsValid");
            MainLayout.Children.Add(ConfBut);

            //Cancel button
            if (cancelButText != null)
            {
                CancelBut = new Button { Text = cancelButText };
                CancelBut.Clicked += CancelBut_Clicked;
                MainLayout.Children.Add(CancelBut);
            }

            //init input results 
            foreach (var view in MainLayout.Children)
            {
                if (view is IdentifiableEntry element)
                    InputResults.Add(element.UserDefinedId, element.Text ?? "");
            }
        }
        #endregion

        #region Element Generators
        /// <summary>
        /// Create Label and Entry pair for inputs
        /// </summary>
        /// <param name="elementValue">Label, Placeholder, Validators, IsPassword, IsEnabled</param>
        /// <param name="layout">Layout to add elements to</param>
        /// <returns>Label, Entry pair</returns>
        public ViewElement CreateLabelEntry(ViewElementData elementValue, StackLayout layout)
        {
            var label = new Label { Text = elementValue.LabelText };
            IdentifiableEntry entry = elementValue.IsEnabled
                ? new IdentifiableEntry { UserDefinedId = elementValue.Id, Placeholder = elementValue.PlaceholderText, IsPassword = elementValue.IsPassword, IsEnabled = elementValue.IsEnabled }
                : new IdentifiableEntry { UserDefinedId = elementValue.Id, Text = elementValue.PlaceholderText, IsPassword = elementValue.IsPassword, IsEnabled = elementValue.IsEnabled };
            if (elementValue.Validators.Count() != 0)
            {
                var vBehavior = new ValidationBehavior
                {
                    Group = ValidationGroup,
                    PropertyName = "Text"
                };
                entry.Behaviors.Add(vBehavior);
                foreach (var validator in elementValue.Validators)
                {
                    if (validator is IValidatorReqReference validatorReq)
                        validatorReq.ReferenceEntry = ViewElements.Last().Entry;
                    (entry.Behaviors.Last() as ValidationBehavior).Validators.Add((IValidator)validator);
                }
            }

            entry.TextChanged += Entry_TextChanged;

            layout.Children.Add(label);
            layout.Children.Add(entry);
            return new ViewElement(label, entry);
        }

        /// <summary>
        /// Setup OnComplete Events
        /// </summary>
        /// <param name="fromEntry"></param>
        /// <param name="toEntry"></param>
        private void SetOnComplete(Entry fromEntry, Entry toEntry = null)
        {
            if (toEntry == null)
                fromEntry.Completed += ConfirmBut_Clicked;
            else
                fromEntry.Completed += (sender, e) => ToNextEntryEvent(sender, e, toEntry);
        }

        /// <summary>
        /// Set focus on next entry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <param name="toEntry"></param>
        private void ToNextEntryEvent(object sender, EventArgs e, Entry toEntry)
        {
            toEntry.Focus();
        }

        /// <summary>
        /// Creates a cash selection grid
        /// </summary>
        /// <param name="negative">Is the value negative</param>
        /// <returns>Cash selection grid</returns>
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

            grid.Add(button1, 0, 0);
            grid.Add(button2, 1, 0);
            grid.Add(button3, 0, 1);
            grid.Add(button4, 1, 1);

            return grid;
        }
        #endregion

        #region Events
        /// <summary>
        /// Update InputResult value when entry is changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Entry_TextChanged(object sender, TextChangedEventArgs e)
        {
            var updatedEntry = sender as IdentifiableEntry;
            InputResults[updatedEntry.UserDefinedId] = e.NewTextValue;
        }

        /// <summary>
        /// Update the decimal return value with the cash button return,
        /// if amount is above of equals required auto submit form
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Value_Clicked(object sender, EventArgs e)
        {
            try
            {
                var input = ViewElements.First().Entry;
                var tempVal = decimal.Parse(string.IsNullOrEmpty(input.Text) ? "0" : input.Text);
                tempVal += decimal.Parse((sender as Button).CommandParameter.ToString());
                input.Text = tempVal.ToString();
                if (tempVal >= _targetAmount && ValidationGroup.IsValid)
                    ConfirmButtonEHandler?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Trigger Confirmation of form
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ConfirmBut_Clicked(object sender, EventArgs e)
        {
            if (ValidationGroup.IsValid)
                ConfirmButtonEHandler?.Invoke(this, e);
        }

        private void CancelBut_Clicked(object sender, EventArgs e)
        {
            ViewElements.ForEach(vE => vE.Entry.Text = default);
            ConfirmButtonEHandler?.Invoke(this, e);
        }

        /// <summary>
        /// Overide the input value and auto, submit form
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PayExact_Clicked(object sender, EventArgs e)
        {
            ViewElements.First().Entry.Text = (sender as Button).CommandParameter.ToString();
            if (ValidationGroup.IsValid)
                ConfirmButtonEHandler?.Invoke(this, e);
        }
        #endregion
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            MainLayout.WidthRequest = Application.Current.MainPage.Width / 2;
            MainLayout.HeightRequest = Application.Current.MainPage.Height / 2;
        }
    }
}