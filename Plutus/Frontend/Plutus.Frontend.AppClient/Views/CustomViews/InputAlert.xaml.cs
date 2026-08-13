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
        /// <summary>
        /// â  A FIELD, WHICH MEANS THE `IsEnabled` BINDING ONTO IT IS DEAD. MAUI bindings resolve
        /// against properties; `SetBinding(IsEnabledProperty, "ValidationGroup.IsValid")` therefore
        /// never resolves, and `IsEnabled` keeps its default â **true**. So per-field validation
        /// still shows its errors, but it does NOT gate Confirm.
        ///
        /// â  DELIBERATELY LEFT THAT WAY FOR NOW (2026-08-11). Making it a property would suddenly
        /// start gating a button on this screen â the same screen Matt has just reported as broken â
        /// and there is no UI host in this repo to test that with. A validation gate that is wrong
        /// leaves an operator with a Confirm that does nothing and no explanation, which is worse
        /// than the current state: every caller re-parses its own input and refuses with a message
        /// (`ExecuteOpenEditItem`, `ExecuteCreateItem`, the tender prompt), so nothing invalid gets
        /// through regardless.
        ///
        /// â  Recorded in `Build/archive/handrun-2026-08-11.md` rather than silently fixed.
        /// </summary>
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
            // ⚠ EVALUATE THE PRE-FILLED VALUES NOW. `IsValid` defaults to false and only one
            // constructor ever called `Update()`, so the group’s idea of validity did not reflect the
            // text actually in the boxes until somebody typed. Harmless today (the binding above is
            // dead — see the ValidationGroup field), and it is what makes gating the button possible
            // later without the gate starting out wrong.
            ValidationGroup.Update();
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
            // ⚠ EVALUATE THE PRE-FILLED VALUES NOW. `IsValid` defaults to false and only one
            // constructor ever called `Update()`, so the group’s idea of validity did not reflect the
            // text actually in the boxes until somebody typed. Harmless today (the binding above is
            // dead — see the ValidationGroup field), and it is what makes gating the button possible
            // later without the gate starting out wrong.
            ValidationGroup.Update();
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
            // ⚠ EVALUATE THE PRE-FILLED VALUES NOW. `IsValid` defaults to false and only one
            // constructor ever called `Update()`, so the group’s idea of validity did not reflect the
            // text actually in the boxes until somebody typed. Harmless today (the binding above is
            // dead — see the ValidationGroup field), and it is what makes gating the button possible
            // later without the gate starting out wrong.
            ValidationGroup.Update();
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
            // ⚠ THE VALUE GOES IN AS `Text` WHEN THE CALLER ASKS FOR IT, and that is finding K.
            // A DISABLED field has always had its value as `Text` (there is nothing else it could
            // be). An ENABLED one got a `Placeholder` — grey ghost text — so an EDIT form showed its
            // five editable rows as empty boxes and its three read-only rows as the only ones with
            // anything in them. That is "I can ONLY change the tax", exactly as reported.
            //
            // ⚠ And `InputResults` reads `entry.Text`, so a placeholder submits as "". An operator
            // who retyped only the price sent an empty name and the save was dropped in silence.
            //
            // ⚠ `PrefillWithPlaceholder` is OPT-IN so the sign-in and password forms keep hints as
            // hints — see its own header.
            var prefill = elementValue.PrefillWithPlaceholder || !elementValue.IsEnabled;

            IdentifiableEntry entry = prefill
                ? new IdentifiableEntry { UserDefinedId = elementValue.Id, Text = elementValue.PlaceholderText, IsPassword = elementValue.IsPassword, IsEnabled = elementValue.IsEnabled }
                : new IdentifiableEntry { UserDefinedId = elementValue.Id, Placeholder = elementValue.PlaceholderText, IsPassword = elementValue.IsPassword, IsEnabled = elementValue.IsEnabled };
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
        /// <summary>
        /// â  THIS COULD SIZE THE DIALOG TO NOTHING, and a dialog with no size over a 40%-black
        /// scrim is an app that has gone dark with no way out. It read:
        ///
        ///     MainLayout.WidthRequest  = Application.Current.MainPage.Width  / 2;
        ///     MainLayout.HeightRequest = Application.Current.MainPage.Height / 2;
        ///
        /// `VisualElement.Width` and `.Height` are **-1 until the element has been arranged**, so
        /// during the first layout pass â which is the one that matters, because that is when the
        /// popup appears â those assignments are `-0.5`. It also reaches for
        /// `Application.Current.MainPage`, which is not this dialog's parent and may be null or a
        /// different page entirely while a Mopups popup is up.
        ///
        /// Now sized from the values the layout actually PASSES IN, and only when they are real.
        /// A dialog that cannot work out how big it should be must fall back to its natural size â
        /// never to zero.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0)
                MainLayout.WidthRequest = Math.Max(320, width / 2);

            // â  Height is a MAXIMUM, not a request. Forcing half the window onto a stack holding a
            // cash grid, entries and up to three buttons clipped the buttons off the bottom of the
            // payment dialog on a short window â including Confirm.
            //
            // â  THE CAP GOES ON THE SCROLLER, NOT ON THE STACK. Capping the inner stack caps the
            // CONTENT, which leaves the scroller nothing to scroll and clips exactly as before â
            // which is how the item editor lost five of its eight fields and was reported as
            // *"I can ONLY change the tax"* (Matt, 2026-08-11). The stack must be free to be
            // taller than the window; the scroller is what makes that reachable.
            if (height > 0)
                Scroller.MaximumHeightRequest = height * 0.9;
        }

        /// <summary>
        /// â  BELT AND BRACES ON VISIBILITY. Mopups animates the popup in and restores
        /// <c>Content.Opacity</c> at the end of that animation (<see cref="CustomViews
        /// .AlertDialogBase{T}.OnAppearingAnimationEndAsync"/>). If the animation does not complete
        /// â and it is the platform's, not ours â the content stays transparent while the scrim
        /// does not, which looks exactly like the app freezing. Nothing about this dialog is worth
        /// leaving to an animation callback.
        /// </summary>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            Opacity = 1;
            MainLayout.Opacity = 1;
            IsVisible = true;
        }
    }
}