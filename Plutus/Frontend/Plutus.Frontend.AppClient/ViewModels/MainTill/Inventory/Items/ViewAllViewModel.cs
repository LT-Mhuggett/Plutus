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

        /// <summary>How many rows this screen is willing to read. ⚠ Named, not inline, because it
        /// is the number <see cref="CapNotice"/> has to tell the truth about.</summary>
        private const int BrowseLimit = 500;

        private bool _capped;

        /// <summary>
        /// ⚠ SAY WHEN THE LIST IS TRUNCATED. `BrowseAsync(500)` silently returns the first 500 rows
        /// of a catalogue that can hold twenty thousand — so an operator scrolling to the bottom of
        /// a shop's inventory reached "R" and reasonably concluded the rest had been deleted.
        ///
        /// A cap with no marker reads as completeness, which is the one thing it is not. Searching
        /// reaches the whole catalogue, so the notice says so rather than just apologising.
        /// </summary>
        public bool ShowCapNotice => _capped && !ShowEmptyNotice;

        public string CapNotice =>
            $"Showing the first {BrowseLimit} items. Search to reach the rest of the catalogue.";
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
            OnPropertyChanged(nameof(ShowCapNotice));
            OnPropertyChanged(nameof(CapNotice));
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
                        s => s.BrowseAsync(BrowseLimit, timeout.Token), timeout.Token);

                    // ⚠ A FULL PAGE MEANS THERE IS PROBABLY MORE. It cannot distinguish "exactly
                    // 500 items" from "the first 500 of 20,000" — so the notice is worded as a
                    // statement about what is SHOWN, which is true either way, rather than a claim
                    // about what was left out.
                    _capped = catalogue.Count >= BrowseLimit;

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
                        // ⚠ Schema v5. Brand in particular is why the column exists: `ItemSearch`
                        // matches on it, so without it this list and the scan box answered the same
                        // query differently.
                        Brand = c.Brand,
                        Desc = c.Desc,
                        Cost = c.CostPence / 100m,
                    }).ToList();

                    // ⚠ THE STOCK COLUMN WAS BLANK ON EVERY ROW, and blank reads as ZERO.
                    //
                    // The list bound `Stock.Quantity` — a legacy EF NAVIGATION PROPERTY that this
                    // mapping has never populated, because the v2 catalogue feed does not carry a
                    // quantity at all (`CatalogueItem` has no such field). MAUI bindings fail
                    // SILENTLY, so a null `Stock` rendered as an empty cell rather than an error:
                    // an operator reading that column would conclude the shop has none of anything.
                    //
                    // ⚠ "∞" or "—" UNTIL THE REAL COUNT ARRIVES — never a zero. `StockUntracked` is
                    // in the feed, so ∞ is a fact from the moment the rows render; "—" means "this
                    // screen has not been told", which is different from "none" and must not be
                    // rendered as 0. `FillStockLevelsAsync` below replaces the dashes.
                    for (var i = 0; i < loaded.Count; i++)
                        loaded[i].StockDisplay = catalogue[i].StockUntracked ? "∞" : "—";
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

                        // ⚠ AFTER the rows are on screen, never before. Stock counts come from the
                        // network; making the list wait for them would put a spinner between the
                        // operator and a catalogue the till already holds locally — and leave the
                        // screen blank whenever the line is down, which is when browsing matters
                        // most. The list renders, then the dashes fill in.
                        _ = FillStockLevelsAsync();
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("ViewAllViewModel.Bind", ex);
                    }
                });
            });
        }

        /// <summary>
        /// Ask the platform how many of each item the shop actually holds, and fill the column
        /// (WP10 / cutover step 25).
        ///
        /// ⚠ STOCK IS NOT IN THE CATALOGUE FEED, and should not be. It moves on every sale on every
        /// till in the shop, so pushing it down an effective-dated feed would either flood the feed
        /// or deliver a number already stale on arrival. It is read when a screen asks.
        ///
        /// ⚠ WHICH MEANS THE COLUMN IS A LIVE READ AND THE LIST IS NOT. A count shown here is true
        /// as of a moment ago; the rows around it come from a local catalogue that may be an hour
        /// old. That is the right trade for a browse screen, and it is why nothing on this screen
        /// may be used to decide a SALE — the basket resolves prices and stock itself.
        ///
        /// ⚠ FAILURE LEAVES THE DASHES. No count is not zero. An operator reading "0" against an
        /// item the shop has simply never counted will reorder it.
        /// </summary>
        private async Task FillStockLevelsAsync()
        {
            try
            {
                var tracked = Items.Where(i => i.StockDisplay != "∞" && !string.IsNullOrWhiteSpace(i.Id))
                                   .ToList();
                if (tracked.Count == 0) return;

                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null) return;   // nobody signed in, or no line — the dashes stand

                // ⚠ 200 AT A TIME, because the endpoint clamps to 200 and does so by TAKING the
                // first 200 rather than refusing. One page of 500 would come back with 300 silent
                // gaps, every one of which renders as "never counted" — a wrong answer that looks
                // exactly like the honest one.
                for (var offset = 0; offset < tracked.Count; offset += 200)
                {
                    var page = tracked.Skip(offset).Take(200).ToList();
                    var levels = await api.GetStockLevelsAsync(page.Select(i => i.Id).ToList());
                    if (levels is null) return;   // refused or unreachable — leave every dash alone

                    var byId = levels
                        .Where(l => !string.IsNullOrWhiteSpace(l.ItemIdOne))
                        .ToDictionary(l => l.ItemIdOne!, l => l, StringComparer.OrdinalIgnoreCase);

                    // ⚠ On the UI thread: `StockDisplay` raises PropertyChanged and the list is
                    // bound to it. Mutating it from a background thread is the kind of thing that
                    // works in testing and throws on a shop floor.
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var item in page)
                            if (byId.TryGetValue(item.Id, out var level))
                                item.StockDisplay = level.Display;
                    });
                }
            }
            catch (Exception ex)
            {
                // ⚠ Fire-and-forget from `InitItems` — an escape here has no caller to catch it.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.StockLevels", ex);
            }
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

        /// <summary>"Add item" — with a barcode when a scan brought us here, without when the
        /// operator pressed the button.</summary>
        Command _createItemCommand;
        public Command CreateItemCommand
        {
            get => _createItemCommand ?? (_createItemCommand = new Command<string>(ExecuteCreateItem));
        }

        Command _manageCategoriesCommand;
        public Command ManageCategoriesCommand
        {
            get => _manageCategoriesCommand ?? (_manageCategoriesCommand = new Command(ExecuteManageCategories));
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

                // ⚠ "Move to the Bin" is a DESTRUCTIVE-LOOKING action on a tap menu, so it is last
                // and it confirms. Binning withdraws the item from sale on every till in the
                // estate, including offline ones — it is not a local tidy-up.
                const string bin = "Move to the Bin…";
                const string stock = "Adjust stock…";

                // ⚠ Not offered for an UNTRACKED item. Its level is meaningless by design, and a
                // movement against it writes a number nothing will ever read.
                var actions = item.StockDisplay == "∞"
                    ? new[] { addToBasket, edit, bin }
                    : new[] { addToBasket, edit, stock, bin };

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    App.Current.MainPage.DisplayActionSheet(
                        item.Name ?? "Item", "Cancel".Translate(), null, actions));

                if (picked == addToBasket) ExecuteAddToBasket(item.Id);
                else if (picked == edit) ExecuteOpenEditItem(item.Id);
                else if (picked == stock) ExecuteAdjustStock(item);
                else if (picked == bin) ExecuteBinItem(item);
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
                // ⚠ THE REASON TRAVELS WITH THE RESULT. An empty list and a failed call are
                // different answers — "this shop has no tax bands" is a fact about the shop,
                // "I couldn't ask" is a fact about the till — and flattening them into one is what
                // made this whole capability look absent.
                var (bandList, bandProblem) = await api.GetTaxBandsAsync(business);
                var (categoryList, categoryProblem) = await api.GetCategoriesAsync(business);

                var bands = bandList ?? new List<TaxBandDto>();
                var categories = categoryList ?? new List<CategoryDto>();

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
                var taxId = await PickTaxBandAsync(bands, current.TaxId, bandProblem);
                if (taxId is null) return;                       // ⚠ Cancel means cancel, not "keep the old band"

                var catId = await PickCategoryAsync(categories, current.CatId, categoryProblem);
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
        /// Write stock off, or correct a count, from the till (WP10 / cutover step 25).
        ///
        /// ⚠⚠ EVERYTHING HERE IS A CHANGE, NEVER A COUNT, AND THE SCREEN MUST SAY SO. Stock is an
        /// append-only ledger: the server does `level.Quantity += qtyDelta`. A box labelled
        /// "quantity" that an operator fills in with what they counted would ADD their count to the
        /// existing one — 7 on the shelf, operator counts 7, stock becomes 14, nothing errors and
        /// nobody finds out until a stock take. So every prompt here asks **how many**, in a
        /// direction the operator has already chosen, and the CURRENT figure is shown beside it.
        ///
        /// ⚠ "Correct the count to N" is deliberately NOT offered. That is `POST /api/v1/stock/takes`,
        /// which sits under a controller-wide portal gate covering inter-store transfers too —
        /// reaching it from a till would grant transfers by accident. Recorded in
        /// `maui-whats-left.md`; a till adjusts, a stock take stays a portal job until it has its
        /// own gate.
        ///
        /// ⚠ A REASON IS COMPULSORY and the server refuses without one. An unexplained stock
        /// correction is indistinguishable from shrinkage being hidden, which is the entire reason
        /// this is gated at supervisor level rather than cashier.
        /// </summary>
        private async void ExecuteAdjustStock(ItemModel item)
        {
            try
            {
                if (item?.Id is null) return;

                // ⚠ EITHER CODE, MIRRORING THE SERVER. `POST /api/v1/stock/movements` is gated
                // `perm:portal.stock.adjust,pos.stock.adjust`, and this screen asked for the till
                // code ALONE until 2026-08-11 — so an **Owner**, who holds the portal code, was
                // refused BY THE TILL for something the platform would have accepted.
                //
                // ⚠ It matters most BEFORE the RBAC re-seed has run: until `Plutus.SeedMigrator
                // rbac` puts `pos.stock.adjust` onto the built-in roles, NOBODY holds it — so a
                // till-code-only check refused every operator on the estate, including the owner,
                // with a message that reads like deliberate policy. Nobody debugs that.
                var gate = Services.Security.TillGate.CheckAny(
                    App.GetViewModel().SignedInOperator, null,
                    PermissionCatalogue.PosStockAdjust, PermissionCatalogue.PortalStockAdjust);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                const string wroteOff = "Write some off (damaged, lost, expired)";
                const string cameIn = "Add some (found, returned to stock, delivery)";

                var direction = await Services.UIHandeling.Modal.ShowAsync(() =>
                    App.Current.MainPage.DisplayActionSheet(
                        $"{item.Name} — {item.StockDisplay} in stock",
                        "Cancel".Translate(), null, wroteOff, cameIn));

                if (direction != wroteOff && direction != cameIn) return;

                var isWriteOff = direction == wroteOff;

                // ⚠ "HOW MANY", not "the new total". The wording is the guard: there is no way to
                // phrase this that makes typing a counted total the obvious thing to do.
                var typed = await App.Current.MainPage.DisplayPromptAsync(
                    isWriteOff ? "Write off how many?" : "Add how many?",
                    $"“{item.Name}” currently shows {item.StockDisplay}. " +
                    "This changes the count by the number you type — it is not the new total.",
                    "OK".Translate(), "Cancel".Translate(), keyboard: Microsoft.Maui.Keyboard.Numeric);

                if (string.IsNullOrWhiteSpace(typed)) return;

                if (!int.TryParse(typed.Trim(), out var howMany) || howMany <= 0)
                {
                    // ⚠ A NEGATIVE typed into "write off how many" would flip the direction the
                    // operator just chose, so only a positive count is accepted and the SIGN comes
                    // from the choice above.
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Enter how many, as a whole number more than zero. Nothing has been changed.",
                        "OK".Translate());
                    return;
                }

                var reason = await App.Current.MainPage.DisplayPromptAsync(
                    "Why?",
                    isWriteOff
                        ? "Damaged, lost, expired, used in the shop…"
                        : "Found, returned to stock, delivery not booked in…",
                    "OK".Translate(), "Cancel".Translate(), maxLength: 120);

                if (string.IsNullOrWhiteSpace(reason))
                {
                    // ⚠ Cancel and blank are BOTH treated as "don't", because the server refuses a
                    // blank reason anyway and a round trip to be told so helps nobody.
                    return;
                }

                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Adjusting stock needs someone signed in and a connection to Plutus.", "OK".Translate());
                    return;
                }

                var storeId = await Services.Storage.TillStoreAccess.UseAsync(
                    s => s.GetIntMetaAsync(Plutus.Client.Storage.MetaKeys.StoreId));

                // ⚠ THE SIGN COMES FROM THE CHOICE, and `WriteOff` must be negative or the server
                // refuses it. The operator never types a minus sign — asking somebody to get a sign
                // right on a stock ledger at a counter is asking for the wrong answer.
                var (ok, problem) = await api.PostStockMovementAsync(
                    item.Id,
                    isWriteOff ? "WriteOff" : "Adjustment",
                    isWriteOff ? -howMany : howMany,
                    reason.Trim(),
                    storeId);

                if (!ok)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "Plutus wouldn't record that stock change.", "OK".Translate());
                    return;
                }

                // ⚠ Re-read the levels so the column moves. Without it the operator writes off two,
                // sees the same number, and does it again — and the ledger takes both.
                await FillStockLevelsAsync();

                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    isWriteOff
                        ? $"Wrote off {howMany} × “{item.Name}”."
                        : $"Added {howMany} × “{item.Name}”.",
                    "OK".Translate());

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Stock Adjusted");
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.AdjustStock", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been changed.", "OK".Translate());
            }
        }

        /// <summary>
        /// Create / rename / reassign / delete categories (WP10 / cutover step 25).
        ///
        /// ⚠ The catalogue is re-synced only if something CHANGED, because an item's category is on
        /// the feed — a rename or a reassign that this screen does not pick up leaves the editor's
        /// dropdown naming a category that no longer exists.
        /// </summary>
        private async void ExecuteManageCategories()
        {
            try
            {
                if (await Services.Inventory.CategoryManager.ShowAsync())
                {
                    await Services.Storage.CatalogueSyncService.SyncAsync();
                    InitItems();
                }
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.Categories", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been changed.", "OK".Translate());
            }
        }

        /// <summary>
        /// Move an item to the Bin — withdraw it from sale (WP10 / cutover step 25).
        ///
        /// ⚠ IT IS NOT A LOCAL TIDY-UP AND THE WORDING MUST SAY SO. Binning stamps the item on the
        /// PLATFORM, the catalogue feed carries it to every till as a tombstone, and every read
        /// path on every till then refuses it — including tills that have been offline since. That
        /// is exactly the behaviour a recall needs, and exactly the behaviour somebody clearing
        /// clutter off a list does not expect.
        ///
        /// ⚠ NOTHING IS DESTROYED. The item keeps its id, so a restore does not split its sales
        /// history, and past sale lines still resolve. The confirmation says that too — an operator
        /// who thinks this is permanent will not use it when they should.
        /// </summary>
        private async void ExecuteBinItem(ItemModel item)
        {
            try
            {
                if (item?.Id is null) return;

                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.InventoryBulk);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                var confirmed = await Services.UIHandeling.Modal.ShowAsync(() =>
                    App.Current.MainPage.DisplayAlert(
                        "Move to the Bin?",
                        $"“{item.Name}” will stop selling on EVERY till, including tills that are " +
                        "offline right now. Nothing is deleted — it keeps its sales history and can " +
                        "be restored from the portal.",
                        "Move to the Bin", "Cancel".Translate()));

                if (!confirmed) return;

                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Binning an item needs someone signed in and a connection to Plutus.", "OK".Translate());
                    return;
                }

                var (ok, problem) = await api.BinItemsAsync(new[] { item.Id }, bin: true);
                if (!ok)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "Plutus refused that.", "OK".Translate());
                    return;
                }

                // ⚠ Pull the catalogue so the row LEAVES THIS LIST. Without it the operator bins an
                // item, sees it still sitting there, and bins it again — and the second attempt
                // succeeds silently because binning is idempotent (`BinnedAtUtc ??=`), which teaches
                // them the button does nothing.
                await Services.Storage.CatalogueSyncService.SyncAsync();
                InitItems();

                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    $"“{item.Name}” has been moved to the Bin.", "OK".Translate());

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Binned");
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.BinItem", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been changed.", "OK".Translate());
            }
        }

        /// <summary>
        /// Create a catalogue item (WP10 / cutover step 25 — the add-unknown-scan flow).
        ///
        /// ⚠ IT TAKES THE BARCODE FROM THE SCAN. That is the whole point: an operator holding
        /// something the till does not know should not have to read the barcode off the packaging
        /// and type it in, which is the step where a digit gets dropped and a second, unsellable
        /// item appears in the catalogue.
        ///
        /// ⚠ THE BARCODE IS CHECKED FREE FIRST, AND THE CLASH IS SHOWN. It is half the composite
        /// primary key, so a duplicate is rejected by the database — the web till showed a raw
        /// "API 500" before it learned to check (`InventoryPage.tsx checkBarcodeFree`). And the
        /// honest response is not "that's taken": it is to NAME the item that has it, because
        /// nine times out of ten the answer is "you already stock this".
        /// </summary>
        public async void ExecuteCreateItem(string scannedBarcode)
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

                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Adding an item needs someone signed in and a connection to Plutus.", "OK".Translate());
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

                const NumberStyles money = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                                           | NumberStyles.AllowDecimalPoint;

                // ── the barcode, and whether anything already has it ──
                var barcode = (scannedBarcode ?? "").Trim();
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    var typed = await App.Current.MainPage.DisplayPromptAsync(
                        "New item", "Scan or type the barcode.", "OK".Translate(), "Cancel".Translate(),
                        maxLength: 20);
                    if (string.IsNullOrWhiteSpace(typed)) return;
                    barcode = typed.Trim();
                }

                // ⚠ 20 CHARACTERS, matching the web till's `maxLength={20}` and the legacy column.
                // A longer code is truncated by the database, so two different products can end up
                // sharing a key — and the second one silently fails to insert.
                if (barcode.Length > 20)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "A barcode can be at most 20 characters. Nothing has been added.", "OK".Translate());
                    return;
                }

                var clash = await api.GetItemAsync(barcode, business);
                if (clash is not null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        $"“{clash.Name}” already has the barcode {barcode}" +
                        (clash.BinnedAtUtc is null ? "." : " (it's in the bin).") +
                        " Nothing has been added.", "OK".Translate());
                    return;
                }

                var (bandList, bandProblem) = await api.GetTaxBandsAsync(business);
                var (categoryList, categoryProblem) = await api.GetCategoriesAsync(business);
                var bands = bandList ?? new List<TaxBandDto>();
                var categories = categoryList ?? new List<CategoryDto>();

                // ⚠ A NEW ITEM HAS NO BAND TO FALL BACK ON, unlike an edit. Without a real list
                // there is no honest way to price it — the ex price cannot be derived and the
                // server's pair guard would reject the write — so this refuses rather than
                // guessing 20%, which would be a VAT decision made by a default.
                if (bands.Count == 0)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        (bandProblem ?? "Plutus didn't send back any tax bands.") +
                        " A new item can't be priced without one, so nothing has been added.",
                        "OK".Translate());
                    return;
                }

                var taxId = await PickTaxBandAsync(bands, bands[0].IdOne, bandProblem);
                if (taxId is null) return;

                var catId = await PickCategoryAsync(
                    categories, categories.Count > 0 ? categories[0].IdOne : Guid.Empty, categoryProblem);
                if (catId is null) return;

                var untracked = await PickStockTrackingAsync(false);
                if (untracked is null) return;

                var chosenBand = bands.FirstOrDefault(b => b.IdOne == taxId.Value);
                var chosenCategory = categories.FirstOrDefault(c => c.IdOne == catId.Value);

                var elements = new List<ViewElementData>
                {
                    new ViewElementData(1, "Barcode", barcode, Array.Empty<IValidator>(), false, false),
                    new ViewElementData(2, "Name".Translate(), "",
                        new IValidator[] { new RequiredValidator() }, false, true),
                    new ViewElementData(3, "Brand", "", Array.Empty<IValidator>(), false, true),
                    new ViewElementData(4, "Description", "", Array.Empty<IValidator>(), false, true),
                    new ViewElementData(5, "Cost (£)", "0.00",
                        new IValidator[] { new CurrencyValueValidator(money) }, false, true),
                    new ViewElementData(6, "Price inc tax (£)", "",
                        new IValidator[] { new RequiredValidator(), new CurrencyValueValidator(money) }, false, true),
                    new ViewElementData(7, "Tax band",
                        Plutus.Client.Core.TaxBandLabel.For(chosenBand, taxId.Value),
                        Array.Empty<IValidator>(), false, false),
                    new ViewElementData(8, "Category",
                        chosenCategory?.Name ?? (catId.Value == Guid.Empty ? "none" : catId.Value.ToString("D")),
                        Array.Empty<IValidator>(), false, false),
                };

                // ⚠ ONLY OFFERED WHEN THE ITEM IS TRACKED. Asking for an opening count on a carrier
                // bag invites somebody to type one, and a count on an untracked item is a number
                // nothing will ever move — the web till hides the same field for the same reason.
                if (!untracked.Value)
                    elements.Add(new ViewElementData(9, "Opening stock (optional)", "",
                        Array.Empty<IValidator>(), false, true));

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Confirm".Translate(), true, "New item", "Cancel".Translate());

                if (answers.Count == 0) return;

                _ = answers.TryGetValue(2, out var name);
                _ = answers.TryGetValue(3, out var brand);
                _ = answers.TryGetValue(4, out var desc);
                _ = answers.TryGetValue(5, out var costText);
                _ = answers.TryGetValue(6, out var priceText);
                _ = answers.TryGetValue(9, out var stockText);

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText)) return;

                if (!decimal.TryParse(priceText, money, CultureInfo.CurrentCulture, out var price) || price < 0)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That price didn't look like a number. Nothing has been added.", "OK".Translate());
                    return;
                }

                if (!decimal.TryParse(costText ?? "", money, CultureInfo.CurrentCulture, out var cost) || cost < 0)
                    cost = 0m;

                // ⚠ The ex price DIVIDES by the band multiplier — see `ExecuteOpenEditItem`. A new
                // item has no existing pair to fall back on, which is why an empty band list
                // refused the whole operation above.
                var exPrice = chosenBand is { Rate: > 0 }
                    ? Math.Round(price / chosenBand.Rate, 2, MidpointRounding.AwayFromZero)
                    : price;

                var (ok, problem) = await api.CreateItemAsync(new ItemDto
                {
                    Id = barcode,
                    IdOne = barcode,
                    Name = name.Trim(),
                    // ⚠ "-" not "", matching the web till, so one column does not end up holding
                    // two different placeholders depending on which till created the row.
                    Brand = string.IsNullOrWhiteSpace(brand) ? "-" : brand.Trim(),
                    Desc = (desc ?? "").Trim(),
                    Cost = cost,
                    Price = price,
                    ExPrice = exPrice,
                    TaxId = taxId.Value,
                    CatId = catId.Value,
                    StockUntracked = untracked.Value,
                }, business);

                if (!ok)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "Plutus refused the new item.", "OK".Translate());
                    return;
                }

                // ⚠ SEPARATE CALL, AND ITS FAILURE IS REPORTED SEPARATELY. The item exists at this
                // point whatever happens next — saying "that didn't work" would send somebody to
                // create it a second time, and the barcode check would then refuse them.
                var stockNote = "";
                if (!untracked.Value && int.TryParse(stockText ?? "", out var qty) && qty > 0)
                {
                    var storeId = await Services.Storage.TillStoreAccess.UseAsync(
                        s => s.GetIntMetaAsync(Plutus.Client.Storage.MetaKeys.StoreId));

                    var (stockOk, stockProblem) = await api.CreateStockAsync(barcode, qty, business, storeId ?? 0);
                    stockNote = stockOk
                        ? $" with {qty} in stock"
                        : $" — but its opening stock was refused: {stockProblem}";
                }

                await Services.Storage.CatalogueSyncService.SyncAsync();
                InitItems();

                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    $"Added “{name.Trim()}”{stockNote}.", "OK".Translate());

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Created");
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here closes the till.
                Services.Analytics.CrashLog.Write("ViewAllViewModel.CreateItem", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been added.", "OK".Translate());
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
        private static async Task<int?> PickTaxBandAsync(
            IReadOnlyList<TaxBandDto> bands, int currentId, string problem)
        {
            if (bands.Count == 0)
            {
                // ⚠ IT USED TO RETURN `currentId` IN SILENCE, and that is the bug Matt reported as
                // *"missing category and tax"*. An empty list meant the sheet never opened, so the
                // operator was never asked and never told — the screen simply behaved as though tax
                // bands were not part of editing an item. A capability that vanishes without a word
                // when a call fails is indistinguishable from one that was never built.
                //
                // ⚠ AND THE REASON IS NAMED. "Plutus didn't send any" is still a dead end if the
                // real answer was a 403 or a 500; the client now carries the status back, so the
                // person in front of the till is told what to do about it instead of being told
                // that a fact about the network is a fact about their shop.
                //
                // ⚠ It still keeps the item's existing band, which is the only safe default: the
                // PUT binds the whole entity, so guessing a band here would re-rate the item.
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    (problem ?? "Plutus didn't send back any tax bands.") +
                    " This item keeps the band it has; everything else you change will still be saved.",
                    "OK".Translate());
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
        private static async Task<Guid?> PickCategoryAsync(
            IReadOnlyList<CategoryDto> categories, Guid currentId, string problem)
        {
            if (categories.Count == 0)
            {
                // ⚠ Same silent skip, same fix — see PickTaxBandAsync.
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    (problem ?? "Plutus didn't send back any categories.") +
                    " This item keeps the category it has; everything else you change will still be saved.",
                    "OK".Translate());
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
        /// Does this item match what the operator typed?
        ///
        /// ⚠ THE RULE IS `SharedKernel.ItemSearch`, NOT A SECOND OPINION — cutover step 25's
        /// *"delete `ViewAllViewModel.FilterItems`"*, and it is a C1 rule with a C2 history: the
        /// same matching logic once existed three times (the server's `ItemParameters.Tokenise`,
        /// the web till's `offline.ts`, and this) and two of the copies carried "keep in sync"
        /// comments, which is a comment admitting the problem rather than fixing it.
        ///
        /// ⚠ WHAT THE LOCAL COPY GOT WRONG, both ways round:
        ///   • it matched on **Desc**, which the shared rule deliberately excludes — *"adding a
        ///     fourth field here without adding it to the server changes what a till finds and
        ///     nothing would say so"*. An item found by its description here and nowhere else is a
        ///     till whose search cannot be reasoned about;
        ///   • it did a whole-string `Contains`, so **"batman one" found nothing** in this list
        ///     while finding *Batman Year One* in the scan box two tabs away. Two search boxes in
        ///     one app, disagreeing, is worse than either being wrong on its own.
        ///
        /// ⚠ It also honours `MatchAllWordsSetting`, which the local copy ignored — so the browse
        /// list and the scan box now answer to the same preference.
        /// </summary>
        private bool FilterItem(ItemModel item)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (item is null) return false;

            return ItemSearch.Matches(
                SearchText, App.GetViewModel().MatchAllWordsSetting, item.Name, item.Id, item.Brand);
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
