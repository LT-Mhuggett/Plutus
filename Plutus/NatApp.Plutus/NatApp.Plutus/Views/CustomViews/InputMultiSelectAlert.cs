using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Helpers.Validators;
using NatApp.Plutus.Pages.CustomViews;
using Syncfusion.ListView.XForms;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Views.CustomViews
{
    public class InputMultiSelectAlert<T> : InputAlert
    {
        #region Public Properties
        public static ObservableCollection<T> Items { get; set; }
        public static SfListView SfListView { get; private set; }
        public bool IsAllSelected
        {
            get
            {
                if (SfListView.SelectedItems.Count == Items.Count)
                    return true;
                else
                    return false;
            }
        }
        #endregion

        /// <summary>
        /// Constructor runs base InputAlert Constructor, Use factory instead.
        /// </summary>
        /// <param name="viewElementsBefore">ViewElements before View</param>
        /// <param name="sfListView">View to be displayed in window</param>
        /// <param name="viewElementsAfter">ViewElements after View</param>
        /// <param name="confirmButText">Confirm button text</param>
        /// <param name="title">Title of window</param>
        private InputMultiSelectAlert(
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsBefore, 
            View sfListView, 
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsAfter, 
            string confirmButText, string title = null) : base(viewElementsBefore, sfListView, viewElementsAfter, confirmButText, title)
        {
        }

        /// <summary>
        /// Factory for creating InputMultiSelectAlert
        /// </summary>
        /// <param name="viewElementsBefore">ViewElements before View</param>
        /// <param name="itemsForList">Items for list and binding property path</param>
        /// <param name="viewElementsAfter">ViewElements after View</param>
        /// <param name="confirmButText">Confirm button text</param>
        /// <param name="title">Title of window</param>
        /// <returns></returns>
        public static InputMultiSelectAlert<T> InputMultiSelectAlertFactory(
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsBefore,
            Tuple<IEnumerable<T>, string> itemsForList,
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsAfter,
            string confirmButText, string title = null)
        {

            Items = new ObservableCollection<T>(itemsForList.Item1);

            SfListView = new SfListView()
            {
                ItemsSource = Items,
                SelectionMode = Syncfusion.ListView.XForms.SelectionMode.Multiple,
                IsStickyFooter = true,
                IsScrollBarVisible = true,
                ItemTemplate = new DataTemplate(() =>
                {
                    var label = new Label()
                    {
                        VerticalOptions = LayoutOptions.Center
                    };
                    label.SetBinding(Label.TextProperty, itemsForList.Item2);
                    return new ViewCell { View = label };
                })
            };

            return new InputMultiSelectAlert<T>(viewElementsBefore, SfListView, viewElementsAfter, confirmButText, title);
        }
        public void Initalize()
        {
            var headerTemplate = new DataTemplate(() =>
            {
                var label = new Label()
                {
                    Text = "Items",
                    FontSize = Device.GetNamedSize(NamedSize.Medium, typeof(Label)),
                   
                };
                return new ViewCell() { View = label };
            });
            var footerTemplate = new DataTemplate(() =>
            {
                var button = new Button()
                {
                    Text = "SelectAll".Translate(),
                };

                #region Triggers
                var falseTrigger = new DataTrigger(typeof(Button))
                {
                    Binding = new Binding() { Source = this, Path = "IsAllSelected" },
                    Value = true
                };
                falseTrigger.Setters.Add(
                    new Setter()
                    {
                        Property = Button.TextProperty,
                        Value = "UnselectAll".Translate()
                    });

                var trueTrigger = new DataTrigger(typeof(Button))
                {
                    Binding = new Binding() { Source = this, Path = "IsAllSelected" },
                    Value = false
                };
                trueTrigger.Setters.Add(
                    new Setter()
                    {
                        Property = Button.TextProperty,
                        Value = "SelectAll".Translate()
                    });
                button.Triggers.Add(falseTrigger);
                button.Triggers.Add(trueTrigger);
                #endregion

                button.Clicked += SelectAll_UnselectAll_Clicked;
                return new ViewCell { View = button };
            });
            SfListView.HeaderTemplate = headerTemplate;
            SfListView.FooterTemplate = footerTemplate;

            SfListView.SelectionChanged += (sender, e) =>
            {
                OnPropertyChanged("IsAllSelected");
            };

            SfListView.SelectedItems.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged("IsAllSelected");
            };
        }

        public IEnumerable<T> SelectedItems()
        {
            var selectedItems = new List<T>();
            foreach(var item in SfListView.SelectedItems)
            {
                if (item is T confItem)
                    selectedItems.Add(confItem);
            }
            return selectedItems;
        }

        #region Events
        /// <summary>
        /// Select All/Unselect All Button click event;
        /// will select all or unselect all items in list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectAll_UnselectAll_Clicked(object sender, EventArgs e)
        {
            if (IsAllSelected)
                SfListView.SelectedItems.Clear();
            else
                SfListView.SelectAll();
        }
        #endregion
    }
}
