using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions;
using Plutus.Helpers.Extensions.XAML;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class InputWithListMultiSelection : ContentView
    {
        public EventHandler ConfirmButtonEHandler { get; set; }

        public List<ListView> Selections { get; set; }
        public ObservableCollection<MultiSelectListView<dynamic>> ListOfItems { get; set; } = new ObservableCollection<MultiSelectListView<dynamic>>();
        public List<Entry> Entries { get; set; }

        public InputWithListMultiSelection(string titleText, List<string> placeholderText,
            string confirmButText, List<string> validationText,
            List<dynamic> selections, List<string> bindingNames, string staticInput)
        {
            InitializeComponent();
            foreach(var item in selections)
            {
                ListOfItems.Add(
                    new MultiSelectListView<dynamic> {
                        Data = item,
                        IsSelected = false
                    });
            }
            Input.Placeholder = placeholderText[0];
            Title.Text = titleText;
            Entries = new List<Entry>();
            Entries.Add(Input);
            confirmBut.Text = confirmButText;
            confirmBut.Clicked += (sender, e) => { ConfirmButtonEHandler?.Invoke(this, e); };
            BindingContext = this;
            if(staticInput != null)
            {
                Input.Text = staticInput;
                Input.IsEnabled = false;
            }
        }

        private void Switch_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var _switch = (sender as Switch).IsToggled;
            if(_switch)
                ListOfItems.ForEach(item => item.IsSelected = true);
            else
                ListOfItems.ForEach(item => item.IsSelected = false);
        }

        private void SelectAll_Clicked(object sender, EventArgs e)
        {
            ListOfItems.ForEach(item => item.IsSelected = true);
            ConfirmButtonEHandler?.Invoke(this, e);
        }
    }
}