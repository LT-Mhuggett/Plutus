using CustomViews.Structs;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Helpers.Security;
using NatApp.Plutus.Helpers.Validators;
using NatApp.Plutus.Services.Analytics;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Inventory.Items
{
    public class AddEditViewModel : BaseViewModel
    {
        #region Fields
        private ItemModel _item;
        private ObservableCollection<CategoryModel> _categories;
        private CategoryModel _category;
        private ObservableCollection<TaxModel> _taxes;
        private TaxModel _tax;
        private int _stock;
        #endregion

        #region Properties
        public ItemModel Item
        {
            get => _item;
            set => SetProperty(ref _item, value);
        }
        public ObservableCollection<CategoryModel> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }
        public CategoryModel Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }
        public ObservableCollection<TaxModel> Taxes
        {
            get => _taxes;
            set => SetProperty(ref _taxes, value);
        }
        public TaxModel Tax
        {
            get => _tax;
            set => SetProperty(ref _tax, value);
        }
        public int Stock
        {
            get => _stock;
            set => SetProperty(ref _stock, value);
        }
        public bool IsUpdate { get; }
        public string CreateUpdateText { get; }
        #endregion


        public AddEditViewModel()
        {
            Title = "Add".Translate();
            Icon = null;
            CreateUpdateText = "Create".Translate();
            IsUpdate = false;
            CreateUpdateCommand = new Command(ExecuteCreate);
            Item = new ItemModel();
            Device.BeginInvokeOnMainThread(async () =>
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    Categories = new ObservableCollection<CategoryModel>(await db.Get<CategoryModel>().AsNoTracking().ToListAsync());
                    Taxes = new ObservableCollection<TaxModel>(await db.Get<TaxModel>().AsNoTracking().ToListAsync());

                    Categories.Add(new CategoryModel
                    {
                        Id = Categories.Last().Id + 1,
                        Name = string.Format("CreateWithArg".Translate(), "Category".Translate())
                    });
                }
                App.SetLoading(false);
            });
        }

        public AddEditViewModel(string itemId)
        {
            Title = "Edit".Translate();
            Icon = null;
            CreateUpdateText = "Update".Translate();
            IsUpdate = true;
            CreateUpdateCommand = new Command(ExecuteUpdate);
            Device.BeginInvokeOnMainThread(async () =>
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    Categories = new ObservableCollection<CategoryModel>(await db.Get<CategoryModel>().AsNoTracking().OrderBy(c => c.Id).ToListAsync());
                    Taxes = new ObservableCollection<TaxModel>(await db.Get<TaxModel>().AsNoTracking().OrderBy(t => t.Id).ToListAsync());

                    Categories.Add(new CategoryModel
                    {
                        Id = Categories.Last().Id + 1,
                        Name = string.Format("CreateWithArg".Translate(), "Category".Translate())
                    });

                    Item = await db.SearchId(itemId)
                        .Include(i => i.Cat)
                        .Include(i => i.Stock)
                        .Include(i => i.Vat)
                        .SingleAsync();
                }

                if (Item.Stock != null)
                    Stock = Item.Stock.Quantity;
                else
                    Stock = -1;
                Tax = Taxes.First(t => t.Id.Equals(Item.Vat.Id));
                Category = Categories.First(c => c.Id.Equals(Item.Cat.Id));
                App.SetLoading(false);
            });
        }

        #region Commands
        public Command CreateUpdateCommand { get; }

        Command _createNewCategoryCommand;

        public Command CreateNewCategoryCommand
        {
            get => _createNewCategoryCommand ?? (_createNewCategoryCommand = new Command(ExecuteCreateCategory));
        }
        #endregion

        #region Execute Command
        private async void ExecuteCreate()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Item", Database.Enums.Permissions.Write, databaseProvider))
                    {
                        CreateUpdateStock();
                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            if (db.IsExists<ItemModel, string>(Item.Id))
                            {
                                await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemExists".Translate(), "OK".Translate());
                                return;
                            }

                            Item.VatId = Tax.Id;
                            Item.CatId = Category.Id;

                            db.Add(Item);
                            if (!db.Save())
                            {
                                Debug.Write("Save Failed!");
                            }

                            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Created (from AddEditViewModel)");

                            //await DisplayPopup();
                            await App.Current.MainPage.DisplayAlert("Success".Translate(), "Saved".Translate(), "OK".Translate());

                            await App.Current.MainPage.Navigation.PopAsync();
                            return;
                        }
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteUpdate()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Item", Database.Enums.Permissions.Write, databaseProvider))
                    {
                        CreateUpdateStock();
                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            //Ensures that old relationship data doesn't overide new changed relationship data
                            Item.Vat = null;
                            Item.Cat = null;
                            Item.VatId = Tax.Id;
                            Item.CatId = Category.Id;

                            db.Update(Item);
                            if (!db.Save())
                            {
                                Debug.Write("Save Failed!");
                            }

                            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Updated (from AddEditViewModel)");

                            //await DisplayPopup();
                            await App.Current.MainPage.DisplayAlert("Success".Translate(), "Saved".Translate(), "OK".Translate());

                            await App.Current.MainPage.Navigation.PopAsync();
                            return;
                        }
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
            await App.Current.MainPage.Navigation.PopAsync();
        }

        private async void ExecuteCreateCategory()
        {
            if (IsBusy ||
                Category == null ||
                Category.Id != Categories.Last().Id)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Item", Database.Enums.Permissions.Write, databaseProvider))
                    {
                        var validators = new IValidator[]
                        {
                            new RequiredValidator()
                        };

                        var viewElements = new ViewElementData[]
                        {
                            new ViewElementData(1, "Name".Translate(), "", validators.AsEnumerable(), false, true),
                            new ViewElementData(2, "Description".Translate(), "", validators.AsEnumerable(), false, true)
                        };
                        var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, "Category".Translate(), "Cancel".Translate());

                        if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                        {
                            Category = Categories.First();
                            return;
                        }

                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            var cat = new CategoryModel
                            {
                                Name = data[0].ToString(),
                                Description = data[1].ToString()
                            };
                            db.Add(cat);
                            if (!db.Save())
                            {
                                Debug.Write("Save Failed!");
                                Category = Categories.First();
                                return;
                            }


                            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Category Created (from AddEditViewModel)");
                            //await DisplayPopup();
                            await App.Current.MainPage.DisplayAlert("Success".Translate(), "Saved".Translate(), "OK".Translate());

                            Categories = new ObservableCollection<CategoryModel>(await db.Get<CategoryModel>().AsNoTracking().OrderBy(c => c.Id).ToListAsync());
                            Device.BeginInvokeOnMainThread(() => Category = Categories.ElementAt(Categories.Count - 2));

                            Categories.Add(new CategoryModel
                            {
                                Id = Categories.Last().Id + 1,
                                Name = string.Format("CreateWithArg".Translate(), "Category".Translate())
                            });
                            return;
                        }
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Operations
        private void CreateUpdateStock()
        {
            if (_stock < 0)
                Item.Stock = null;
            else if (Item.Stock == null)
            {
                Item.Stock = new StockModel()
                {
                    Quantity = _stock,
                    StoreId = App.GetViewModel().Store.Id
                };
            }
            else
            {
                Item.Stock.Quantity = _stock;
            }
        }
        /*
        private async Task DisplayPopup()
        {
            var popupLayout = new SfPopupLayout();
            popupLayout.PopupView.BackgroundColor = Color.Transparent;
            popupLayout.PopupView.ShowHeader = false;
            popupLayout.PopupView.ShowFooter = false;
            popupLayout.BackgroundColor = Color.Transparent;
            var stack = new StackLayout();
            stack.Children.Add(new IconImage
            {
                Icon = "md-check-circle",
                IconColor = Color.Red,
                IconSize = 30
            });
            stack.Children.Add(new Label
            {
                Text = "Saved".Translate()
            });
            popupLayout.Content = stack;

            popupLayout.Show();
            await Task.Delay(500);
            popupLayout.IsOpen = false;
        }*/
        #endregion
    }
}
