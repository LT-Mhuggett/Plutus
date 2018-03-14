using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public class SelectMultipleBasePage<T> : ContentPage
    {
        public class WrappedSelection<T> : INotifyPropertyChanged
        {
            public T Item { get; set; }
            bool isSelected { get; set; }

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value) return;
                    isSelected = value;
                    PropertyChanged(this, new PropertyChangedEventArgs("IsSelected"));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged = delegate { };
        }

        public class WrappedItemSelectionTemplate : ViewCell
        {
            public WrappedItemSelectionTemplate() : base()
            {
                Label name = new Label();
                name.SetBinding(Label.TextProperty, new Binding("Item.Name"));
                Switch mainSwitch = new Switch();
                mainSwitch.SetBinding(Switch.IsToggledProperty, new Binding("IsSelected"));
                RelativeLayout layout = new RelativeLayout();
                layout.Children.Add(name,
                    Constraint.Constant(5),
                    Constraint.Constant(5),
                    Constraint.RelativeToParent(p => p.Width - 60)
                );

                layout.Children.Add(mainSwitch,
                    Constraint.RelativeToParent(p => p.Width - 55),
                    Constraint.Constant(5),
                    Constraint.Constant(50)
                );
                View = layout;
            }
        }

        public List<WrappedSelection<T>> WrappedItems = new List<WrappedSelection<T>>();

        public SelectMultipleBasePage(List<T> items)
        {
            WrappedItems = items.Select(
                item => new WrappedSelection<T>()
                {
                    Item = item,
                    IsSelected = false
                }).ToList();

            ListView mainList = new ListView()
            {
                ItemsSource = WrappedItems,
                ItemTemplate = new DataTemplate(typeof(WrappedItemSelectionTemplate))
            };

            mainList.ItemSelected += (sender, e) =>
            {
                if (e.SelectedItem == null) return;
                var o = (WrappedSelection<T>) e.SelectedItem;
                o.IsSelected = !o.IsSelected;
                ((ListView) sender).SelectedItem = null;
            };
            var footerButton = new Button
            {
                Text = "Confirm"
            };

            footerButton.Clicked += FooterButton_Clicked;

            mainList.Footer = footerButton;

            Content = mainList;
            ToolbarItems.Add(new ToolbarItem("All", null, SelectAll, ToolbarItemOrder.Primary));
            ToolbarItems.Add(new ToolbarItem("None", null, SelectNone, ToolbarItemOrder.Primary));
        }

        private void FooterButton_Clicked(object sender, System.EventArgs e)
        {
            MessagingCenter.Send((App) Application.Current, "SelectedItems");
            Navigation.PopModalAsync();
        }

        void SelectAll()
        {
            foreach (var wi in WrappedItems)
                wi.IsSelected = true;
        }

        void SelectNone()
        {
            foreach (var wi in WrappedItems)
                wi.IsSelected = false;
        }

        public List<T> GetSelection() =>
            WrappedItems.Where(item => item.IsSelected).Select(wrappedItem => wrappedItem.Item).ToList();
    }
}