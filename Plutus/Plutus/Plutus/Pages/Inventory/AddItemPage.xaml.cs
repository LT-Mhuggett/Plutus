using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Models;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddItemPage : ContentPage
	{
        internal ItemModel item = new ItemModel();

        public AddItemPage ()
		{
			InitializeComponent ();
		}

        private async void ViewCell_Tapped(object sender, EventArgs e)
        {
            string action;
            if (Camera.IsCameraAval())
            {
                action = await DisplayActionSheet("Picture", "Cancel", null, "Camera", "Photo Roll");
            }
            else
            {
                action = await DisplayActionSheet("Picture", "Cancel", null, "Photo Roll");
            }
            

            switch (action)
            {
                case "Camera":
                    Camera.getPhoto(item, Pic);
                    break;
                case "Photo Roll":
                    break;
                case "Cancel":
                    break;
            }
        }
    }
}