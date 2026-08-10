using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CustomViews.Structs;
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.MessagingCenter;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;


using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items
{
    public class ViewAllViewModel : BaseViewModel
    {
        #region Private Fields
        private string _searchText;
        private int _limit;
        #endregion

        #region Properties
        #region Public
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value, onChanged: () => ExecuteItemFilter());
        }
        /// <summary>
        /// Every item this screen knows about, unfiltered. ⚠ The list on screen is
        /// <see cref="ItemGroups"/>; this is the source it is rebuilt from, so a search narrows the
        /// view without losing the rows.
        /// </summary>
        public ObservableCollection<ItemModel> Items { get; private set; } = new ObservableCollection<ItemModel>();

        /// <summary>
        /// The A–Z groups the list actually renders.
        ///
        /// ⚠ GROUPING IS THE VIEWMODEL'S JOB NOW. It used to be Syncfusion's: the view pushed its
        /// own `DataSource` in through a `OneWayToSource` binding and grouping was applied to that
        /// object on assignment, because doing it in the constructor dereferenced a null the view
        /// had not pushed yet — the NullReferenceException that made "View all items" crash on
        /// every open, wrapped in a TargetInvocationException so the stack pointed at
        /// InitializeComponent. ⚠ Matt is not renewing the Syncfusion licence (2026-08-10), so the
        /// list is a plain MAUI `CollectionView` and that entire class of failure is gone with it:
        /// there is no shared mutable object between view and viewmodel to get the order wrong on.
        /// </summary>
        public ObservableCollection<ItemGroup> ItemGroups { get; } = new ObservableCollection<ItemGroup>();

        /// <summary>Is the list empty because there is nothing, or because the search matched
        /// nothing? ⚠ A blank list with no message reads as "this shop sells nothing" — which is
        /// exactly how the legacy-table bug hid for as long as it did.</summary>
        public bool ShowEmptyNotice => ItemGroups.Count == 0;

        public string EmptyNotice => string.IsNullOrWhiteSpace(SearchText)
            ? "No items yet. They arrive from Plutus with the catalogue."
            : $"Nothing matches “{SearchText}”.";
        #endregion
        #endregion

        public ViewAllViewModel()
        {
            Title = "View All Items";
        }

        /// <summary>
        /// One A–Z bucket. ⚠ A `List&lt;T&gt;` subclass, which is what MAUI's `IsGrouped` binding
        /// expects — it enumerates each group directly, so a wrapper with an `Items` property
        /// renders empty rows and, as ever with MAUI bindings, says nothing about why.
        /// </summary>
        public sealed class ItemGroup : List<ItemModel>
        {
            public ItemGroup(string key, IEnumerable<ItemModel> items) : base(items) => Key = key;
            public string Key { get; }
        }

        /// <summary>
        /// Rebuild the on-screen groups from <see cref="Items"/> and the current search text.
        ///
        /// ⚠ MUST RUN ON THE UI THREAD — it mutates an `ObservableCollection` the list is bound to.
        /// ⚠ Null-safe on the group key: keying on `item.Name[0]` throws on an item with no name,
        /// and it used to do so inside the list's own layout pass, where it stranded the screen
        /// rather than surfacing an error anyone could act on.
        /// </summary>
        private void RebuildGroups()
        {
            var groups = Items
                .Where(FilterItem)
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Name)
                    ? "#"
                    : char.ToUpperInvariant(i.Name.Trim()[0]).ToString())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ItemGroup(g.Key, g))
                .ToList();

            ItemGroups.Clear();
            foreach (var group in groups) ItemGroups.Add(group);

            OnPropertyChanged(nameof(ShowEmptyNotice));
            OnPropertyChanged(nameof(EmptyNotice));
        }

        /// <summary>
        /// Fill the browsable item list from the V2 CATALOGUE.
        ///
        /// ⚠ THIS READ THE LEGACY `Items` TABLE AND SILENTLY SHOWED NOTHING. On a
        /// portal-provisioned till that table is empty and stays empty — the catalogue arrives
        /// through `/api/v1/catalogue/changes` into the v2 store — so browse-and-tap, one of the two
        /// ways an operator puts something in a basket, simply did not exist. No error, no empty
        /// state, just a blank list, which reads as "this shop sells nothing".
        ///
        /// ⚠ Loads OFF the UI thread. The old version opened SQLite and ran a two-`Include` query
        /// synchronously with the screen already up.
        /// </summary>
        public void InitItems()
        {
            // ⚠ NO GLOBAL OVERLAY HERE, and its absence is deliberate. This runs from
            // `ViewAllView.OnAppearing`, which fires WHILE the page push is still transitioning —
            // so raising the overlay issued a MODAL push into the middle of a navigation push on
            // the same window. MAUI does not serialise the two stacks and the WinUI handler
            // resolved the collision into a corrupted layout: the item list drew over the tab bar,
            // at the wrong size, with no way back out. Reported 2026-08-10.
            //
            // The read below is a capped local SQLite query. A list that fills a moment after the
            // screen arrives is a far better outcome than a screen the operator cannot leave, and
            // an in-page busy indicator is the right long-term answer (WP10's screen work).

            _ = Task.Run(async () =>
            {
                var loaded = new List<ItemModel>();
                try
                {
                    // ⚠ TIMED OUT, because the store is SHARED. `TillStoreAccess` serialises every
                    // caller behind one semaphore, so a read that never returns does not just hang
                    // this screen — it blocks the heartbeat, the outbox drain and the catalogue
                    // sync behind it, and the till goes quiet with no error anywhere.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var catalogue = await Services.Storage.TillStoreAccess.UseAsync(
                        s => s.BrowseAsync(500, timeout.Token), timeout.Token);

                    // ⚠ Mapped to the legacy `ItemModel` because that is what the list view binds
                    // to, and MAUI bindings fail SILENTLY — swapping the bound type would blank the
                    // rows rather than fail. The model goes when the inventory screen is reshaped.
                    loaded = catalogue.Select(c => new ItemModel
                    {
                        Id = c.IdOne,
                        Name = c.Name,
                        Price = c.PricePence / 100m,
                        // ⚠ Derived from the rate, NOT read off the item: the v2 catalogue stores
                        // the inc price and a rate, and this list is a browse — the SALE price pair
                        // is resolved at basket-add by `EffectivePricePairAsync`, which is the only
                        // thing allowed to decide what a line costs.
                        ExPrice = c.VatRateBp > 0
                            ? Math.Round(c.PricePence / (1m + c.VatRateBp / 10000m)) / 100m
                            : c.PricePence / 100m,
                    }).ToList();
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("ViewAllViewModel.InitItems", ex);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // ⚠ STILL LOWERS THE OVERLAY, even though this screen no longer raises one.
                    // It is a no-op when nothing is up, and it is the last thing standing between an
                    // operator and a stranded spinner if some OTHER screen left one over the app on
                    // the way here. Cheap insurance; the overlay is the component with the worst
                    // failure mode in this app.
                    //
                    // ⚠ In its OWN try, because `SetLoading` reaches through `App.GetViewModel()`,
                    // which casts `_app.BindingContext` — it can throw, and if it took the binding
                    // down with it the screen would be blank AND covered.
                    try { App.SetLoading(false); }
                    catch (Exception ex) { Services.Analytics.CrashLog.Write("ViewAllViewModel.Overlay", ex); }

                    try
                    {
                        Items = new ObservableCollection<ItemModel>(loaded);
                        OnPropertyChanged(nameof(Items));
                        RebuildGroups();
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("ViewAllViewModel.Bind", ex);
                    }
                });
            });
        }

        #region Commands
        /*
        Command _searchItemsCommand;

        public Command SearchItemsCommand
        {
            get => _searchItemsCommand ?? (_searchItemsCommand = new Command(ExecuteItemSearch));
        }*/
        /*
        #region Load More Items
        
        Command _loadMoreItemsCommand;

        public Command LoadMoreItemsCommand
        {
            get => _loadMoreItemsCommand ?? (_loadMoreItemsCommand = new Command(ExecuteItemLoad, CanLoadMore));
        }

        private bool CanLoadMore()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                return db.Get<ItemModel>().Count() > Items.Count;
            }
        }
        #endregion
        */

        Command _addToBasketCommandArg;
        public Command AddToBasketCommandArg
        {
            get => _addToBasketCommandArg ?? (_addToBasketCommandArg = new Command<string>(ExecuteAddToBasket));
        }

        Command _openEditItemCommandArg;
        public Command OpenEditItemCommandArg
        {
            get => _openEditItemCommandArg ?? (_openEditItemCommandArg = new Command<string>(ExecuteOpenEditItem));
        }

        Command _updateItemStockCommandArg;
        public Command UpdateItemStockCommandArg
        {
            get => _updateItemStockCommandArg ?? (_updateItemStockCommandArg = new Command<string>(ExecuteUpdateItemStock));
        }

        #endregion

        #region Execute Command
        #region Search & Filter
        /*
        private async void ExecuteItemSearch()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                Items.AddRange(db.Get<ItemModel>()
                    .Include(i => i.Stock)
                    .Include(i => i.Vat)
                    .OrderBy(i => i.Name)
                    .Where(i => i.Name.Contains(
                        SearchText,
                        StringComparison.OrdinalIgnoreCase) ||
                        (i.Brand != null ? i.Brand.Contains(
                            SearchText,
                            StringComparison.OrdinalIgnoreCase) : false) ||
                        (i.Desc != null ? i.Desc.Contains(
                            SearchText,
                            StringComparison.OrdinalIgnoreCase) : false))
                    .Where(item => !Items.Contains(item))
                    .Select(item => item));
            }
        }*/

        // ⚠ A straight rebuild now. This used to hand a predicate to Syncfusion's `DataSource` and
        // call `RefreshFilter()`; with the licence going (Matt, 2026-08-10) the filtering is the
        // viewmodel's, over a list capped at 500 rows — cheap enough to redo on every keystroke.
        private void ExecuteItemFilter() => RebuildGroups();
        /*
        private async void ExecuteItemLoad()
        {
            LoadItems();
        }*/
        #endregion
        private void ExecuteAddToBasket(string itemId)
        {
            MessagingCenter.Send(this, "AddToBasket", itemId);
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket Requested (from ViewAllViewModel)");
        }

        /// <summary>
        /// A row was tapped — offer what can be done with it.
        ///
        /// ⚠ THIS IS THE ONLY DISCOVERABLE WAY IN. "Add to basket" and "Edit" lived exclusively on a
        /// context menu (right-click on desktop, long-press on touch) with nothing on screen to
        /// advertise it. Asked what happened when he tried to edit an item, Matt answered "I didn't
        /// know how to open it" — which is the honest verdict on a capability reachable only by a
        /// gesture nobody mentions.
        ///
        /// ⚠ Through `Modal`, because picking an action here leads straight into ANOTHER dialog (the
        /// edit prompt) — and two modals in quick succession is what threw a COMException and closed
        /// the till at the payment prompt.
        /// </summary>
        public async void RowTapped(ItemModel item)
        {
            if (item?.Id is null) return;

            try
            {
                const string addToBasket = "Add to basket";
                const string edit = "Edit item";

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    App.Current.MainPage.DisplayActionSheet(
                        item.Name ?? "Item", "Cancel".Translate(), null, addToBasket, edit));

                if (picked == addToBasket) ExecuteAddToBasket(item.Id);
                else if (picked == edit) ExecuteOpenEditItem(item.Id);
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.RowTapped", ex);
            }
        }

        /// <summary>
        /// Edit an item's NAME and PRICE, on the platform (WP10 / cutover step 25).
        ///
        /// ⚠ IT USED TO OPEN `AddEditView`, WHICH WROTE TO THE LEGACY LOCAL DATABASE — a table
        /// nothing reads. The edit reached no report, no other till and no VAT return, and since the
        /// basket resolves from the v2 catalogue the operator could not even see their own change.
        /// It was hidden on 2026-08-10 for exactly that reason; this is the real fix.
        ///
        /// ⚠ THE PRICE PAIR IS DERIVED, NEVER TYPED TWICE. The server guards
        /// `|price − exPrice × rate| ≤ 2p` because free-typed ex-prices corrupted 47 live items — a
        /// £7.99 item with a £799.00 ex-price — and with them every downstream VAT figure. The
        /// operator gives the INC price; the ex price comes from the band the item already carries.
        ///
        /// ⚠ READ-MODIFY-WRITE through `UpdateItemFieldsAsync`, because the PUT binds the WHOLE
        /// entity: a bare price change would clear `StockUntracked` or blank `BinnedAtUtc`,
        /// restoring a withdrawn item to sale on every till in the estate.
        /// </summary>
        private async void ExecuteOpenEditItem(string itemId)
        {
            try
            {
                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PortalPricesManage);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                // ⚠ The OPERATOR's client. The legacy item controllers read an `objectidentifier`
                // claim in their base CONSTRUCTOR, which only an operator token carries — a device
                // token does not merely fail the policy, it 500s before the action runs.
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Editing an item needs someone signed in and a connection to Plutus.", "OK".Translate());
                    return;
                }

                var businessId = await Services.Storage.TillStoreAccess.UseAsync(
                    s => s.GetGuidMetaAsync(Plutus.Client.Storage.MetaKeys.BusinessId));
                if (businessId is not Guid business || business == Guid.Empty)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "This till hasn't learnt which business it belongs to yet.", "OK".Translate());
                    return;
                }

                var current = await api.GetItemAsync(itemId, business);
                if (current is null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Plutus couldn't find that item.", "OK".Translate());
                    return;
                }

                const NumberStyles money = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                                           | NumberStyles.AllowDecimalPoint;

                // ⚠ THE FULL FIELD SET, because two fields was not an item editor. Matt,
                // 2026-08-10: *"I can now edit, but it seems to be missing a lot of options compared
                // to the webtill."* He is right — the web till's dialog offers barcode, name, brand,
                // description, cost, price, tax band, category and stock tracking, and this offered
                // name and price. An editor that can change a third of an item is one an operator
                // has to leave the till to finish, which is the opposite of parity.
                //
                // ⚠ THE BARCODE IS DELIBERATELY NOT EDITABLE, and the web till disables it on an
                // edit too (`disabled={busy || !!item}`). It is half the composite primary key:
                // changing it is a DELETE and an INSERT, which orphans every stock movement, every
                // sale line and every webstore listing that points at the old one.
                var bands = await api.GetTaxBandsAsync(business) ?? new List<TaxBandDto>();
                var categories = await api.GetCategoriesAsync(business) ?? new List<CategoryDto>();

                // ⚠ THE CHOICES COME FIRST, AND THE FORM THEN SHOWS WHAT WAS CHOSEN. Matt,
                // 2026-08-10 (second report): *"Maui edit items is missing category and tax e.g.
                // 20%."* The first attempt asked for them in action sheets AFTER the form, so an
                // operator opening "Edit item" saw name/brand/price and no tax or category anywhere
                // — and if the lists came back empty the sheets were skipped in silence and never
                // appeared at all. Both readings of "missing" were true.
                //
                // ⚠ The rule this breaks is the one that keeps biting: A SCREEN MUST NOT DECIDE
                // SOMETHING WITHOUT SHOWING IT. Tax band and category are written on every save
                // (the PUT binds the whole entity), so they are always part of the edit whether or
                // not anyone was asked.
                var taxId = await PickTaxBandAsync(bands, current.TaxId);
                if (taxId is null) return;                       // ⚠ Cancel means cancel, not "keep the old band"

                var catId = await PickCategoryAsync(categories, current.CatId);
                if (catId is null) return;

                var untracked = await PickStockTrackingAsync(current.StockUntracked);
                if (untracked is null) return;

                var chosenBand = bands.FirstOrDefault(b => b.IdOne == taxId.Value);
                var chosenCategory = categories.FirstOrDefault(c => c.IdOne == catId.Value);

                var elements = new ViewElementData[]
                {
                    new ViewElementData(1, "Name".Translate(), current.Name ?? "",
                        new IValidator[] { new RequiredValidator() }, false, true),
                    // ⚠ "-" is what the WEB TILL writes when a brand is unknown, so the same column
                    // does not end up holding "-" from one till and "" from another. It is shown as
                    // blank here for the same reason the web till strips it on the way in.
                    new ViewElementData(2, "Brand", current.Brand == "-" ? "" : current.Brand ?? "",
                        Array.Empty<IValidator>(), false, true),
                    new ViewElementData(3, "Description", current.Desc ?? "",
                        Array.Empty<IValidator>(), false, true),
                    new ViewElementData(4, "Cost (£)", current.Cost.ToString("0.00"),
                        new IValidator[] { new CurrencyValueValidator(money) }, false, true),
                    new ViewElementData(5, "Price inc tax (£)", current.Price.ToString("0.00"),
                        new IValidator[] { new RequiredValidator(), new CurrencyValueValidator(money) }, false, true),

                    // ⚠ DISABLED ROWS, and their answers are IGNORED — they exist so the operator
                    // can SEE the tax band and the category on the same screen as the price they
                    // are setting. "20% VAT" next to "£9.99" is the check that catches a
                    // zero-rated book priced as if it carried VAT, and no amount of correct
                    // arithmetic further down replaces being able to look at it.
                    new ViewElementData(6, "Tax band", BandLabel(chosenBand, taxId.Value),
                        Array.Empty<IValidator>(), false, false),
                    new ViewElementData(7, "Category",
                        chosenCategory?.Name ?? (catId.Value == Guid.Empty ? "none" : catId.Value.ToString("D")),
                        Array.Empty<IValidator>(), false, false),
                    new ViewElementData(8, "Stock", untracked.Value ? "not tracked (∞)" : "counted",
                        Array.Empty<IValidator>(), false, false),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Confirm".Translate(), true, "Edit item", "Cancel".Translate());

                if (answers.Count == 0) return;

                _ = answers.TryGetValue(1, out var name);
                _ = answers.TryGetValue(2, out var brand);
                _ = answers.TryGetValue(3, out var desc);
                _ = answers.TryGetValue(4, out var costText);
                _ = answers.TryGetValue(5, out var priceText);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText)) return;

                if (!decimal.TryParse(priceText, money, CultureInfo.CurrentCulture, out var price) || price < 0)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That price didn't look like a number. Nothing has been changed.", "OK".Translate());
                    return;
                }

                if (!decimal.TryParse(costText ?? "", money, CultureInfo.CurrentCulture, out var cost) || cost < 0)
                    cost = current.Cost;   // blank or nonsense leaves the cost alone; it is not the operator's field

                // ⚠ THE EX PRICE IS DERIVED FROM THE CHOSEN BAND, never typed and never carried
                // over. The server guards `|price − exPrice × rate| ≤ 2p` because free-typed
                // ex-prices corrupted 47 live items — a £7.99 item with a £799.00 ex-price — and
                // with them every downstream VAT figure. ⚠ `rate` is a MULTIPLIER (1.2 = 20%), so
                // the ex price DIVIDES by it; multiplying makes a £10 item's ex price £12 and the
                // server rejects the write with a message that never mentions the units.
                //
                // ⚠ Falls back to the item's EXISTING pair ratio when the band is unknown to
                // `/api/Tax/Index` — which keeps an unclassified item editable instead of
                // unsaveable.
                var exPrice = chosenBand is not null && chosenBand.Rate > 0
                    ? Math.Round(price / chosenBand.Rate, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(price * (current.Price > 0 ? current.ExPrice / current.Price : 1m), 2,
                                 MidpointRounding.AwayFromZero);

                var (ok, problem) = await api.UpdateItemFieldsAsync(itemId, business, item =>
                {
                    item.Name = name.Trim();
                    item.Brand = string.IsNullOrWhiteSpace(brand) ? "-" : brand.Trim();
                    item.Desc = (desc ?? "").Trim();
                    item.Cost = cost;
                    item.Price = price;
                    item.ExPrice = exPrice;
                    item.TaxId = taxId.Value;
                    item.CatId = catId.Value;
                    item.StockUntracked = untracked.Value;
                });

                if (!ok)
                {
                    // ⚠ The SERVER's words. Its band guard explains exactly what is wrong and what
                    // to do about it — better than anything this screen could invent.
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "Plutus refused the change.", "OK".Translate());
                    return;
                }

                // ⚠ Pull the catalogue so the change is visible HERE. Without it the operator edits
                // an item, sees the old price on the list and in the basket, and reasonably concludes
                // the edit failed.
                await Services.Storage.CatalogueSyncService.SyncAsync();
                InitItems();

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Edited");
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.EditItem", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been changed.", "OK".Translate());
            }
        }

        /// <summary>
        /// Which VAT band the item sits in. Null means the operator backed out.
        ///
        /// ⚠ THE BAND IS NOT DERIVABLE FROM THE RATE. Zero-rated and Exempt both have rate 1.0 and
        /// are entirely different things — they land in different boxes on a VAT return — so the
        /// list shows NAMES and the id is what gets written. Collapsing them would be the one
        /// mistake this dropdown exists to prevent (`vat-exempt-must-stay-supported`).
        ///
        /// ⚠ Through `Modal`, because every one of these sheets is followed by another one, and two
        /// modals in quick succession is what threw the COMException that closed the till at the
        /// payment prompt.
        /// </summary>
        private static async Task<int?> PickTaxBandAsync(IReadOnlyList<TaxBandDto> bands, int currentId)
        {
            if (bands.Count == 0)
            {
                // ⚠ IT USED TO RETURN `currentId` IN SILENCE, and that is the bug Matt reported as
                // *"missing category and tax"*. An empty list meant the sheet never opened, so the
                // operator was never asked and never told — the screen simply behaved as though tax
                // bands were not part of editing an item. A capability that vanishes without a word
                // when a call fails is indistinguishable from one that was never built.
                //
                // ⚠ It still keeps the item's existing band, which is the only safe default: the
                // PUT binds the whole entity, so guessing a band here would re-rate the item.
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Plutus didn't send back any tax bands, so this item keeps the one it has. " +
                    "Everything else you change will still be saved.", "OK".Translate());
                return currentId;
            }

            var labels = bands
                .Select(b => $"{BandLabel(b, b.IdOne)}{(b.IdOne == currentId ? "  ✓" : "")}")
                .ToArray();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                App.Current.MainPage.DisplayActionSheet("Tax band", "Cancel".Translate(), null, labels));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return null;

            var index = Array.IndexOf(labels, picked);
            return index >= 0 ? bands[index].IdOne : null;
        }

        /// <summary>
        /// How a tax band reads to an operator: "Standard — 20%".
        ///
        /// ⚠ THE RULE LIVES IN `Client.Core/TaxBandLabel.cs`, not here, because the part that can be
        /// got wrong is the CONVERSION: `Rate` is a MULTIPLIER (1.2 = 20%), so the percentage is
        /// `(rate − 1) × 100`. A screen that quietly prints "1.2%" beside a 20% band is worse than
        /// one that shows no rate at all — and a rule in a private method on a viewmodel is a rule
        /// no test can reach. See `till-design.md` C1 and `TaxBandLabelTests`.
        /// </summary>
        private static string BandLabel(TaxBandDto band, int fallbackId)
            => Plutus.Client.Core.TaxBandLabel.For(band, fallbackId);

        /// <summary>Which category the item belongs to. Null means the operator backed out.</summary>
        private static async Task<Guid?> PickCategoryAsync(IReadOnlyList<CategoryDto> categories, Guid currentId)
        {
            if (categories.Count == 0)
            {
                // ⚠ Same silent skip, same fix — see PickTaxBandAsync.
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Plutus didn't send back any categories, so this item keeps the one it has. " +
                    "Everything else you change will still be saved.", "OK".Translate());
                return currentId;
            }

            var labels = categories
                .Select(c => $"{(string.IsNullOrWhiteSpace(c.Name) ? c.IdOne.ToString("D") : c.Name)}{(c.IdOne == currentId ? "  ✓" : "")}")
                .ToArray();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                App.Current.MainPage.DisplayActionSheet("Category", "Cancel".Translate(), null, labels));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return null;

            var index = Array.IndexOf(labels, picked);
            return index >= 0 ? categories[index].IdOne : null;
        }

        /// <summary>
        /// Whether this item's stock is counted. Null means the operator backed out.
        ///
        /// ⚠ IT MUST BE ASKED, not assumed. `StockUntracked` is written back on every edit because
        /// the PUT binds the whole entity — so an editor that does not offer it has to guess, and a
        /// wrong guess turns a carrier bag into stock-tracked goods (or the reverse) on every till
        /// in the estate. The wording matches the web till's, deliberately.
        /// </summary>
        private static async Task<bool?> PickStockTrackingAsync(bool current)
        {
            const string track = "Count stock for this item";
            const string dont = "Don't track stock (∞ — carrier bags, back-issues)";

            var labels = new[]
            {
                track + (current ? "" : "  ✓"),
                dont + (current ? "  ✓" : ""),
            };

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                App.Current.MainPage.DisplayActionSheet("Stock", "Cancel".Translate(), null, labels));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return null;
            return picked == labels[1];
        }

        private async void ExecuteUpdateItemStock(string itemId)
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
                        var idValidtors = new IValidator[]
                        {
                        new RequiredValidator()
                        };

                        var qtyValidators = new IValidator[]
                        {
                        new RequiredValidator(),
                        new IntegerValidator()
                        };

                        var viewElements = new ViewElementData[]
                        {
                            new ViewElementData(1, string.Format("IdArg".Translate(), "Item".Translate()), itemId, idValidtors.AsEnumerable(), false, false),
                            new ViewElementData(2, string.Format("ToIncrement/Decrement".Translate(), "Quantity".Translate()), "", qtyValidators.AsEnumerable(), false, true)
                        };

                        var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, "UpdateStock".Translate(), "Cancel".Translate());

                        if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                            return;

                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            var stock = db.Get<StockModel>().Where(s => s.ItemId.Equals(itemId)).FirstOrDefault();
                            if (stock == default(StockModel))
                            {
                                stock = new StockModel { ItemId = itemId, StoreId = App.GetViewModel().Store.Id };
                                db.Add(stock);
                            }
                            data.TryGetValue(2, out var quatityText);
                            stock.Quantity += int.Parse(quatityText);
                            if (!db.Save())
                                Debug.Write("Database save issue!");
                        }
                        InitItems();
                        return;
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
        /// <summary>
        /// Does this item match what the operator typed? Barcode, name, brand or description.
        ///
        /// ⚠ NULL-SAFE ON `Id` AND `Name` TOO. It was not: a catalogue row with a null name threw
        /// NullReferenceException out of the filter, which Syncfusion swallowed into its own layout
        /// pass — the list simply stopped updating and nothing appeared anywhere.
        /// </summary>
        private bool FilterItem(ItemModel item)
        {
            if (string.IsNullOrEmpty(SearchText)) return true;
            if (item is null) return false;

            return Contains(item.Id) || Contains(item.Name) || Contains(item.Brand) || Contains(item.Desc);

            bool Contains(string field) =>
                field?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        /*
        private void LoadItems()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                foreach (var item in db.Get<ItemModel>()
                    .Include(i => i.Stock)
                    .Include(i => i.Vat)
                    .OrderBy(i => i.Name)
                    .Skip(Items.Count)
                    .Where(item => !Items.Contains(item))
                    .Take(_limit)
                    .Select(item => item))
                    Items.Add(item);
            }
        }*/
        #endregion

        #region Disposeable Implementation
        protected override void Dispose(bool disposing)
        {
            Items = null;
            _searchText = null;
            base.Dispose(disposing);
        }
        #endregion
    }
}
