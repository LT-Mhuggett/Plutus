using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class FirstTimeStartUpPage : TabbedPage
    {
        public FirstTimeStartUpPage ()
        {
            InitializeComponent();

            Children.Add(new ConfigPage());
            Children.Add(new StoreLoginPage());
        }
    }
}