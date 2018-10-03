using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions.XAML;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public class InputWithListMultiSelection<T> : ContentView
	{
        public EventHandler ConfirmButtonEHandler { get; set; }
        
        public List<ListView> Selections { get; set; }
	    public List<List<MultiSelectListView<T>>> ListOfListItems { get; set; }
	    public List<Entry> Entries { get; set; }

	    public InputWithListMultiSelection (string titleText, List<string> placeholderText,
	        string confirmButText, List<string> validationText,
	        List<List<T>> selections, List<string>bindingNames)
	    {
	        ListOfListItems = new List<List<MultiSelectListView<T>>>();
	        Entries = new List<Entry>();

            var MainLayout = new StackLayout
            {
                Children = { new Label{Text = titleText } }
            };
		    var scrollview = new ScrollView {Content = MainLayout};

		    this.Content = scrollview;
		    foreach (var pholderText in placeholderText)
		    {
                var entry = new Entry
                {
                    Placeholder = pholderText
                };
		        Entries.Add(entry);
		        MainLayout.Children.Add(entry);
		    }

		    for (var i = 0; i <= selections.Count - 1; i++)
		    {
		        var data = new List<MultiSelectListView<T>>();
		        foreach (var item in selections[i])
		        {
                    var selectedData = new MultiSelectListView<T>
                    {
                        Data = item
                    };
                    data.Add(selectedData);
		        }

                ListOfListItems.Add(data);

		        var cell = new ViewCell();

		        var label = new Label();
		        label.SetBinding(BindingContextProperty, new Binding(bindingNames[i]));

		        var _switch = new Switch();
                _switch.SetBinding(Switch.BindingContextProperty, new Binding("IsSelected"));

		        cell.View = new Grid
		        {
		            ColumnDefinitions = new ColumnDefinitionCollection
		            {
		                new ColumnDefinition {Width = GridLength.Star},
		                new ColumnDefinition {Width = GridLength.Auto},
		            },
		            Children =
		            {
		                new StackLayout
		                {
		                    VerticalOptions = LayoutOptions.CenterAndExpand,
		                    Children =
		                    {
		                        label
		                    }
		                },
		                _switch
		            }
		        };
		        var listView = new ListView()
		        {
		            ItemsSource = data,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    VerticalOptions = LayoutOptions.FillAndExpand,
                    ItemTemplate = new DataTemplate(()=>cell)
		        };
		        MainLayout.Children.Add(listView);
		        var confButton = new Button {Text = confirmButText};
		        confButton.Clicked += ConfirmButtonEHandler;
                MainLayout.Children.Add(confButton);
		    }
		}
	}
}