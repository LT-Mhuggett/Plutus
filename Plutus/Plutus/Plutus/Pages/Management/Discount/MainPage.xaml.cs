using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Management.Discount
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
	    private readonly List<Models.DiscountModel> _allDiscounts = new List<Models.DiscountModel>();
	    private Models.DiscountModel _discount;
	    private bool blockEvent = false;

		public MainPage ()
		{
			InitializeComponent ();

		    _allDiscounts = App.DbContext.Get<Models.DiscountModel>()
		        .Include(d => d.DisCategoryList)
		        .Include(d => d.DisItemList)
		        .ToList();
		    foreach (var dis in _allDiscounts)
		    {
		        DisSelector.Items.Add(dis.Name);
		    }

		    DisSelector.SelectedIndexChanged += DisSelector_Changed;

		}

	    private async void DisSelector_Changed(object sender, EventArgs e)
	    {
            if (_discount == null) goto ChangeLoadedDiscount;

	        if (_discount.Changed)
	        {
	            var save = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ChangesMade"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("No"));

	            if (save)
	                App.DbContext.Save();
                else
	                App.DbContext.RevertDbContextChanges();
                _discount.Changed = false;
            }
            ChangeLoadedDiscount:
	        {
	            var currentIndex = ((Picker) sender).SelectedIndex;
	            blockEvent ^= true;
	            _discount = _allDiscounts[currentIndex];
	            DiscountArea.BindingContext = _discount;
	            blockEvent ^= true;
	        }
	    }

	    private void CancelEmpCheck_Clicked(object sender, EventArgs e)
	    {
	        EId.Text = null;
	        VerifyId.IsVisible = false;
	        MPage.IsEnabled = true;
	    }
        
	    private void PropChanged(object sender, EventArgs e)
	    {
            if(!blockEvent)
                _discount.Changed = true;
	    }
    }
}