using CustomViews.Structs;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Pages.CustomViews;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// "Which basket lines does this discount apply to?" — the multi-select behind the alterations
    /// flow.
    ///
    /// ⚠ NO LONGER SYNCFUSION (2026-08-10). Matt: *"I am not going to renew Syncfusion, it seems
    /// like it can be replaced."* This was an `SfListView` in multi-select mode and it is the LAST
    /// Syncfusion control on a path an operator reaches while selling — it is one tap from a
    /// discount, which is real money off a real basket. A control whose licence the library may
    /// start rejecting does not belong there.
    ///
    /// ⚠ WHAT CHANGED, honestly: MAUI's `CollectionView` has `Header`/`Footer` VIEWS rather than
    /// templates, and its footer SCROLLS with the content where `IsStickyFooter` pinned it. On a
    /// list of basket lines — a handful of rows, never a page — that is a difference nobody will
    /// see. Everything else is like for like.
    /// </summary>
    public class InputMultiSelectAlert<T> : InputAlert
    {
        #region Public Properties
        public static ObservableCollection<T> Items { get; set; }

        /// <summary>⚠ Kept STATIC, as it was, because the factory builds the view before the
        /// instance exists and `Initalize()` reaches it afterwards. It is a latent bug — two of
        /// these open at once would share one list — but this dialog is modal and serialised
        /// through `Modal`, and changing the lifetime is a bigger change than the licence removal
        /// this commit is doing.</summary>
        public static CollectionView SelectionList { get; private set; }

        public bool IsAllSelected =>
            SelectionList?.SelectedItems is not null &&
            Items is not null &&
            SelectionList.SelectedItems.Count == Items.Count &&
            Items.Count > 0;
        #endregion

        /// <summary>
        /// Constructor runs base InputAlert Constructor, Use factory instead.
        /// </summary>
        private InputMultiSelectAlert(
            IEnumerable<ViewElementData> viewElementsBefore,
            View selectionList,
            IEnumerable<ViewElementData> viewElementsAfter,
            string confirmButText, string title = null)
            : base(viewElementsBefore, selectionList, viewElementsAfter, confirmButText, title)
        {
        }

        /// <summary>
        /// Factory for creating InputMultiSelectAlert.
        /// </summary>
        /// <param name="itemsForList">Items for list, and the property path each row displays.</param>
        public static InputMultiSelectAlert<T> InputMultiSelectAlertFactory(
            IEnumerable<ViewElementData> viewElementsBefore,
            Tuple<IEnumerable<T>, string> itemsForList,
            IEnumerable<ViewElementData> viewElementsAfter,
            string confirmButText, string title = null)
        {
            Items = new ObservableCollection<T>(itemsForList.Item1);

            SelectionList = new CollectionView
            {
                ItemsSource = Items,
                SelectionMode = SelectionMode.Multiple,
                ItemTemplate = new DataTemplate(() =>
                {
                    var label = new Label { VerticalOptions = LayoutOptions.Center };
                    label.SetBinding(Label.TextProperty, itemsForList.Item2);
                    return label;
                }),
            };

            return new InputMultiSelectAlert<T>(viewElementsBefore, SelectionList, viewElementsAfter, confirmButText, title);
        }

        public void Initalize()
        {
            // ⚠ VIEWS, not DataTemplates. `CollectionView.Header`/`Footer` take a view directly;
            // handing them a DataTemplate compiles and renders NOTHING, silently, which is the MAUI
            // failure mode this codebase keeps meeting.
            SelectionList.Header = new Label { Text = "Items" };

            var button = new Button { Text = SelectAllText() };
            button.Clicked += SelectAll_UnselectAll_Clicked;
            SelectionList.Footer = button;

            SelectionList.SelectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(IsAllSelected));
                // ⚠ SET DIRECTLY rather than through a DataTrigger. The Syncfusion version bound
                // two triggers to `IsAllSelected` on a footer inside a template; a footer VIEW has
                // no binding context to reach this dialog from, so the label is assigned here —
                // where it is also obvious that it changes.
                button.Text = SelectAllText();
            };
        }

        private string SelectAllText() => (IsAllSelected ? "UnselectAll" : "SelectAll").Translate();

        public IEnumerable<T> SelectedItems()
        {
            var selected = new List<T>();
            if (SelectionList?.SelectedItems is null) return selected;

            foreach (var item in SelectionList.SelectedItems)
                if (item is T confItem) selected.Add(confItem);

            return selected;
        }

        #region Events
        /// <summary>
        /// Select all / unselect all.
        ///
        /// ⚠ A NEW LIST IS ASSIGNED, not mutated in place. `CollectionView.SelectedItems` raises
        /// its change notification from the SETTER; adding to the existing collection updates the
        /// count but leaves the rows unhighlighted, so the operator sees "Unselect all" over a list
        /// that looks untouched.
        /// </summary>
        private void SelectAll_UnselectAll_Clicked(object sender, EventArgs e)
        {
            SelectionList.SelectedItems = IsAllSelected
                ? new List<object>()
                : Items.Cast<object>().ToList();

            OnPropertyChanged(nameof(IsAllSelected));
            if (sender is Button button) button.Text = SelectAllText();
        }
        #endregion
    }
}
