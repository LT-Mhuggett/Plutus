using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.FirstTimeStartUp
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : TabbedPage
    {
        /// <summary>
        /// Basic Constructor for the FirstTimeStartUpPage object
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            Children.Add(new ConfigPage());
            Children.Add(new WelcomePage());
            Children.Add(new ExternalSource());
            CurrentPage = Children[1];
            //Children.Add(new StoreLoginPage());
        }
    }
}