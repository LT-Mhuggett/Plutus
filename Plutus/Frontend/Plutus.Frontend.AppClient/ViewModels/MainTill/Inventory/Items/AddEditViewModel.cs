using CustomViews.Structs;
using Microsoft.Maui.ApplicationModel;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.Analytics;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items
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
            MainThread.BeginInvokeOnMainThread(async () =>
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
            MainThread.BeginInvokeOnMainThread(async () =>
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

                        // ⚠ `Count == 0` FIRST — backing out now yields an EMPTY dictionary (see
                        // `InputAlert.CancelBut_Clicked`, 2026-08-18), and `Any(…)` over nothing is
                        // FALSE, so without this the cancel path fell through to `data[…]` below.
                        if (data.Count == 0 || data.Any(d => string.IsNullOrEmpty(d.Value)))
                        {
                            Category = Categories.First();
                            return;
                        }

                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            var cat = new CategoryModel
                            {
                                // ⚠ KEYS 1 AND 2, not 0 and 1 — ViewElementData ids are 1-based (the two
                                // `new ViewElementData(1…)/(2…)` above), so `data[0]` threw
                                // KeyNotFoundException on the SUCCESS path: add-category from the item
                                // editor could never have worked. Found 2026-08-18 while making cancel safe.
                                Name = data[1],
                                Description = data[2]
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
                            MainThread.BeginInvokeOnMainThread(() => Category = Categories.ElementAt(Categories.Count - 2));

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
                // ⚠⚠ GUARDED BECAUSE `EnsureStoreAsync` IS GONE — cutover step 21, 2026-08-23. The
                // legacy store row is no longer created at sign-in, so `.Store` is null on every
                // modern till and this was the last place that dereferenced it unguarded.
                //
                // ⚠ REFUSES, IT DOES NOT DEFAULT. The register is explicit: *"Do NOT unblock this by
                // null-coalescing to 0 — that writes stock rows against store 0, a silent data change
                // wearing a null-fix disguise."* No store means no stock row, which is recoverable;
                // a row against the wrong store is not.
                //
                // ⚠ THIS SCREEN IS UNREACHABLE (hidden 2026-08-10, L2) and the guard exists so that
                // stays TRUE OF A CRASH TOO. It must not become a reason to keep the file: L2 deletes
                // it, and this goes with it.
                var legacyStore = App.GetViewModel()?.Store;
                if (legacyStore is null)
                {
                    Services.Analytics.CrashLog.Write("AddEditViewModel.Stock",
                        new InvalidOperationException(
                            "No legacy store on this till, so a stock row cannot be written. Stock is the "
                            + "portal's to set (step 25)."));
                    return;
                }

                Item.Stock = new StockModel()
                {
                    Quantity = _stock,
                    StoreId = legacyStore.Id
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
            popupLayout.PopupView.BackgroundColor = Colors.Transparent;
            popupLayout.PopupView.ShowHeader = false;
            popupLayout.PopupView.ShowFooter = false;
            popupLayout.BackgroundColor = Colors.Transparent;
            var stack = new StackLayout();
            stack.Children.Add(new IconImage
            {
                Icon = "md-check-circle",
                IconColor = Colors.Red,
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
