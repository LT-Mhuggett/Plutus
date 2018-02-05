using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Management.Discount
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        private List<Models.DiscountModel> _allDiscounts = new List<Models.DiscountModel>();
        private Models.DiscountModel _discount = new DiscountModel();
        private bool blockEvent = false;

        public MainPage()
        {
            InitializeComponent();

            LoadDisData();

            DisSelector.SelectedIndexChanged += DisSelector_Changed;

            SaveAddBut.Text = App.Translate.ProvideValue("A");
            SaveAddBut.Clicked += SaveAddBut_Clicked_Add_Event;
            DiscountArea.BindingContext = _discount;
        }

        private void LoadDisData()
        {
            _allDiscounts = App.DbContext.Get<Models.DiscountModel>()
                .Include(d => d.DisCategoryList)
                .Include(d => d.DisItemList)
                .ToList();
            foreach (var dis in _allDiscounts)
            {
                DisSelector.Items.Add(dis.Name);
            }
        }

        private void SaveAddBut_Clicked_Add_Event(object sender, EventArgs e)
        {
            App.DbContext.Add(_discount);
            if (!App.DbContext.Save())
                Console.WriteLine("Save Error");
            _discount.Changed = false;
            blockEvent ^= true;
            _discount = new DiscountModel();
            DiscountArea.BindingContext = _discount;
            blockEvent ^= true;
            DisSelector.SelectedIndex = -1;
        }

        private void DisSelector_Changed(object sender, EventArgs e)
        {
            SaveBeforeChange();
            var currentIndex = ((Picker)sender).SelectedIndex;
            if (currentIndex < 0) return;
            blockEvent ^= true;
            _discount = _allDiscounts[currentIndex];
            DiscountArea.BindingContext = _discount;
            SaveAddBut.Text = App.Translate.ProvideValue("S");
            SaveAddBut.Clicked -= SaveAddBut_Clicked_Add_Event;
            SaveAddBut.Clicked += SaveAddBut_Clicked_Save_Event;
            blockEvent ^= true;
        }

        private void SaveAddBut_Clicked_Save_Event(object sender, EventArgs e)
        {
            if (!_discount.Changed) return;
            if (!App.DbContext.Save())
                Console.WriteLine("Save Error");
            _discount.Changed = false;
            blockEvent ^= true;
            _discount = new DiscountModel();
            DiscountArea.BindingContext = _discount;
            SaveAddBut.Text = App.Translate.ProvideValue("A");
            SaveAddBut.Clicked -= SaveAddBut_Clicked_Save_Event;
            SaveAddBut.Clicked += SaveAddBut_Clicked_Add_Event;
            blockEvent ^= true;
            DisSelector.SelectedIndex = -1;
        }

        private async void SaveBeforeChange()
        {
            if (_discount == null) return;

            if (!_discount.Changed) return;
            var save = await DisplayAlert(App.Translate.ProvideValue("Hmm"),
                App.Translate.ProvideValue("ChangesMade"), App.Translate.ProvideValue("Yes"),
                App.Translate.ProvideValue("No"));

            if (save)
                App.DbContext.Save();
            else
                App.DbContext.RevertDbContextChanges();
            _discount.Changed = false;
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }

        private void PropChanged(object sender, EventArgs e)
        {
            if (!blockEvent)
                _discount.Changed = true;
        }

        private async void ListView_Add(object sender, EventArgs e)
        {
            var obj = (Button) sender;
            switch (obj.CommandParameter)
            {
                case "Cate":
                {
                    var returnedData = await Helpers.CustomViews.DataPickerInputAlertHelper
                        .LaunchDataPickerInputAlertAsync(
                            App.Translate.ProvideValue("DisSelectTitle"), "Confirm", "Somthing is not valid",
                            App.DbContext.Get<Models.CategoryModel>().ToList());
                    if (returnedData.Item1 == null)
                    {
                        return;
                    }
                    try
                    {
                        var newDis = new Discount_Category()
                        {
                            Cat = returnedData.Item1,
                            StartDateTime = (DateTime) returnedData.Item2,
                            EndDateTime = (DateTime) returnedData.Item3
                        };

                        _discount.Changed = true;
                        _discount.DisCategoryList.Add(newDis);
                        DiscountArea.BindingContext = null;
                        DiscountArea.BindingContext = _discount;
                        }
                    catch (Exception)
                    {
                        Console.WriteLine(@"Error in Cate");
                    }
                    return;
                }
                case "Item":
                {
                    var returnedData = await Helpers.CustomViews.DataPickerInputAlertHelper
                        .LaunchDataPickerInputAlertAsync(
                            App.Translate.ProvideValue("DisSelectTitle"), "Confirm", "Somthing is not valid",
                            App.DbContext.Get<Models.ItemModel>().ToList());
                    if (returnedData.Item1 == null)
                    {
                        return;
                    }
                    try
                    {
                        var newItem = new Discount_Item()
                        {
                            Item = returnedData.Item1,
                            StartDateTime = (DateTime) returnedData.Item2,
                            EndDateTime = (DateTime) returnedData.Item3
                        };

                        _discount.Changed = true;
                        _discount.DisItemList.Add(newItem);
                        DiscountArea.BindingContext = null;
                        DiscountArea.BindingContext = _discount;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine(@"Error in Cate");
                    }
                    return;
                }
            }
        }

        private void Add_Clicked(object sender, EventArgs e)
        {
            SaveBeforeChange();
            blockEvent ^= true;
            _discount = new DiscountModel();
            DiscountArea.BindingContext = _discount;
            SaveAddBut.Text = App.Translate.ProvideValue("A");
            SaveAddBut.Clicked -= SaveAddBut_Clicked_Save_Event;
            SaveAddBut.Clicked += SaveAddBut_Clicked_Add_Event;
            blockEvent ^= true;
            DisSelector.SelectedIndex = -1;
        }
    }
}