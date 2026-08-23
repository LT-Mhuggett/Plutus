using CommonPOSLibrary.Exceptions;
using Mapster;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.MessagingCenter;
using CustomViews.Structs;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Newtonsoft.Json;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.SharedKernel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Till
{
    public class TillViewModel : BaseViewModel
    {
        #region Private Fields
        private bool _isDesktop;
        private string _itemId;
        private int _quantity;
        private bool _pickerIsOpen;
        private IBasketRecord _selectedBasketRecord;
        #endregion

        #region Properties
        #region Public
        public bool IsDesktop
        {
            get => _isDesktop;
            set => SetProperty(ref _isDesktop, value);
        }
        public double TopBarFontSize => new Label().FontSize;

        /// <summary>
        /// The noticeboard banner at the top of this page — pick-from-floor notes and platform
        /// announcements (WP5b).
        ///
        /// ⚠ ITS OWN VIEWMODEL, not another dozen properties on this one. This class is already the
        /// largest on the till, and the banner has nothing to do with selling: keeping it separate
        /// means the XAML binds `{Binding Notices.Rows}` and the two can be read apart.
        ///
        /// ⚠ Filled by <see cref="Services.Sync.TillCadence"/>, redrawn by the page on each tick.
        /// It does not poll on its own.
        /// </summary>
        public NoticeboardViewModel Notices { get; } = new();
        public string ItemId
        {
            get => _itemId;
            set => SetProperty(ref _itemId, value);
        }
        /// <summary>
        /// How many of the next scanned item go in the basket.
        ///
        /// ⚠ CLAMPED TO AT LEAST 1, IN THE SETTER. This used to be a Syncfusion `SfNumericEntry`
        /// whose `Minimum="1"` did the clamping, so the rule lived in a XAML attribute on a control
        /// the till no longer uses (Matt, 2026-08-10: *"I am not going to renew Syncfusion"*). A
        /// plain `Entry` will happily hand over 0, or −3, and `IncrementQuantity(0)` adds a line
        /// that charges nothing while looking exactly like a sale.
        /// ⚠ A rule that lives in a control's markup is a rule that leaves with the control.
        /// </summary>
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value < 1 ? 1 : value);
        }

        /// <summary>The − and + either side of the quantity box, which is how a touch till changes
        /// it. ⚠ The decrement cannot go below 1: the setter refuses, so the button is safe to
        /// press repeatedly.</summary>
        Command _quantityUpCommand;
        public Command QuantityUpCommand => _quantityUpCommand ??= new Command(() => Quantity += 1);

        Command _quantityDownCommand;
        public Command QuantityDownCommand => _quantityDownCommand ??= new Command(() => Quantity -= 1);
        public ObservableCollection<SavedTransactionModel> StoredTransactions { get; } = new ObservableCollection<SavedTransactionModel>();

        /// <summary>
        /// Is there anything parked to retrieve? Drives the **Retrieve** button's enabled state.
        ///
        /// ⚠⚠ WHY A BUTTON EXISTS AT ALL NOW. Matt, 2026-08-18: *"How do I retrieve a basket in the
        /// MAUI till? I have saved 2, but cant retreive them. Should there be two buttons? Save and
        /// retrieve, with the retreive unclickable if you have nothing to retrieve."*
        ///
        /// It was never missing — it was a TOOLBAR ITEM labelled "Baskets", inserted into the page's
        /// toolbar when the first basket is parked and removed when the last one goes. So the feature
        /// worked and the person using it could not find it, which is the third time that has happened
        /// here (the refund button, the reprint, this). **A capability nobody can reach is not a
        /// capability**, so it is now a button next to Save, where the thing it undoes lives.
        ///
        /// ⚠ And "unclickable if you have nothing" is not only tidier — `ExecuteRetrieveTransaction`
        /// called `StoredTransactions.First()` on an empty collection, which throws
        /// `InvalidOperationException` out of an `async void`. The button being disabled is the first
        /// guard; the method now checks as well, because a disabled button is a UI state and a crash
        /// is a crash.
        /// </summary>
        public bool HasStoredTransactions => StoredTransactions.Count > 0;
        public ObservableCollection<IBasketRecord> Basket { get; } = new ObservableCollection<IBasketRecord>();

        /// <summary>
        /// The carrier bags this shop sells — one button each, cheapest first (ruling 2026-08-19).
        ///
        /// ⚠⚠ Matt: *"That creates the 5p and 20p bags at the back and that pushes down to the tills."*
        /// It replaces `DefaultBagId`, ONE barcode held per device, which is how this till came to
        /// offer a bag whose barcode no item had. The portal owns the list; nothing is set here.
        ///
        /// ⚠ A SHOP SELLS MORE THAN ONE. A single-use bag at the statutory minimum and a dearer bag for
        /// life sit side by side, so this is a collection and the buttons carry their prices — "Bag"
        /// alone made the cashier remember which one the till happened to be set to.
        ///
        /// ⚠ Read from the local cache, never the network: this runs in a constructor and on the UI
        /// thread. <see cref="RedrawCarrierBags"/> refills it when the cadence says it moved.
        /// </summary>
        public ObservableCollection<Plutus.Contracts.Client.CarrierBagDto> CarrierBags { get; }
            = new ObservableCollection<Plutus.Contracts.Client.CarrierBagDto>();

        /// <summary>
        /// Refill the bag buttons from the cache.
        ///
        /// ⚠ MUST BE CALLED ON THE UI THREAD — it writes a collection a `BindableLayout` is bound to.
        /// The page marshals; see `TillView.OnCarrierBagsChanged`.
        ///
        /// ⚠ Rebuilt wholesale rather than diffed: it is two or three buttons, and the cadence only
        /// signals when something actually changed.
        /// </summary>
        public void RedrawCarrierBags()
        {
            CarrierBags.Clear();
            foreach (var bag in Services.Sales.CarrierBags.Bags) CarrierBags.Add(bag);
        }
        public IBasketRecord SelectedBasketRecord
        {
            get => _selectedBasketRecord;
            set => SetProperty(ref _selectedBasketRecord, value);
        }
        /// <summary>
        /// What the basket is worth, for the two labels at the bottom of the till screen.
        ///
        /// ⚠⚠ POUNDS-SHAPED VIEWS OVER ONE PENCE SUM, since 2026-08-21 (step 11b). This was
        /// `Basket.Sum(bR => bR.Price * …)` — a `decimal` re-derivation of
        /// <see cref="Services.Storage.CheckoutCommit.BasketMoneyPence"/>, which is the figure the
        /// commit's reconciliation guard checks the payload against. **Four copies of that sum lived
        /// in this file** and one of them ended `Pence.FromDecimal(sale.Total)` — rounding the sum
        /// instead of the lines, which `BasketMoneyPence`'s own header forbids in as many words.
        ///
        /// ⚠ They agreed only because `Price` is an exact projection of `PricePence`. Nothing was
        /// holding that, and the first person to give a basket record a price from anywhere else
        /// would have produced a till whose screen and payload differ by a penny — visible to the
        /// operator only as the commit refusing a sale it cannot explain.
        ///
        /// ⚠ STILL `decimal`, and that is not an oversight: `TillView.xaml` binds both with
        /// `StringFormat='{0:C2}'`, and a `long` behind that formatter renders £3.30 as **£330.00**,
        /// silently. Same reason `IBasketRecord.Price` stayed a decimal.
        /// </summary>
        public decimal SaleExTax => Services.Storage.CheckoutCommit.BasketMoneyExPence(Basket) / 100m;

        /// <inheritdoc cref="SaleExTax"/>
        public decimal SaleIncTax => Services.Storage.CheckoutCommit.BasketMoneyPence(Basket) / 100m;
        public ObservableCollection<DiscountModel> Alterations { get; } = new ObservableCollection<DiscountModel>();
        public ObservableCollection<string> AlterationNames
        {
            get
            {
                var discountNames = new ObservableCollection<string>();
                foreach (var discount in Alterations)
                {
                    discountNames.Add(discount.Name);
                }
                return discountNames;
            }
        }
        /// <summary>
        /// ⚠ VESTIGIAL, and kept only so the removal is visible. It drove `SfPicker.IsOpen`; the
        /// alterations picker is a `DisplayActionSheet` now (2026-08-10, Syncfusion removal), so
        /// nothing reads or writes this any more. It goes with the rest of the Syncfusion clean-up
        /// in `Build/To do/MAUI-retrofit.md` §10.
        /// </summary>
        [Obsolete("The alterations picker is a DisplayActionSheet now. Nothing binds this.")]
        public bool PickerOpen
        {
            get => _pickerIsOpen;
            set => SetProperty(ref _pickerIsOpen, value);
        }
        #endregion
        #endregion

        public TillViewModel()
        {
            #region Init
            Title = "Till".Translate();
            Icon = "md-store";

            // ⚠ THE BASKET OWNS THE CATALOGUE SYNC. A sync mid-basket rewrites prices under the
            // operator's hands: a line added before the tick and one added after would come from
            // different price lists, in one sale, and the receipt would be the only evidence it
            // happened. The heartbeat and the outbox drain are NOT held — neither touches the
            // catalogue, and a queued sale should not wait for a customer to finish paying.
            Services.Sync.TillCadence.BasketIsOpen = () => Basket.Count > 0;

            #region Events
            // ⚠⚠ THE "Baskets" TOOLBAR ITEM IS GONE — 2026-08-22. Matt, of the MAUI app bar:
            // *"Baskets in the top right shouldnt be there."*
            //
            // It was a DUPLICATE of the Retrieve button, and this file's own `HasStoredTransactions`
            // comment says why: the toolbar item was the original way to reach a parked basket, Matt
            // could not find it (*"I have saved 2, but cant retreive them"*), and it was replaced on
            // 2026-08-18 by a Retrieve button next to Save — "where the thing it undoes lives". The
            // button landed; the toolbar item was never removed, so both existed and only one was
            // discoverable.
            //
            // ⚠ It also cost the app bar its right-hand corner: an item inserted at index 0 of the
            // page toolbar sits exactly where the web till puts the clock, ❓ and 👥.
            StoredTransactions.CollectionChanged += (sender, e) =>
                // ⚠ The Retrieve button reads this — MAUI will not work it out on its own.
                OnPropertyChanged(nameof(HasStoredTransactions));
            Basket.CollectionChanged += (sender, e) =>
            {
                if (e.NewItems != null)
                    foreach (INotifyPropertyChanged added in e.NewItems)
                        added.PropertyChanged += BasketRecordOnPropertyChanged;
                if (e.OldItems != null)
                    foreach (INotifyPropertyChanged removed in e.OldItems)
                        removed.PropertyChanged -= BasketRecordOnPropertyChanged;
                OnPropertyChanged(nameof(Basket));
                OnPropertyChanged(nameof(SaleExTax));
                OnPropertyChanged(nameof(SaleIncTax));

                // ⚠⚠ THE MEMBER'S DISCOUNT FOLLOWS THE BASKET (step 27). The web till re-applies it
                // reactively on `[customer, lines]`; this is that effect. Doing it here rather than
                // at each mutating command is deliberate — there are a dozen ways a line enters or
                // leaves this basket, and the one somebody forgets is the one where a member is
                // silently charged full price.
                //
                // ⚠ RE-ENTRANT BY CONSTRUCTION: the refresh adds and removes `BasketAlteration`s,
                // which fires this very handler. `RefreshAutoDiscounts` guards on a flag and is a
                // no-op while it is running.
                //
                // ⚠⚠ AN AUTOMATIC DISCOUNT THE OPERATOR REMOVED STAYS REMOVED. A removal seen while
                // the flag is DOWN is the operator's own — ours all happen inside it — so that is the
                // one reliable way to tell "somebody took this off" from "we are rebuilding it".
                // Without this the rebuild below puts it straight back and full price is unreachable.
                if (!_refreshingMemberDiscount && e.OldItems != null)
                    foreach (var removed in e.OldItems.OfType<BasketAlteration>())
                        if (removed.Automatic && removed.ItemsAssocitated != null)
                            foreach (var line in removed.ItemsAssocitated)
                                if (!_autoDiscountWaived.Any(w => ReferenceEquals(w, line)))
                                    _autoDiscountWaived.Add(line);

                RefreshAutoDiscounts();
            };
            Alterations.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(Alterations));
                OnPropertyChanged(nameof(AlterationNames));
            };
            #endregion
            // ⚠ OFF THE UI THREAD, and off the legacy database (cutover step 18). This opened a
            // SQLite connection and read it SYNCHRONOUSLY inside `BeginInvokeOnMainThread` — i.e.
            // the till screen was built while the main thread waited on disk I/O, and on a
            // portal-provisioned till the read is against a legacy file that is created, migrated
            // and then found empty. The parked baskets live in the v2 store now.
            _ = Task.Run(async () =>
            {
                try
                {
                    var parked = await Services.Storage.TillStoreAccess.UseAsync(s => s.ListBasketsAsync());

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var basket in parked)
                            StoredTransactions.Add(new SavedTransactionModel
                            {
                                Id = basket.Id.ToString("D"),
                                Name = basket.Name,
                            });
                    });
                }
                catch (Exception ex)
                {
                    // A till that cannot list its parked baskets must still sell.
                    CrashLog.Write("TillViewModel.LoadParkedBaskets", ex);
                }
            });

            // The shop's scheduled discount rules, from the CACHE, so the first line scanned already
            // gets its discount rather than waiting for a cadence tick.
            //
            // ⚠ OFF THE UI THREAD — it opens the v2 store. `LoadDiscountRulesAsync` marshals back to
            // the main thread before touching the basket, and swallows everything: a till that cannot
            // read its promotions must still sell, at the shelf price.
            _ = Task.Run(LoadDiscountRulesAsync);

            MessagingCenter.Subscribe<Inventory.Items.ViewAllViewModel, string>(this, "AddToBasket", (sender, arg) =>
            {
                ExecuteItemAddArg(arg);
            });

            #endregion
            IsDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
            Quantity = 1;

            // ⚠ From the CACHE, so the buttons are there on the first draw rather than a minute later.
            // The cadence refreshes them and the page redraws on `Changed` — a constructor is exactly
            // where this app freezes data for a whole session (pitfall 17), so this is the seed only.
            RedrawCarrierBags();
        }

        #region Commands
        #region Items
        #region Adding
        #region Manual
        private Command _manualAddCommand;

        public Command ManualAddCommand => _manualAddCommand ?? (_manualAddCommand = new Command(ExecuteItemAdd, () => !string.IsNullOrEmpty(ItemId)));

        #endregion
        #region Manual with arg
        // ⚠ L13 — `ManualAddCommandArg` removed 2026-08-23: a Command wrapper bound to nothing. The
        // LIVE `ManualAddCommand` above is what the scan box uses (`TillView.xaml` ReturnCommand).
        // ⚠ The region itself is KEPT because its `#endregion` is further down and pairing them by
        // eye is how you get an unbalanced file that will not compile — which is exactly what the
        // first attempt at this deletion did.

        private Command _addBagCommand;

        /// <summary>
        /// Ring up a carrier bag (ruling 2026-08-19).
        ///
        /// ⚠ It goes through the SAME add-by-barcode path as everything else, because a bag IS an
        /// ordinary catalogue item — taxed, discountable, refundable, on the receipt. Nothing about
        /// selling one is special, and a separate path would be a second place for the price to come
        /// from.
        /// </summary>
        public Command AddBagCommand => _addBagCommand ?? (_addBagCommand =
            new Command<Plutus.Contracts.Client.CarrierBagDto>(
                bag => ExecuteItemAddArg(bag.IdOne),
                bag => bag is not null && !string.IsNullOrEmpty(bag.IdOne)));

        #endregion
        #region AutoScan

        private Command _autoScanCommand;

        public Command AutoScanCommand => _autoScanCommand ?? (_autoScanCommand = new Command(ExecuteAutoScan));

        #endregion
        #endregion
        #region Removing

        private Command _removeOneCommandArg;

        public Command RemoveOneCommandArg => _removeOneCommandArg ?? (_removeOneCommandArg = new Command<IBasketRecord>(ExecuteRemoveOne));

        private Command _removeAllCommandArg;

        public Command RemoveAllCommandArg => _removeAllCommandArg ?? (_removeAllCommandArg = new Command<IBasketRecord>(ExecuteRemoveAll));

        #endregion
        #region Adjust

        private Command _adjustCommandArg;

        public Command AdjustCommandArg => _adjustCommandArg ?? (_adjustCommandArg = new Command<BasketItem>(ExecuteAdjustItem));

        #endregion
        #region Return

        private Command _returnCommandArg;

        public Command ReturnCommandArg => _returnCommandArg ?? (_returnCommandArg = new Command<BasketItem>(ExecuteReturn));

        private Command _revertReturnCommandArg;

        public Command RevertReturnCommandArg => _revertReturnCommandArg ?? (_revertReturnCommandArg = new Command<BasketItem>(ExecuteRevertReturn));

        #endregion
        #endregion
        #region Transactions
        #region Alter

        private Command _alterTransactionSelectorCommand;

        public Command AlterTransactionSelectorCommand => _alterTransactionSelectorCommand ?? (_alterTransactionSelectorCommand = new Command(ExecuteAlterTransactionSelector));
        // ⚠ L13 — `AlterTransactionCommand` removed 2026-08-23: a Command wrapper bound to nothing.
        // ⚠ `ExecuteAlterTransaction` STAYS — the SELECTOR command above dispatches to it (:2128).

        #endregion
        #region Store

        private Command _storeTransactionCommand;

        public Command StoreTransactionCommand => _storeTransactionCommand ?? (_storeTransactionCommand = new Command(ExecuteStoreTransaction));

        #endregion
        #region Retrieve

        private Command _retrieveTransactionCommand;

        public Command RetrieveTransactionCommand => _retrieveTransactionCommand ?? (_retrieveTransactionCommand = new Command(ExecuteRetrieveTransaction));

        #endregion
        #region Checkout

        private Command _checkoutTransactionCommand;

        public Command CheckoutTransactionCommand => _checkoutTransactionCommand ?? (_checkoutTransactionCommand = new Command(ExecuteCheckoutTransaction));

        #endregion
        #region Cancel

        private Command _cancelTransactionCommand;

        public Command CancelTransactionCommand => _cancelTransactionCommand ?? (_cancelTransactionCommand = new Command(ExecuteCancelTransaction));

        #endregion
        #endregion

        #endregion

        #region Command Execution
        #region Items
        #region Add
        private async void ExecuteItemAdd()
        {
            if (IsBusy) return;

            // ⚠⚠ EVERY DOOR, NOT JUST THIS ONE. Matt, 2026-08-13, hand-test A8: *"you can add an item
            // from inventory, add to till. This needs to be stopped as well."* He was right, and the
            // shape of the miss is the one this codebase keeps making: the rule was put on the path
            // that was REPORTED (the scan box) and not on the other path to the same basket
            // (Inventory → Add to till → `ExecuteItemAddArg`, which had only the `IsBusy` guard).
            //
            // ⚠ Same class as the original finding B, one level up. That gate was at COMMIT — right
            // for the ledger, too late for the operator. This one was at the door — but only at one
            // of the doors. A rule enforced per-entry-point is a rule with a hole in it, so it now
            // lives in ONE method that every add path calls.
            if (await RefuseIfDayClosedAsync())
            {
                ItemId = string.Empty;
                return;
            }

            // ⚠ THE SCAN BOX IS THE DOOR A SCANNER ACTUALLY FEEDS. I added this to
            // `ExecuteItemAddArg` first and not here — which is precisely the hole the comment above
            // was written about, made again, two commits later. One method, every door.
            if (await TryRouteMemberScanAsync(ItemId)) return;

            IsBusy = true;
            try
            {
                var lookup = await FindItem(ItemId);

                // ⚠ A cancelled picker leaves the box ALONE and says nothing. The operator is
                // mid-decision; clearing what they typed or telling them the item does not exist
                // both undo work they are still doing.
                if (lookup.Cancelled) return;

                var item = lookup.Item;
                if (item == null)
                {
                    // ⚠ AN UNKNOWN BARCODE IS USUALLY A NEW PRODUCT, NOT A MISTAKE (cutover step
                    // 25). Until now the till said "we can't find an item with that ID" and stopped
                    // — so the only way to sell something newly delivered was to leave the counter,
                    // find another machine, and add it there. That is how shops end up ringing new
                    // stock through as a "miscellaneous" line, which loses the sale from every
                    // stock figure and every category report it should appear in.
                    await OfferToAddUnknownAsync(ItemId);
                    return;
                }

                BasketItem tempItem;

                // ⚠⚠ `Basket.Contains` IS THE FIX, AND ITS ABSENCE COST A SALE. Matt, 2026-08-11:
                // *"I am trying to add “All star batman tp vol1” which I just sold and it wont let me
                // add it to the till."*
                //
                // `SelectedBasketRecord` is bound to the basket list’s selection and is NOT cleared
                // when the basket is emptied after a sale. So it kept pointing at the BasketItem for
                // the line just sold — an object no longer IN `Basket`. Scanning that same item matched
                // it here, incremented the quantity of a DETACHED GHOST, and added nothing to the
                // screen. No error, no line, and the scan box cleared as though it had worked.
                //
                // ⚠ It could only ever affect the item that was just sold, which is exactly what was
                // reported — and exactly what makes it look like the item is broken rather than the till.
                //
                // ⚠ Guarding on membership rather than only nulling the selection: this makes the bug
                // structurally impossible however the selection comes to dangle.
                // ⚠⚠ ONE RULE, ASKED THE SAME WAY BY BOTH BRANCHES (5c item 3b). The selected-line
                // fast path used to check only the item id and "not a return", while the search below
                // also compared the price pair - two paths, two rules. So: ring a 5 pound item, adjust
                // it to 50p, leave the line selected, scan it again, and the second unit joined the
                // adjusted line AT 50p. The web till has never done that (`basket.ts` excludes
                // `l.adjusted`). The rule now lives in `SharedKernel.BasketMerge` (C1) with the
                // TypeScript twin pinned by shared vectors (C2).
                if (SelectedBasketRecord is BasketItem selected &&
                    Basket.Contains(selected) &&
                    Mergeable(selected, item))
                {
                    tempItem = selected;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => Mergeable(bI, item));
                }

                if (tempItem == default)
                    // ⚠ The v2 category rides on the BASKET line, not on the legacy item — see
                    // `BasketItem.CategoryId`. A scheduled discount targets it, and a line without it
                    // is simply never matched by a category rule.
                    Basket.Add(new BasketItem(item, Quantity)
                    {
                        CategoryId = lookup.CategoryId,
                        // ⚠ Only on a NEW line: a unit merged into an existing line keeps whatever
                        // that line recorded, because the line is one row on one receipt and its
                        // snapshot belongs to the scan that created it.
                        ScannedBarcode = lookup.ScannedBarcode,
                    });
                else
                    tempItem.IncrementQuantity(Quantity);
                ItemId = string.Empty;
                Quantity = 1;

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteItemAddArg(string id)
        {
            if (IsBusy) return;

            // ⚠⚠ THE DOOR MATT CAME THROUGH (A8, 2026-08-13). Inventory → "Add to till" arrives here
            // via the `AddToBasket` message, and this path had NO day-closed check — so a Z-closed
            // till refused a scan and accepted the same item from the item list.
            if (await RefuseIfDayClosedAsync()) return;

            if (await TryRouteMemberScanAsync(id)) return;

            IsBusy = true;
            try
            {
                var lookup = await FindItem(id);
                if (lookup.Cancelled) return;

                var item = lookup.Item;
                if (item == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    Logger.LogEvent(AppLogLevel.Warn, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                        new Dictionary<string, string> {{"Success", "False"}, {"Reason", "Item no longer exists"}});
                    return;
                }

                BasketItem tempItem;

                // ⚠⚠ `Basket.Contains` IS THE FIX, AND ITS ABSENCE COST A SALE. Matt, 2026-08-11:
                // *"I am trying to add “All star batman tp vol1” which I just sold and it wont let me
                // add it to the till."*
                //
                // `SelectedBasketRecord` is bound to the basket list’s selection and is NOT cleared
                // when the basket is emptied after a sale. So it kept pointing at the BasketItem for
                // the line just sold — an object no longer IN `Basket`. Scanning that same item matched
                // it here, incremented the quantity of a DETACHED GHOST, and added nothing to the
                // screen. No error, no line, and the scan box cleared as though it had worked.
                //
                // ⚠ It could only ever affect the item that was just sold, which is exactly what was
                // reported — and exactly what makes it look like the item is broken rather than the till.
                //
                // ⚠ Guarding on membership rather than only nulling the selection: this makes the bug
                // structurally impossible however the selection comes to dangle.
                // ⚠⚠ ONE RULE, ASKED THE SAME WAY BY BOTH BRANCHES (5c item 3b). The selected-line
                // fast path used to check only the item id and "not a return", while the search below
                // also compared the price pair - two paths, two rules. So: ring a 5 pound item, adjust
                // it to 50p, leave the line selected, scan it again, and the second unit joined the
                // adjusted line AT 50p. The web till has never done that (`basket.ts` excludes
                // `l.adjusted`). The rule now lives in `SharedKernel.BasketMerge` (C1) with the
                // TypeScript twin pinned by shared vectors (C2).
                if (SelectedBasketRecord is BasketItem selected &&
                    Basket.Contains(selected) &&
                    Mergeable(selected, item))
                {
                    tempItem = selected;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => Mergeable(bI, item));
                }

                if (tempItem == default)
                    // ⚠ The v2 category rides on the BASKET line, not on the legacy item — see
                    // `BasketItem.CategoryId`. A scheduled discount targets it, and a line without it
                    // is simply never matched by a category rule.
                    Basket.Add(new BasketItem(item, Quantity)
                    {
                        CategoryId = lookup.CategoryId,
                        // ⚠ Only on a NEW line: a unit merged into an existing line keeps whatever
                        // that line recorded, because the line is one row on one receipt and its
                        // snapshot belongs to the scan that created it.
                        ScannedBarcode = lookup.ScannedBarcode,
                    });
                else
                    tempItem.IncrementQuantity(Quantity);

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                    new Dictionary<string, string> {{"Success", "True"}});
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Remove
        private void ExecuteRemoveOne(IBasketRecord basketRecord)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                if (basketRecord.Quantity > 1)
                    basketRecord.Quantity--;
                else
                {
                    Basket.Remove(basketRecord);
                    if (SelectedBasketRecord == basketRecord)
                        SelectedBasketRecord = null;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteRemoveAll(IBasketRecord basketRecord)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                Basket.Remove(basketRecord);
                if (SelectedBasketRecord == basketRecord)
                    SelectedBasketRecord = null;
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Adjust
        /// <summary>
        /// Ask a SECOND person to authorise something this operator cannot.
        ///
        /// ⚠ REPLACES `Authorisation.RequestAuthorisedUserInput`, which is not ported and must not
        /// be: it declares `string authEmpId = default`, loops `while (string.IsNullOrEmpty(authEmpId))`
        /// and **never assigns it** — so correct credentials re-prompt indefinitely and the only
        /// exit is Cancel. Its caller then re-checked the ORIGINAL operator anyway, so the
        /// authoriser was collected and thrown away.
        ///
        /// ⚠ The real rule lives in `OperatorLogin.AuthoriseOverrideAsync`: it refuses
        /// self-authorisation, applies the SUPERVISOR's own ceiling, window and staleness tier —
        /// nothing about being an override relaxes any of it — and names both people.
        /// </summary>
        /// <summary>
        /// Σ (price × qty) over SALE lines only, in pence — the room a discount has.
        ///
        /// ⚠ RETURNS ARE EXCLUDED, not subtracted. `SaleIncTax` nets them off, which is right for a
        /// total and wrong here: £10 of goods beside a £30 refund has £10 of discount headroom, not
        /// −£20. `VatLineMath.ForLine` drops a discount on a return by design, so money apportioned
        /// onto one vanishes and the sale then fails the server's reconcile invariant.
        /// </summary>
        /// <remarks>
        /// ⚠ `PricePence` DIRECTLY, not `Pence.FromDecimal(b.Price)` (2026-08-21). `Price` is a
        /// pounds-shaped view *of* `PricePence`, so converting it back was a round trip through a
        /// lossy shape to reach a number that was already there — and it read as though pounds were
        /// the store, which is the impression step 11b's whole reshape exists to remove.
        /// </remarks>
        private long SaleLinesGrossPence() =>
            Basket.OfType<BasketItem>()
                  .Where(b => !b.IsReturn)
                  .Sum(b => b.PricePence * Math.Max(1, b.Quantity));

        /// <summary>Σ of the discounts already on this basket, in pence, as a POSITIVE number.
        /// ⚠ Same shape as `CheckoutCommit.ApplyAlterations` reads them (magnitude × quantity), so
        /// the gate and the commit path agree about what is already off.</summary>
        private long DiscountAlreadyPence() =>
            Basket.OfType<BasketAlteration>()
                  .Sum(a => Pence.FromDecimal(Math.Abs(a.Price)) * Math.Max(1, a.Quantity));

        /// <summary>
        /// Turn a <see cref="Plutus.SharedKernel.DiscountDecision"/> into words for the counter.
        ///
        /// ⚠ THE SENTENCE IS BUILT HERE, NOT IN THE RULE, and that is deliberate: `RefundDecision`
        /// already settled that money formatting is a client concern — "£" is wrong the first time a
        /// tenant trades in another currency, and a baked sentence cannot go through `I18N_L10N`.
        /// The rule hands over a verdict and the amounts; this composes them.
        ///
        /// ⚠ `AlreadyPence` is named whenever it is non-zero. Told only "the most you can take off
        /// is £3.00" on an £8 basket, an operator reasonably concludes the till is wrong.
        /// </summary>
        private static string DiscountRefusalMessage(Plutus.SharedKernel.DiscountDecision d)
        {
            string Gbp(long pence) => (pence / 100m).ToString("C2", CultureInfo.CurrentCulture);

            switch (d.Verdict)
            {
                case Plutus.SharedKernel.DiscountVerdict.NothingToDiscount:
                    return "There's nothing in the basket to discount. A returned item can't be discounted — a refund gives back what the customer actually paid.";

                case Plutus.SharedKernel.DiscountVerdict.NotAnAmount:
                    return "A discount has to be more than nothing.";

                default:
                    return d.AlreadyPence > 0
                        ? string.Format(
                            "That's more than is left to discount. {0} is already off, so the most you can take off now is {1}.",
                            Gbp(d.AlreadyPence), Gbp(d.HeadroomPence))
                        : string.Format(
                            "A discount can't be more than the basket. The most you can take off is {0}.",
                            Gbp(d.HeadroomPence));
            }
        }

        /// <summary>
        /// Ask why this discount is being given — binding default 22(c).
        ///
        /// ⚠ Returns null when the operator cancels OR types nothing usable, and the caller abandons
        /// the discount either way. There is no "skip" and that is the ruling: an optional reason is
        /// an empty column, and the one discount anybody ever asks about is the one where nobody
        /// typed anything.
        ///
        /// ⚠ THROUGH `InputAlertHelper`, which gates on `Modal`. This dialog sits between two others
        /// (the amount prompt before it, possibly the supervisor prompt after), and stacking dialogs
        /// outside that gate is what threw the COMException that closed the till at the payment
        /// prompt — runbook pitfalls 11–14.
        ///
        /// ⚠ `RequiredValidator` refuses an empty box up front, so the operator is told by the field
        /// rather than by an alert after the fact; `DiscountAudit.NormaliseReason` is still the
        /// decider, because a validator cannot see that "   " is blank.
        /// </summary>
        private static async Task<string> AskForDiscountReasonAsync()
        {
            IValidator[] validators = { new RequiredValidator() };

            ViewElementData[] elements =
            {
                new ViewElementData(1, "Reason".Translate(), "", validators.AsEnumerable(), false, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                elements, "Confirm".Translate(), false, "WhyThisDiscount".Translate(), "Cancel".Translate());

            answers.TryGetValue(1, out var typed);

            return Plutus.SharedKernel.DiscountAudit.NormaliseReason(typed);
        }

        /// <summary>
        /// Why the till would not record this discount, in words for the counter.
        ///
        /// ⚠ Same split as <see cref="DiscountRefusalMessage"/>: the rule returns a verdict, the
        /// client composes the sentence, so nothing in `SharedKernel` carries a translatable string.
        ///
        /// ⚠ Each sentence names the operator's NEXT ACTION. "Self-authorised" in particular has to
        /// say *fetch someone else* — an operator told only that it was refused will simply try the
        /// same credentials again.
        /// </summary>
        private static string DiscountAuditRefusalMessage(Plutus.SharedKernel.DiscountAuditVerdict verdict) =>
            verdict switch
            {
                Plutus.SharedKernel.DiscountAuditVerdict.NoReason =>
                    "Every discount has to say why it was given. Try again and type a short reason.",

                Plutus.SharedKernel.DiscountAuditVerdict.NoAuthoriser =>
                    "This discount is above your limit, so a supervisor has to authorise it. Nothing has been taken off.",

                Plutus.SharedKernel.DiscountAuditVerdict.SelfAuthorised =>
                    "A discount can't be authorised by the person giving it. Ask someone else to sign in on the prompt.",

                _ => "This discount can't be recorded.",
            };

        /// <summary>
        /// Who authorised a step-up, for the record that outlives this till.
        ///
        /// ⚠⚠ THIS TYPE EXISTS BECAUSE THE ANSWER USED TO BE THROWN AWAY. The override returned
        /// `bool`: it verified a supervisor, wrote their name to the till's LOCAL LOG, and dropped
        /// it. So *"who approved this discount?"* could only be answered by walking to that till and
        /// reading a file — and a re-imaged till has none. Binding default 22(c) needs it on the
        /// SALE, which means it has to survive the return.
        /// </summary>
        private readonly record struct SupervisorGrant(Guid AuthorisedByUserId, string AuthorisedByName);


        /// <summary>
        /// May a newly-added unit of <paramref name="item"/> join <paramref name="line"/>?
        ///
        /// ⚠ THE RULE ITSELF IS IN `SharedKernel.BasketMerge` (C1) so the web till's copy can be
        /// pinned against it (C2). This method is only the translation: it maps MAUI's line onto the
        /// rule's questions and nothing more. ⚠ Keep it that way - a condition added HERE is a
        /// condition the other till does not have, which is how the two got out of step to begin with.
        /// </summary>
        private static bool Mergeable(BasketItem line, Models.TillItem item) =>
            Plutus.SharedKernel.BasketMerge.CanMerge(
                sameItem: line.Item.Id.Equals(item.Id),
                lineIsReturn: line.IsReturn,
                lineAdjusted: line.Adjusted,
                // ⚠ FALSE, and not an oversight. MAUI carries a discount as its OWN basket record
                // (`MemberDiscountBasket.Build` adds a line) rather than as a field on the sale line,
                // so there is no per-line discount to test here. The web till passes its `l.discount`.
                // ⚠⚠ If a per-line discount is ever added to `BasketItem`, this argument is the
                // line that must change with it.
                lineHasDiscount: false,
                lineIncPence: line.PricePence,
                lineExPence: line.PriceExTaxPence,
                catalogueIncPence: Plutus.SharedKernel.Pence.FromDecimal(item.Price),
                catalogueExPence: Plutus.SharedKernel.Pence.FromDecimal(item.ExPrice));
        /// <summary>
        /// Ask a supervisor to authorise this. ⚠ Null means it did NOT happen — cancelled, refused,
        /// nobody signed in, or an error. Every caller must treat null as a refusal, which is why
        /// this returns a grant rather than a bool with the identity logged out of band.
        /// </summary>
        private async Task<SupervisorGrant?> RequestSupervisorOverrideAsync(string permission, long? amountPence)
        {
            try
            {
                var requestedBy = App.GetViewModel().SignedInOperator;
                if (requestedBy is null) return null;   // nobody to attribute the request to

                var credentials = await Helpers.Security.SupervisorPrompt.AskAsync();
                if (credentials is null) return null;   // cancelled — the basket is untouched

                var login = new Plutus.Client.Core.OperatorLogin(
                    new Services.Connectivity.DbOperatorStore());

                var result = await login.AuthoriseOverrideAsync(
                    requestedBy, credentials.Value.EmailOrId, credentials.Value.Password,
                    permission, amountPence);

                if (!result.Succeeded)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), result.Message, "OK".Translate());
                    return null;
                }

                // ⚠ THE LOCAL LOG STAYS. It is not the audit record any more — the sale is — but it
                // is the only trace of an override that authorised something which then FAILED to
                // commit, and that is precisely the sequence somebody investigates.
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Supervisor override",
                    new Dictionary<string, string>
                    {
                        { "Permission", permission },
                        { "RequestedBy", requestedBy.UserId.ToString() },
                        { "AuthorisedBy", result.Granted!.AuthorisedByUserId.ToString() },
                        { "AuthorisedByName", result.Granted.AuthorisedByName },
                        { "AmountPence", amountPence?.ToString() ?? "n/a" },
                    });

                return new SupervisorGrant(result.Granted.AuthorisedByUserId, result.Granted.AuthorisedByName);
            }
            catch (Exception ex)
            {
                // ⚠ An override that errors is an override that did NOT happen.
                CrashLog.Write("TillViewModel.RequestSupervisorOverrideAsync", ex);
                return null;
            }
        }

        private async void ExecuteAdjustItem(BasketItem basketItem)
        {
            if (IsBusy) return;

            // ⚠ THIS HAD NO PERMISSION CHECK AT ALL. Anyone who could reach the till could retype
            // any line's price to anything, with nothing recorded about who did it.
            var priceGate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PosPriceOverride);

            if (!priceGate.Allowed)
            {
                if (!priceGate.NeedsOverride ||
                    await RequestSupervisorOverrideAsync(PermissionCatalogue.PosPriceOverride, null) is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), priceGate.Message, "OK".Translate());
                    return;
                }
            }

            IsBusy = true;
            try
            {
                const NumberStyles numberStyles = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
                IValidator[] validators = {
                    new RequiredValidator(),
                    new CurrencyValueValidator(numberStyles)
                };

                // ⚠⚠ ONE FIELD, THE INC-VAT PRICE — AND THIS IS A VAT FIX, NOT A LAYOUT ONE.
                //
                // It asked for the ex-VAT price AND the inc-VAT price and wrote BOTH onto the line.
                // The sale line's declared VAT rate is derived from that pair (till-design C1 rule 2),
                // so two numbers typed by hand WERE the VAT figure on the sale: £10.00 ex against
                // £10.50 inc declared 5% on a 20% item, with nothing to validate it and nothing
                // downstream able to tell. Found 2026-08-18 while comparing this screen with the web
                // till, which has always asked for one number.
                //
                // ⚠ The ex half is DERIVED by `SharedKernel.PriceAdjust.ExFromInc` from the
                // CATALOGUE pair's proportion — the same rule, and now the same code path, as the web
                // till's `adjust` reducer. A zero-rated item stays zero-rated by construction.
                //
                // ⚠ The prompt says what it wants. "Price" alone left the operator guessing which of
                // the two boxes was which, which is half of why the old shape was dangerous.
                ViewElementData[] elements = {
                    new ViewElementData(1, "Price (inc VAT)", basketItem.Price.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true),
                };

                var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Adjust".Translate());

                // ⚠⚠ BACKING OUT LEAVES AN **EMPTY** RESULT, NOT A NULL — `InputAlertHelper.ShowAsync`
                // ends `?? new Dictionary<…>()`, and since 2026-08-18 Cancel and the ✕ clear the
                // results too, so all four exits agree. `TryGetValue` then returns false and this
                // returns without touching the price. ⚠ The old code read the blanked values, found
                // them non-null and ran `decimal.Parse("")` — that is the crash Matt hit with the ✕.
                if (!data.TryGetValue(1, out string typedPrice) || string.IsNullOrWhiteSpace(typedPrice))
                    return;

                var newIncPence = Plutus.SharedKernel.Pence.FromDecimal(
                    decimal.Parse(typedPrice, numberStyles, CultureInfo.CurrentCulture));

                // ⚠ A negative price is money OUT of the drawer dressed as a sale line. Returns are
                // how goods go back, and they carry a reason and a refund cap; this must not become a
                // second, unaudited way to do it.
                if (newIncPence < 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "A price cannot be negative. Use Refund to send goods back.", "OK".Translate());
                    return;
                }

                // ⚠ The CATALOGUE pair, never the line's current pair — so adjusting the same line
                // twice derives from the same proportion both times and cannot drift a penny per edit.
                basketItem.PriceExTaxPence = Plutus.SharedKernel.PriceAdjust.ExFromInc(
                    newIncPence,
                    Plutus.SharedKernel.Pence.FromDecimal(basketItem.Item.Price),
                    Plutus.SharedKernel.Pence.FromDecimal(basketItem.Item.ExPrice));
                basketItem.PricePence = newIncPence;

                // ⚠ MARK THE LINE. The web till puts a `*` beside an adjusted price and gives the row
                // its own class; MAUI showed the new number and nothing else, so a £5 item retyped to
                // 50p looked exactly like an item that costs 50p. Finding W's lesson on a different
                // control: what an operator cannot see, they do again.
                basketItem.Adjusted = true;

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Adjustment");
            }
            catch (Exception ex)
            {
                // ⚠⚠ THIS METHOD IS `async void`: an escaping exception goes to the dispatcher
                // UNHANDLED and takes the till with it. It had `try`/`finally` and no `catch`, and on
                // 2026-08-18 a `FormatException` from `decimal.Parse("")` did exactly that when the
                // operator backed out of the dialog. The root cause is fixed in
                // `InputAlert.CancelBut_Clicked` (all exits now yield an empty result), and this is the
                // backstop: a price that cannot be parsed must cost the operator a message, never the
                // till.
                Services.Analytics.CrashLog.Write("TillViewModel.ExecuteAdjustItem", ex);
                try
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That price couldn't be applied, so the line is unchanged.", "OK".Translate());
                }
                catch (Exception inner) { Services.Analytics.CrashLog.Write("ExecuteAdjustItem.alert", inner); }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Return
        private Command _returnSelectedCommand;

        /// <summary>
        /// The visible door onto a refund — finding Z1, 2026-08-13.
        ///
        /// ⚠⚠ THE ONLY WAY IN WAS A RIGHT-CLICK ON A BASKET LINE, and nothing on screen said so. Matt:
        /// *"The webtill allows you look up returns and sales via a button next to the barcode entry
        /// bar. Is this set to be replicated within MAUI?"* **A feature reachable only by right-click on
        /// a touch till is a feature that does not exist** — and this one hands money back.
        ///
        /// ⚠ IT IS NOT THE WEB TILL'S FLOW, and the difference is worth knowing rather than papering
        /// over. The web till opens a dialog, finds the SALE, and adds the returned line from it. MAUI
        /// works the other way round: put the item in the basket, then mark that line as going back. So
        /// this button drives the flow MAUI actually has, and says what to do when nothing is selected
        /// rather than opening a dialog that cannot work yet.
        ///
        /// ⚠ **Step 26 is where the shapes converge** — its cross-till sale lookup is the screen that
        /// lets MAUI start from the sale like the web till does. This is the ½-day version that stops
        /// the feature being invisible in the meantime.
        /// </summary>
        public Command ReturnSelectedCommand => _returnSelectedCommand ??= new Command(async () =>
        {
            // ⚠⚠ THE WHOLE BODY IS WRAPPED, AND IT HAS TO BE. `new Command(async () => …)` is an
            // `async void` in all but spelling: nothing awaits it, so ANY exception escaping here is
            // posted to the dispatcher unhandled and **takes the till down** — not a failed action, a
            // dead counter mid-sale. Matt, 2026-08-18: *"if I click the refund button, the till
            // crashes."*
            try
            {
                if (IsBusy) return;

                // ⚠⚠ NOTHING SELECTED IS THE NORMAL CASE, AND IT WAS THE CRASH. `SelectedBasketRecord`
                // is null until an operator taps a line, so `SelectedBasketRecord.IsReturn` below
                // threw a NullReferenceException the instant the button was pressed — which is what
                // anybody does first, because pressing a button is how you find out what it does.
                //
                // ⚠ The bitter part: the guidance for exactly this case already existed at the BOTTOM
                // of this method ("Which item is coming back?"), and the null dereference sat above
                // it. The right message was written, and unreachable.
                if (SelectedBasketRecord is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Which item is coming back?",
                        "Scan or search for the item the customer is returning so it is in the basket, "
                        + "tap its line to select it, then press this again.\n\nYou will be asked which "
                        + "sale it came from — pick it from this till's recent sales, or type the number "
                        + "on the receipt.",
                        "OK".Translate());
                    return;
                }

                // Already a return: RevertReturn is that line's job, and silently doing nothing here is
                // how an operator concludes the button is broken.
                if (SelectedBasketRecord.IsReturn)
                {
                    await Application.Current.MainPage.DisplayAlert("Already going back",
                        "That line is already a return. Use the line's own menu to undo it.", "OK".Translate());
                    return;
                }

                if (SelectedBasketRecord is BasketItem item)
                {
                    ExecuteReturn(item);
                    return;
                }

                // Selected, but not an item — a note or a return-placeholder row. Same guidance.
                await Application.Current.MainPage.DisplayAlert("Which item is coming back?",
                    "Tap the line for the ITEM the customer is returning, then press this again.",
                    "OK".Translate());
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillViewModel.ReturnSelectedCommand", ex);
                // ⚠ Say something. A button that visibly does nothing gets pressed again, and the
                // operator's next move is to restart the till mid-basket.
                try
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That couldn't be started. The basket is unchanged.", "OK".Translate());
                }
                catch (Exception inner) { Services.Analytics.CrashLog.Write("ReturnSelectedCommand.alert", inner); }
            }
        });

        private async void ExecuteReturn(BasketItem basketItem)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                #region Setup Input Alert
                IValidator[] stringValidators = {
                    new RequiredValidator()
                };
                #endregion

                // ⚠⚠ A COPY OF THE LINE, NOT A CAST TO A SUBCLASS (step 11b, 2026-08-22). This
                // was `basketItem.Adapt<BasketReturnItem>()` — a Mapster hop across an inheritance
                // chain that existed only to change the runtime type. The subclass is gone; the line
                // is flagged instead, by `MarkAsReturn` below once the reason and origin are known.
                //
                // ⚠ STILL A COPY, and deliberately: the basket may already hold the SALE line for
                // this item, and flagging that one in place would turn a sale the customer is buying
                // into a refund under their hands.
                var returnItem = basketItem.Adapt<BasketItem>();

                // ⚠ PICK THE SALE, DON'T TYPE ITS UUID. Until 2026-08-10 this dialog's first field
                // was "Sale id" and nothing in the app could produce one — no list, no search. The
                // only source was the barcode on a PRINTED RECEIPT, so a till with no printer could
                // not refund anything at all, and the refund rule (complete since steps 15–17) had
                // no door. Reported by Matt as *"In MAUI I cannot do a refund?"*.
                //
                // ⚠ Reads this till's OWN sales, so it works with the line down — which is when a
                // shop most needs to hand money back. Typing an id stays available for goods bought
                // on ANOTHER till, where only the server knows the sale.
                var recent = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.ListRecentSalesAsync(20, purchasesOnly: true));

                const string anotherTill = "Sold on another till — look it up in Plutus…";
                const string typeItInstead = "Enter a sale ID…";
                string saleIdFromPicker = null;

                if (recent is { Count: > 0 })
                {
                    var labels = recent
                        .Select(r => $"{r.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} · "
                                   + $"{r.GrossPence / 100m:C} · "
                                   + $"{r.LineCount} item{(r.LineCount == 1 ? "" : "s")}"
                                   + (string.IsNullOrWhiteSpace(r.FirstItemIdOne) ? "" : $" · {r.FirstItemIdOne}"))
                        .ToList();

                    // ⚠ THIS TILL'S SALES FIRST, ALWAYS. They are the common case and the only ones
                    // available with the line down. The platform lookup is the second step, not the
                    // default, so a refund never depends on the network unless it has to.
                    labels.Add(anotherTill);
                    labels.Add(typeItInstead);

                    var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                        Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                            "Which sale is this going back to?", "Cancel".Translate(), null, labels.ToArray()));

                    if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate())
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Return",
                            new Dictionary<string, string> { { "Canceled", "True" }, { "At", "SalePicker" } });
                        return;
                    }

                    if (picked == anotherTill)
                    {
                        saleIdFromPicker = await PickPlatformSaleAsync();
                        if (saleIdFromPicker is null) return;   // backed out, or nothing to show
                    }
                    else
                    {
                        var index = labels.IndexOf(picked);
                        if (index >= 0 && index < recent.Count)
                            saleIdFromPicker = recent[index].SaleId.ToString("D");
                    }
                }

                // ⚠ Only ask for what is still unknown. Having just chosen the sale from a list,
                // being asked to type its id as well is the kind of step that gets worked around.
                var elements = saleIdFromPicker is null
                    ? new[]
                    {
                        new ViewElementData(1, string.Format("IdArg".Translate(), "Sale".Translate()), "", stringValidators, false, true),
                        new ViewElementData(2, "Reason".Translate(), "ReturnReasonExample".Translate(), stringValidators, false, true)
                    }
                    : new[]
                    {
                        new ViewElementData(2, "Reason".Translate(), "ReturnReasonExample".Translate(), stringValidators, false, true)
                    };

                {
                    var alertReturnValues = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Returns".Translate(), "Cancel".Translate());

                    // ⚠ AN EMPTY DICTIONARY IS "THEY BACKED OUT" — checked BEFORE anything is
                    // injected into it. Since 2026-08-10 the dialog can be dismissed with Escape as
                    // well as Cancel, and that path returns nothing at all rather than blanked
                    // entries. Injecting the picked sale id first would have made an ordinary
                    // Escape look like a missing-key fault and shown "a critical error has been
                    // reported" for pressing Esc.
                    if (alertReturnValues.Count == 0)
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Return",
                            new Dictionary<string, string> { { "Canceled", "True" }, { "At", "Reason" } });
                        return;
                    }

                    // ⚠ The sale id comes from the picker when there was one — the dialog only ever
                    // carries the fields it actually asked for.
                    if (saleIdFromPicker is not null)
                        alertReturnValues[1] = saleIdFromPicker;

                    // ⚠ BOTH answers are required, and the old test said the opposite. It was
                    // `!TryGetValue(1, …) & !TryGetValue(2, …)` — a non-short-circuit AND, so it
                    // only complained when BOTH keys were missing. One missing key sailed through
                    // and the code carried on with a null sale id. Read both (the `out` values are
                    // needed either way), then refuse if EITHER is absent.
                    var haveSaleId = alertReturnValues.TryGetValue(1, out string saleIdText);
                    var haveReason = alertReturnValues.TryGetValue(2, out string reasonText);

                    if (!haveSaleId || !haveReason)
                    {
                        Logger.LogError(new ArgumentException(
                                $"{this.GetType().Name}: {nameof(alertReturnValues)} does not have the expected key required, to move forward!"),
                            new Dictionary<string, string>
                                {{"alertReturnValues", string.Join(Environment.NewLine, alertReturnValues)}});
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                            "CriticalErrorReportedMesg".Translate(),
                            "OK".Translate());
                        return;
                    }

                    // ⚠ CANCEL MUST ESCAPE, and it did not. `InputAlert`'s Cancel button blanks the
                    // entries and then fires the CONFIRM handler, so the dictionary comes back with
                    // its keys present and their values null — `TryGetValue` returns true, the
                    // error branch above is skipped, and this branch re-opened the dialog. The
                    // operator was trapped in a modal with no way out but killing the app, losing
                    // the basket with it. Blank input IS the cancel signal here; there is no other.
                    if (string.IsNullOrEmpty(saleIdText) || string.IsNullOrEmpty(reasonText))
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Return", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    // ⚠ THE WHOLE LEGACY LOOKUP IS GONE (cutover step 16). It queried the local
                    // legacy `Trans` table — empty on a portal-provisioned till, and permanently
                    // so, because sales are committed to the new store — and then called
                    // `trans.First()`, which throws from this `async void` with no catch the
                    // moment the item is not on that sale. Scanning the wrong receipt, an ordinary
                    // counter mistake, closed the application. Its "refunds left" test counted
                    // QUANTITIES on this machine only, so goods bought on another till could be
                    // refunded here in full, twice.
                    //
                    // `ReturnLookup` prefers the SERVER (only it knows what other tills have given
                    // back) and falls back to this till's own record only INSIDE the rolling
                    // window. `RefundRules.Authorise` — the shared rule — decides.
                    var resolution = await Services.Sales.ReturnLookup.ResolveAsync(
                        saleIdText, basketItem.Item?.Id, basketItem.Quantity);

                    if (!resolution.IsAllowed)
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Hmm".Translate(), resolution.Decision.Reason, "OK".Translate());
                        return;
                    }

                    // ⚠ SAY SO WHEN IT WAS CAPPED. `RefundDecision` is explicit that handing over
                    // less than was asked for without saying so is how a refund becomes an argument
                    // at the counter — the customer expects the figure they asked for.
                    if (resolution.Decision.WasCapped &&
                        !await Application.Current.MainPage.DisplayAlert(
                            "Hmm".Translate(),
                            $"Only {resolution.Decision.AllowedPence / 100m:C2} of this sale is left to refund "
                            + $"({resolution.Decision.AlreadyRefundedPence / 100m:C2} has already been given back). "
                            + "Refund that instead?",
                            "Yes".Translate(), "Cancel".Translate()))
                        return;

                    // ⚠ THE PRICE THE CUSTOMER ACTUALLY PAID, from the original sale — not today's
                    // catalogue price. A price that moved since would refund the wrong amount, and
                    // the direction it goes wrong is whichever way the shop loses.
                    returnItem.Price = resolution.UnitIncPence / 100m;
                    returnItem.PriceExTax = resolution.UnitExPence / 100m;
                    returnItem.MarkAsReturn(reasonText, saleIdText);
                }
                //finalize change
                Basket.Remove(basketItem);

                // ⚠ Same rule: the line this pointed at has gone.

                if (ReferenceEquals(SelectedBasketRecord, basketItem)) SelectedBasketRecord = null;
                Basket.Add(returnItem);
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Return");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteRevertReturn(BasketItem basketReturnItem)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var basketItem = basketReturnItem.Adapt<BasketItem>();

                //finalize change
                Basket.Remove(basketReturnItem);
                Basket.Add(basketItem);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #endregion
        private void ExecuteAutoScan()
        {
            throw new NotImplementedException();
        }

        #region Customer (step 27 — WP12 attach)

        private Plutus.Client.Core.PlutusApiClient.CustomerDetailDto _attachedCustomer;
        private bool _refreshingMemberDiscount;

        /// <summary>
        /// The member attached to this sale, or null. ⚠ Held as the **live detail read**, never the
        /// search summary: the summary carries no tier and no balance, and the tier is what decides
        /// the money.
        /// </summary>
        public Plutus.Client.Core.PlutusApiClient.CustomerDetailDto AttachedCustomer
        {
            get => _attachedCustomer;
            private set
            {
                _attachedCustomer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AttachedCustomerLabel));
                OnPropertyChanged(nameof(HasAttachedCustomer));
                OnPropertyChanged(nameof(HasNoAttachedCustomer));
            }
        }

        public bool HasAttachedCustomer => _attachedCustomer != null;

        /// <summary>
        /// Nobody attached — so the lookup button shows and the attached-customer row does not.
        ///
        /// ⚠ An explicit inverse rather than a converter: MAUI ships no negating converter, and a
        /// hand-rolled one is a second place for this to go wrong silently. ⚠ It must be raised in the
        /// setter above with the others — a `DynamicResource`-style silent failure is bad enough, but a
        /// binding to a property that never notifies leaves a button that is right once and then wrong
        /// for the rest of the shift.
        /// </summary>
        public bool HasNoAttachedCustomer => _attachedCustomer == null;

        /// <summary>
        /// How much store credit the attached customer has, in pence — 0 when nobody is attached.
        ///
        /// ⚠ THE FIGURE FROM THE LAST LIVE READ, not a cached or derived one. It is refreshed on
        /// every attach; the SERVER remains the authority and refuses an overdraw at redeem time, so
        /// this decides only whether the button is offered and what it is capped at.
        /// </summary>
        public long CreditAvailablePence => _attachedCustomer?.CreditBalancePence ?? 0;

        private Plutus.Client.Core.PlutusApiClient.GiftCardLookupDto _presentedGiftCard;

        /// <summary>
        /// A gift card the customer has handed over, looked up and held for checkout.
        ///
        /// ⚠ HELD IN PAGE STATE, NOT IN THE BASKET — the same shape finding Y used for the origin
        /// sale's tenders. It is not a basket line: nothing about it is being sold.
        ///
        /// ⚠ The balance here is the figure at LOOKUP. The server re-checks at redeem and refuses an
        /// overdraw, which is what actually protects the money; this decides whether to offer the
        /// button and what to cap the box at.
        /// </summary>
        public Plutus.Client.Core.PlutusApiClient.GiftCardLookupDto PresentedGiftCard
        {
            get => _presentedGiftCard;
            private set
            {
                _presentedGiftCard = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPresentedGiftCard));
                OnPropertyChanged(nameof(PresentedGiftCardLabel));
            }
        }

        public bool HasPresentedGiftCard => _presentedGiftCard != null;

        public string PresentedGiftCardLabel => _presentedGiftCard is null
            ? string.Empty
            : $"{_presentedGiftCard.Pretty ?? _presentedGiftCard.Code} — {(_presentedGiftCard.BalancePence / 100m):C2}";

        public long GiftCardAvailablePence =>
            _presentedGiftCard is { IsSpendable: true } card ? card.BalancePence : 0;

        /// <summary>
        /// What the operator sees at the top of the sale — the member's name and, when it grants
        /// one, the tier and rate.
        ///
        /// ⚠ AN EXPIRED MEMBERSHIP SAYS SO, out loud. Showing "Jo Bloggs — Gold 10%" while charging
        /// full price is the worst of both: the operator tells the customer they got their discount
        /// and the receipt disagrees, at the counter, with a queue.
        /// </summary>
        public string AttachedCustomerLabel
        {
            get
            {
                if (_attachedCustomer is null) return string.Empty;

                var name = string.IsNullOrWhiteSpace(_attachedCustomer.Name)
                    ? _attachedCustomer.MemberNo ?? "Member"
                    : _attachedCustomer.Name;

                var m = _attachedCustomer.Membership;
                if (m is null) return name;

                if (m.Expired)
                    return $"{name} — {m.Tier} (expired, no discount)";

                return SharedKernel.MemberDiscount.Applies(true, false, m.AutoDiscountRate)
                    ? $"{name} — {SharedKernel.MemberDiscount.Label(m.Tier, m.AutoDiscountRate)}"
                    : name;
            }
        }

        private Command _attachCustomerCommand;
        public Command AttachCustomerCommand => _attachCustomerCommand ??=
            new Command(ExecuteAttachCustomer);

        private Command _detachCustomerCommand;
        public Command DetachCustomerCommand => _detachCustomerCommand ??=
            new Command(ExecuteDetachCustomer);

        /// <summary>
        /// Find a member and attach them.
        ///
        /// ⚠ An action sheet and an input alert, like every other picker in this app since the
        /// Syncfusion removal — and through `Modal`, because this leads into a second dialog and two
        /// dialogs in quick succession is what closed the till at the payment prompt (pitfalls 11–14).
        /// </summary>
        private async void ExecuteAttachCustomer()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                IValidator[] validators = { new RequiredValidator() };
                ViewElementData[] elements =
                {
                    new ViewElementData(1, "Name, phone, email or member number", "",
                        validators.AsEnumerable(), false, true),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Search".Translate(), false, "Find a member", "Cancel".Translate());

                answers.TryGetValue(1, out var term);
                if (string.IsNullOrWhiteSpace(term)) return;

                await SearchAndAttachAsync(term);
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — without this the till closes.
                CrashLog.Write("TillViewModel.ExecuteAttachCustomer", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "The member couldn't be looked up. The sale is unaffected.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Search, let the operator pick, then attach — shared by the button and the card scan.
        ///
        /// ⚠ ONLINE ONLY, and it says so rather than failing vaguely. The tier decides money and a
        /// till holds no customer cache; guessing from a stale local copy is how a member gets a
        /// discount they are no longer entitled to.
        /// </summary>
        private async Task SearchAndAttachAsync(string term)
        {
            var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Members can only be looked up when the till is online. The sale is unaffected.",
                    "OK".Translate());
                return;
            }

            var matches = await api.SearchCustomersAsync(term);

            if (matches is null || matches.Count == 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    string.Format("No member found for '{0}'.", term), "OK".Translate());
                return;
            }

            var chosen = matches[0];

            // ⚠ Only ask when there is genuinely a choice. A scanned card resolves to one member and
            // an action sheet with a single row is a keypress that teaches operators to tap blind.
            if (matches.Count > 1)
            {
                var names = matches
                    .Select(m => string.IsNullOrWhiteSpace(m.MemberNo)
                        ? m.Name ?? "(no name)"
                        : $"{m.Name} · {m.MemberNo}")
                    .ToArray();

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                        "Which member?", "Cancel".Translate(), null, names));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                var index = Array.IndexOf(names, picked);
                if (index < 0) return;
                chosen = matches[index];
            }

            // ⚠ THE LIVE DETAIL READ, every time — the search carries no tier and no balance.
            var detail = await api.GetCustomerAsync(chosen.Id);
            if (detail is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That member couldn't be read. The sale is unaffected.", "OK".Translate());
                return;
            }

            AttachedCustomer = detail;
            RefreshAutoDiscounts();

            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member attached",
                new Dictionary<string, string>
                {
                    { "CustomerId", detail.Id.ToString() },
                    { "Tier", detail.Membership?.Tier ?? "none" },
                    { "Expired", (detail.Membership?.Expired ?? false).ToString() },
                });

            // ⚠ Say what changed. An operator who cannot see that a discount was applied will apply
            // one by hand as well — finding W's lesson, on a different screen.
            //
            // ⚠ SUMMED OVER EVERY AUTOMATIC ALTERATION, because there can now be several: the tier
            // discount on some lines and a scheduled rule on others, whichever was worth more per
            // line. Reporting only the first would understate what came off, which is worse than
            // saying nothing — an operator would top it up by hand.
            var automatic = Services.Storage.AutoDiscountBasket.ExistingOn(Basket);
            if (automatic.Count > 0)
            {
                var off = automatic.Sum(a => Math.Abs(a.Price));
                await Application.Current.MainPage.DisplayAlert(
                    "Member".Translate(),
                    string.Format("{0} — {1} off this basket.",
                        AttachedCustomerLabel, off.ToString("C2", CultureInfo.CurrentCulture)),
                    "OK".Translate());
            }
        }

        /// <summary>
        /// Take the member off this sale.
        ///
        /// ⚠ Removes AUTOMATIC discounts only — an operator's own manual discounts stay, because they
        /// were a separate decision. `AutoDiscountBasket.ExistingOn` finds them by the `Automatic`
        /// flag for exactly this reason.
        ///
        /// ⚠ And a rebuild rather than a removal: a scheduled rule that was being out-bid by the
        /// member's tier now becomes the best offer on those lines, so the customer still gets the
        /// shop's advertised discount after their card comes off the sale.
        /// </summary>
        private void ExecuteDetachCustomer()
        {
            AttachedCustomer = null;
            RefreshAutoDiscounts();
        }

        /// <summary>
        /// Was that scan a membership card? If so, attach the member and report handled.
        ///
        /// ⚠⚠ ONE METHOD, EVERY DOOR — the lesson `RefuseIfDayClosedAsync` was extracted for, and
        /// which I re-learned here: this went onto `ExecuteItemAddArg` (Inventory → Add to till)
        /// before the scan box, which is the door a scanner actually feeds. **A rule enforced
        /// per-entry-point is a rule with a hole in it.**
        ///
        /// ⚠ A MEMBER CARD IS NOT A PRODUCT. Scanners are keyboard-wedge into the same box, so a
        /// membership barcode arrives exactly like an EAN. `LooksLikeMemberScan` is deliberately
        /// STRICTER than `TryCanonicalise`: it requires the "C" prefix AND a valid check character,
        /// so a bare six-digit product code can never be mistaken for a member.
        ///
        /// ⚠ Its own `IsBusy` bracket, because it is called BEFORE the callers take theirs. Sharing
        /// one would leave a dialog flow holding a gate it then waits on — finding U exactly.
        /// </summary>
        private async Task<bool> TryRouteMemberScanAsync(string scanned)
        {
            var isMember = SharedKernel.MemberNumbers.LooksLikeMemberScan(scanned);
            var isGiftCard = !isMember && SharedKernel.GiftCardCodes.LooksLikeCard(scanned);

            if (!isMember && !isGiftCard) return false;

            if (IsBusy) return true;   // handled: it is a card, and we are mid-flow
            IsBusy = true;

            try
            {
                if (isMember) await SearchAndAttachAsync(scanned);
                else await TryPresentGiftCardAsync(scanned);
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.TryRouteMemberScanAsync", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That card couldn't be looked up. The sale is unaffected.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
                // ⚠ The box is cleared whatever happened — a card left in it would be re-scanned as
                // an item by the operator's next Enter.
                ItemId = string.Empty;
            }

            return true;
        }

        /// <summary>
        /// Spend the store credit this sale is settling with, if any. False means **abort the sale**.
        ///
        /// ⚠ Called BEFORE the commit, deliberately — see the call site. Everything it can refuse is
        /// something the server would have refused anyway; refusing here is what keeps the sale
        /// abandonable.
        ///
        /// ⚠ THE ENTRY ID IS MINTED ONCE, HERE. It is what makes the redeem idempotent, so a retry
        /// of the same attempt draws the balance down once. ⚠ Any retry the operator makes is a NEW
        /// attempt with a new sale, so a fresh id is correct there — what must never happen is one
        /// attempt generating two.
        /// </summary>
        private async Task<bool> TryRedeemStoreCreditAsync(
            IReadOnlyList<Plutus.Contracts.Client.IngestTender> tenders)
        {
            var creditPence = tenders
                .Where(t => t.TenderType == SharedKernel.Tenders.Credit)
                .Sum(t => t.AmountPence);

            if (creditPence <= 0) return true;   // nothing to spend — the ordinary case

            // ⚠ Defence in depth: the button is only offered with a customer attached, but the
            // basket and the attachment are separate state and a detach mid-checkout is possible.
            // Committing a sale carrying credit nobody owns would be money from nowhere.
            if (AttachedCustomer is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This sale is paying with store credit but no member is attached any more. Nothing has been taken.",
                    "OK".Translate());
                return false;
            }

            var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Store credit can only be taken while the till is online. Nothing has been taken — take the payment another way.",
                    "OK".Translate());
                return false;
            }

            var (ok, problem) = await api.RedeemCreditAsync(
                AttachedCustomer.Id, creditPence, Uuid7.New(), Uuid7.New());

            if (!ok)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    (problem ?? "That store credit couldn't be taken.")
                    + " Nothing has been taken — the basket is still here.",
                    "OK".Translate());
                return false;
            }

            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Store credit redeemed",
                new Dictionary<string, string>
                {
                    { "CustomerId", AttachedCustomer.Id.ToString() },
                    { "AmountPence", creditPence.ToString() },
                });

            return true;
        }

        /// <summary>
        /// A gift card has been handed over — look it up and hold it for checkout.
        ///
        /// ⚠ IT DOES NOT ADD A BASKET LINE. Nothing is being sold; the card is a way of PAYING, so
        /// it lands in page state and appears as a tender at checkout.
        ///
        /// ⚠ AN UNSOLD CARD IS REFUSED HERE, WITH ITS OWN SENTENCE. A card off the rack scans
        /// perfectly and has a real code — accepting it would hand over goods against value nobody
        /// bought. `IsSpendable` is the shared verdict; this only turns it into words.
        /// </summary>
        private async Task<bool> TryPresentGiftCardAsync(string scanned)
        {
            var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "A gift card can only be used while the till is online — Plutus holds the balance.",
                    "OK".Translate());
                return true;   // handled: it was a card, we just cannot use it
            }

            var card = await api.LookupGiftCardAsync(scanned);

            if (card is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That gift card wasn't recognised. Check the number and try again.",
                    "OK".Translate());
                return true;
            }

            // ⚠ AN UNSOLD CARD IS BEING BOUGHT, NOT SPENT. Same fork as the web till: a code off the
            // rack means the customer is buying it, so ask what to load it with rather than refusing.
            if (string.Equals(card.Status, "unsold", StringComparison.OrdinalIgnoreCase))
            {
                await OfferToSellGiftCardAsync(card);
                return true;
            }

            if (!card.IsSpendable)
            {
                // ⚠ Name the actual state. "Not valid" sends an operator round in circles; "that
                // card hasn't been sold yet" tells them what happened and what to do about it.
                var why = (card.Status ?? string.Empty).ToLowerInvariant() switch
                {
                    "unsold" => "That gift card hasn't been sold yet, so there's nothing on it to spend.",
                    "spent" => "That gift card has already been spent — its balance is zero.",
                    "expired" => "That gift card has expired.",
                    "void" => "That gift card has been cancelled.",
                    _ => "That gift card can't be used.",
                };

                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), why, "OK".Translate());
                return true;
            }

            PresentedGiftCard = card;

            await Application.Current.MainPage.DisplayAlert(
                "Gift card".Translate(),
                string.Format("{0} — take it as payment at checkout.".Translate(), PresentedGiftCardLabel),
                "OK".Translate());

            return true;
        }

        /// <summary>
        /// SELL a gift card — ask what to load it with and put it in the basket (WP13).
        ///
        /// ⚠⚠ THE TENANT'S VOUCHER TREATMENT DECIDES THE VAT, AND AN UNCHOSEN ONE REFUSES THE SALE.
        /// `GiftCardVat` throws rather than guessing, because the treatment decides whether VAT falls
        /// due now or when the card is spent — and a guess writes a wrong figure onto a VAT return.
        /// The message names the portal, because that is where it is fixed.
        ///
        /// ⚠ NOTHING IS ACTIVATED HERE. The line goes in the basket; the card is loaded on the
        /// server at commit, before the sale is recorded. Activating now would leave a live card
        /// behind if the operator then cleared the basket.
        /// </summary>
        private async Task OfferToSellGiftCardAsync(
            Plutus.Client.Core.PlutusApiClient.GiftCardLookupDto card)
        {
            var treatment = SharedKernel.GiftCardVat.FromWireName(card.VatTreatment);

            if (!SharedKernel.GiftCardVat.CanSell(treatment))
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Gift cards can't be sold until the VAT treatment for vouchers is set in the Plutus portal. "
                    + "It decides whether VAT is charged now or when the card is spent, so it can't be guessed.",
                    "OK".Translate());
                return;
            }

            const NumberStyles styles = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                | NumberStyles.AllowDecimalPoint;

            IValidator[] validators = { new RequiredValidator(), new CurrencyValueValidator(styles) };

            ViewElementData[] elements =
            {
                new ViewElementData(1, "Amount to load".Translate(), "", validators.AsEnumerable(), false, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                elements, "Confirm".Translate(), false, "Sell a gift card".Translate(), "Cancel".Translate());

            answers.TryGetValue(1, out var typed);
            if (string.IsNullOrWhiteSpace(typed)) return;

            if (!decimal.TryParse(typed, styles, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That isn't an amount a card can be loaded with.", "OK".Translate());
                return;
            }

            // ⚠ THE PUBLISHED standard rate, never a literal 2000 — a tenant on 5% must get 5%.
            // Only needed for the `Single` treatment; `PairFor` ignores it under `Multi`.
            var standardRateBp = 0;
            if (treatment == SharedKernel.VoucherTreatment.Single)
            {
                var resolved = await Services.Storage.VatBands.StandardRateBpAsync();
                if (resolved is not int bp)
                {
                    // ⚠ REFUSE rather than assume 20%. This till has not been told the rate, and a
                    // guessed rate on a card sale is a wrong VAT return.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "This till doesn't know the current VAT rate yet, so a gift card can't be priced. "
                        + "Check the connection on the Plutus tab, then try again.",
                        "OK".Translate());
                    return;
                }
                standardRateBp = bp;
            }

            var line = Services.Storage.CheckoutCommit.GiftCardItem(
                card.Code, Pence.FromDecimal(amount), treatment, standardRateBp);

            Basket.Add(line);

            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Gift card added to basket",
                new Dictionary<string, string>
                {
                    { "AmountPence", Pence.FromDecimal(amount).ToString() },
                    { "Treatment", treatment.ToString() },
                });
        }

        /// <summary>
        /// Load every gift card this sale is SELLING. False means **abort the sale**.
        ///
        /// ⚠ BEFORE THE COMMIT, like the redeems: a card that cannot be loaded — one already active,
        /// most likely — must stop the sale BEFORE the customer is charged for it.
        ///
        /// ⚠ Idempotent by entry id, so a retried attempt loads once.
        /// </summary>
        private async Task<bool> TryActivateGiftCardsAsync()
        {
            var selling = Services.Storage.CheckoutCommit.GiftCardsSoldIn(Basket);
            if (selling.Count == 0) return true;

            var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "A gift card can only be sold while the till is online — Plutus has to load it. Nothing has been taken.",
                    "OK".Translate());
                return false;
            }

            foreach (var (code, amountPence) in selling)
            {
                var (ok, _, problem) = await api.ActivateGiftCardAsync(
                    code, amountPence, Uuid7.New(), Uuid7.New(),
                    customerId: AttachedCustomer?.Id);

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        (problem ?? "That gift card couldn't be loaded.")
                        + " Nothing has been taken — the basket is still here.",
                        "OK".Translate());
                    return false;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Gift card activated",
                    new Dictionary<string, string> { { "AmountPence", amountPence.ToString() } });
            }

            return true;
        }

        /// <summary>
        /// Spend the gift card this sale is settling with, if any. False means **abort the sale**.
        ///
        /// ⚠ Called BEFORE the commit, exactly like store credit and for exactly the same reason:
        /// the server owns the balance, so an over-redeem, an expiry or a void must be refused while
        /// the sale can still be abandoned.
        /// </summary>
        private async Task<bool> TryRedeemGiftCardAsync(
            IReadOnlyList<Plutus.Contracts.Client.IngestTender> tenders)
        {
            var giftPence = tenders
                .Where(t => t.TenderType == SharedKernel.Tenders.GiftCard)
                .Sum(t => t.AmountPence);

            if (giftPence <= 0) return true;

            if (PresentedGiftCard?.Code is not string code)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This sale is paying with a gift card but none is being held any more. Nothing has been taken.",
                    "OK".Translate());
                return false;
            }

            var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "A gift card can only be taken while the till is online. Nothing has been taken — take the payment another way.",
                    "OK".Translate());
                return false;
            }

            var (ok, balance, problem) = await api.RedeemGiftCardAsync(
                code, giftPence, Uuid7.New(), Uuid7.New());

            if (!ok)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    (problem ?? "That gift card couldn't be taken.")
                    + " Nothing has been taken — the basket is still here.",
                    "OK".Translate());
                return false;
            }

            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Gift card redeemed",
                new Dictionary<string, string>
                {
                    { "AmountPence", giftPence.ToString() },
                    { "BalanceAfterPence", balance.ToString() },
                });

            return true;
        }

        /// <summary>
        /// The shop's scheduled discount rules, as this till last heard them.
        ///
        /// ⚠ HELD IN MEMORY because the rebuild below runs from `Basket.CollectionChanged`, which is
        /// synchronous — reaching for SQLite there would mean either blocking the UI thread on every
        /// scan or making the discount arrive a moment after the line, which is worse than not having
        /// it. Loaded once when the till screen opens and refreshed on the sync cadence.
        ///
        /// ⚠ EMPTY, NEVER NULL — "not told any rules" and "no rules" both mean full price at a counter.
        /// </summary>
        private IReadOnlyList<ScheduledDiscount> _discountRules = Array.Empty<ScheduledDiscount>();

        /// <summary>
        /// Lines whose AUTOMATIC discount the operator deliberately took off.
        ///
        /// ⚠⚠ WITHOUT THIS "you can always charge full price" LASTS UNTIL THE NEXT SCAN. Removing the
        /// alteration fires `CollectionChanged`, the rebuild runs, the line is eligible again and the
        /// discount comes straight back — the operator would watch it reappear with no way to stop it.
        ///
        /// ⚠ Reference identity on the BASKET LINE, so it is cleared by removing the line — which is
        /// the operator's own "ring it again" gesture. ⚠ Page state, not basket state: a parked basket
        /// keeps its records, and a waiver is a decision about the sale in front of somebody now.
        /// </summary>
        private readonly List<BasketItem> _autoDiscountWaived = new();

        /// <summary>
        /// Rebuild every automatic discount to match the basket as it stands — the member's tier and
        /// any live scheduled rule, through the one shared resolver.
        ///
        /// ⚠⚠ RE-ENTRANCY IS THE WHOLE RISK HERE. This is called FROM `Basket.CollectionChanged`, and
        /// it adds and removes basket records — so without the flag it would recurse until the stack
        /// ran out, on the first scan a member ever made.
        ///
        /// ⚠ REMOVE THEN REBUILD, never edit in place: an alteration's associated-items list is what
        /// decides where the money lands, and mutating it would leave a stale association pointing at
        /// lines that are no longer in the basket.
        ///
        /// ⚠ THERE CAN BE MORE THAN ONE NOW. Two rules can target different categories, and a member's
        /// tier may out-bid one of them and not the other — so this removes ALL the automatic
        /// alterations and puts back however many the resolver produces.
        ///
        /// ⚠ Silent by design. A discount that cannot be computed must not interrupt a sale; the worst
        /// case is the pre-existing behaviour (full price), which is visible on screen.
        /// </summary>
        private void RefreshAutoDiscounts()
        {
            if (_refreshingMemberDiscount) return;
            _refreshingMemberDiscount = true;

            try
            {
                foreach (var existing in Services.Storage.AutoDiscountBasket.ExistingOn(Basket))
                    Basket.Remove(existing);

                var m = AttachedCustomer?.Membership;
                var member = m is null
                    ? MemberStanding.None
                    : new MemberStanding(true, m.Expired, m.AutoDiscountRate, m.Tier);

                // ⚠ Nothing to do at all when there is neither a member nor a rule — the common case,
                // and worth short-circuiting so an ordinary sale does no work per scan.
                if (!member.HasMembership && _discountRules.Count == 0) return;

                // ⚠⚠ THE TILL'S OWN CLOCK, LOCAL, at this instant. `DateTime.Now` rather than UtcNow
                // is deliberate: the day mask and the time window are wall-clock facts about the shop
                // floor ("Wednesdays, 09:00–17:00"), and the shared rule converts to UTC itself for
                // the validity bounds. Passing UtcNow would make a Wednesday rule end at midnight UTC.
                var rebuilt = Services.Storage.AutoDiscountBasket.Build(
                    Basket,
                    member,
                    _discountRules,
                    DateTime.Now,
                    App.GetViewModel().SignedInOperator?.UserId ?? Guid.Empty,
                    replacing: null,
                    waived: _autoDiscountWaived);

                foreach (var alteration in rebuilt) Basket.Add(alteration);
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.RefreshAutoDiscounts", ex);
            }
            finally
            {
                _refreshingMemberDiscount = false;
                OnPropertyChanged(nameof(SaleExTax));
                OnPropertyChanged(nameof(SaleIncTax));
            }
        }

        /// <summary>
        /// Load this till's discount rules from the cache, then rebuild.
        ///
        /// ⚠ FROM THE CACHE, so it works with the line down — the refresh path is the sync cadence's
        /// job, not this one's. ⚠ Failure is silent and leaves the rules empty: a shop must not lose
        /// its till because a promotions feed was unreachable.
        /// </summary>
        private async Task LoadDiscountRulesAsync()
        {
            try
            {
                var api = await Services.Storage.TillPlacement.TryCreateApiAsync().ConfigureAwait(false);
                if (api is null) return;

                var rules = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => new Plutus.Client.Core.DiscountRuleCache(
                        api, new Plutus.Client.Storage.MetaDiscountRuleStore(s)).RulesAsync())
                    .ConfigureAwait(false);

                if (rules is null) return;

                _discountRules = rules;
                MainThread.BeginInvokeOnMainThread(RefreshAutoDiscounts);
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.LoadDiscountRulesAsync", ex);
            }
        }

        #endregion

        #region Transaction
        #region Alter
        // ⚠ `async void` because it is a Command handler — so it MUST NOT let an exception escape.
        // The try/finally below is the only thing between a bad discount list and a closed till.
        private async void ExecuteAlterTransactionSelector()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                Alterations.Clear();

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Alteration (Discounts)");
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);

                // ⚠ `App.GetViewModel().EmployeeId` used to be passed here and it CRASHED THE APP on
                // a portal-provisioned till: the property threw on an empty legacy roster, out of a
                // plain `void` command handler, straight through Button.Clicked to the UI thread.
                // Tapping the leftmost button on the till screen closed the application. It now
                // returns null, and the audit user is the SIGNED-IN OPERATOR, which is the person
                // who actually applied the discount on either sign-in path.
                // ⚠ L9, 2026-08-23 — the `?? EmployeeId` fallback is gone with the legacy login that
                // was the only thing filling `Employees`. It could only ever answer null now, and the
                // signed-in operator is the person who actually applied the discount on the one
                // remaining sign-in path.
                var auditUser = App.GetViewModel().SignedInOperator?.UserId.ToString();

                // ⚠⚠ THE LAST LEGACY DATABASE READ ON THIS TILL IS GONE — L5, 2026-08-23, and it
                // changes nothing an operator sees. The comment below already recorded why: *"The
                // legacy Discounts table is empty on a portal till and was never seeded even on
                // legacy ones."* This loop has been adding nothing to an empty list.
                //
                // ⚠ SO THE PICKER STILL OPENS EMPTY, exactly as it did yesterday, and the guard
                // below still catches it. That is a removal, not a regression.
                //
                // ⚠ WIRING THE REAL MANUAL DISCOUNTS IS A FEATURE, NOT THIS. `/api/v1/discounts/rules`
                // already carries them — `DiscountRuleDto.AutoApply == false` is documented as "a
                // catalogue entry an operator picks" — and this till already fetches that endpoint
                // for the AUTOMATIC half (`LoadDiscountRulesAsync`). Filling this list from the
                // manual half would give MAUI a capability it has never actually had, which is a
                // parity job with its own row, not something to slip into a legacy sweep.

                // ⚠ THE PICKER MUST NOT OPEN EMPTY. This was an `SfPicker`, whose `SelectedIndex` on
                // a column with no rows is 0, not null, so the view's SelectionChanged fired
                // `AlterTransactionCommand.Execute(0)` and `Alterations.ElementAt(0)` threw
                // ArgumentOutOfRangeException in another `async void` — an empty dialog whose OK
                // button closed the app. The legacy Discounts table is empty on a portal till and
                // was never seeded even on legacy ones. ⚠ The guard STAYS even though an action
                // sheet cannot do that: an empty sheet is still a dead end for the operator.
                if (Alterations.Count == 0)
                {
                    Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(),
                        "There are no discounts set up for this till yet.",
                        "OK".Translate());
                    return;
                }

                // ⚠ AN ACTION SHEET, NOT A SYNCFUSION PICKER (2026-08-10). Matt is not renewing the
                // licence, and this app already uses `DisplayActionSheet` for tenders, item search
                // and refund origins — so this is the control operators here already know, and it
                // has no markup that can go stale against a package version.
                //
                // ⚠ Through `Modal`, because choosing a discount leads straight into ANOTHER dialog
                // (the amount prompt), and two modals in quick succession is what threw the
                // COMException that closed the till at the payment prompt.
                var names = AlterationNames.ToArray();
                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                        "Alterations".Translate(), "Cancel".Translate(), null, names));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                var index = Array.IndexOf(names, picked);
                if (index < 0) return;

                // ⚠ Released BEFORE dispatching, because `ExecuteAlterTransaction` opens with the
                // same `if (IsBusy) return;` guard — leaving it set here would make the discount
                // silently do nothing, which is exactly the failure this whole session keeps finding.
                IsBusy = false;
                ExecuteAlterTransaction(index);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteAlterTransaction(int selectedAlteration)
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                // ⚠ THIS HAD NO PERMISSION CHECK AT ALL, and it is the one place on the till where
                // an operator types a money amount straight off the goods — a cash discount or a
                // percentage, unbounded. `pos.discount` is ceiling-capable precisely so it can be
                // handed out with a limit; nothing was asking for it.
                var discountGate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosDiscount);

                if (!discountGate.Allowed &&
                    (!discountGate.NeedsOverride ||
                     await RequestSupervisorOverrideAsync(discountGate.Permission, discountGate.AmountPence) is null))
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(), discountGate.Message, "OK".Translate());
                    return;
                }

                var alteration = Alterations.ElementAt(selectedAlteration);
                var items = new List<BasketItem>();

                foreach (var item in Basket.Where(br => br is BasketItem).Cast<BasketItem>().ToList())
                {
                    for (var i = 0; i < item.Quantity; i++)
                        items.Add((BasketItem)item.Clone());
                }

                var entries = new ViewElementData[1];

                // ⚠⚠ THE PERCENT BOX IS PRE-FILLED AS A PERCENT (Matt, 2026-08-17: *"make it %"*).
                // `DiscountModel.Amount` from the legacy NatApp table holds a FRACTION — 0.1 for 10%,
                // which is why the old `Price * Amount` looked plausible. A box that now means
                // "percent" has to show **10**, or every migrated discount reads as 0.1% and applies
                // a hundredth of itself.
                var prefill = alteration.Type == 0
                    ? alteration.Amount.ToString(CultureInfo.CurrentCulture)
                    : Plutus.SharedKernel.PercentDiscountInput
                        .PercentFromFraction(alteration.Amount).ToString(CultureInfo.CurrentCulture);

                if (alteration.Amount == 0.0m)
                    entries[0] = new ViewElementData(1,
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), "0", new List<IValidator>(), false, true);

                else
                    entries[0] = new ViewElementData(1,
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), prefill, new List<IValidator>(), false, false);

                //data type -> Tuple<List<string>, List<BasketItem>>
                var (alterationAmounts, applyAlterationsToBasketItems) = await Helpers.CustomViews.InputMultiSelectAlertHelper<string, BasketItem>.LaunchInputMulitSelectAlertAsync(entries, Tuple.Create<IEnumerable<BasketItem>, string>(items, "Name"), default, "Confirm".Translate(), false, "Alterations".Translate());

                // ⚠⚠ THE PERCENT IS PARSED ONCE, HERE, AND REFUSED POLITELY (Matt, 2026-08-17:
                // *"make it %"*). Until now both percentage branches did
                // `item.Price * Decimal.Parse(typed)` — so typing **10** for 10% multiplied the price
                // BY TEN. Nobody was overcharged, because the money rule below refuses a discount
                // larger than the basket, but the operator was told the basket was too small rather
                // than that they had typed the wrong thing, and no percentage over 100% could ever
                // be applied at all.
                //
                // ⚠ `LineDiscounts.Percentage` THROWS on a fraction above 1 — correctly, since by
                // then it is a programming error. An operator mistyping is not, so the parse and the
                // refusal happen here and the money rule stays strict behind them.
                decimal fraction = 0m;

                if (alteration.Type != 0)
                {
                    if (Plutus.SharedKernel.PercentDiscountInput
                            .FractionFromTyped(alterationAmounts.First()) is not decimal parsed)
                    {
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                            Plutus.SharedKernel.PercentDiscountInput.RefusalMessage(alterationAmounts.First()),
                            "OK".Translate());
                        return;
                    }

                    fraction = parsed;
                }

                BasketAlteration adjustment;

                // ⚠⚠ BUILT FIRST, CHECKED, AND ONLY THEN ADDED TO THE BASKET (2026-08-13, binding
                // default 22). The two branches below produce the same total by different arithmetic
                // — one rounds per item, the other rounds the sum — so computing "what will this
                // discount come to?" a second time for the check would be a copy that drifts from the
                // thing it is checking. Building the real alterations and summing THEM cannot drift.
                //
                // ⚠ Nothing reaches `Basket` until both gates below have passed. A half-applied
                // discount (some items altered, then a refusal) would leave the operator to undo it
                // by hand, in front of a customer.
                var pending = new List<BasketAlteration>();

                if (applyAlterationsToBasketItems.Count() < items.Count())
                {
                    foreach (var item in applyAlterationsToBasketItems)
                    {
                        if (alteration.Type == 0)
                        {
                            var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1;
                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount, alterationAmount);

                        }
                        else
                        {
                            // ⚠ THE SHARED MONEY RULE, in pence, on both the inc and the ex figure —
                            // the ex one decides the VAT, so deriving it any other way would put the
                            // two tills' VAT returns a penny apart on the same basket.
                            // ⚠ Quantity 1: this branch has already exploded the basket into one
                            // clone per unit, so each `item` here IS a single unit.
                            var incOff = Plutus.SharedKernel.LineDiscounts.Percentage(
                                item.PricePence, 1, fraction, isReturn: false);
                            var exOff = Plutus.SharedKernel.LineDiscounts.Percentage(
                                item.PriceExTaxPence, 1, fraction, isReturn: false);

                            var alterationAmount = Tuple.Create(
                                incOff / -100m,
                                exOff / -100m);

                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount.Item1, alterationAmount.Item2);
                        }
                        pending.Add(adjustment);
                    }
                }
                else
                {
                    if (alteration.Type == 0)
                    {
                        var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(alterationAmounts.First()) * applyAlterationsToBasketItems.Count(), 2, MidpointRounding.AwayFromZero)) * -1;
                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, applyAlterationsToBasketItems, alterationAmount, alterationAmount);
                    }
                    else
                    {
                        // ⚠ THE WHOLE-SELECTION BRANCH rounds the SUM once, not each item — the same
                        // choice the web till makes, and the reason the two branches can differ by a
                        // penny on odd quantities. `LineDiscounts.Percentage` does that rounding.
                        var incTotal = applyAlterationsToBasketItems.Sum(t => t.PricePence);
                        var exTotal = applyAlterationsToBasketItems.Sum(t => t.PriceExTaxPence);

                        var alterationAmount = Tuple.Create(
                            Plutus.SharedKernel.LineDiscounts.Percentage(incTotal, 1, fraction, isReturn: false) / -100m,
                            Plutus.SharedKernel.LineDiscounts.Percentage(exTotal, 1, fraction, isReturn: false) / -100m);

                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, applyAlterationsToBasketItems, alterationAmount.Item1, alterationAmount.Item2);
                    }
                    pending.Add(adjustment);
                }

                var requestedPence = pending.Sum(a => Pence.FromDecimal(Math.Abs(a.Price)) * Math.Max(1, a.Quantity));

                // ── Gate 1: the money rule (default 22a) ──────────────────────────────────────────
                // ⚠ Matt, 2026-08-13: *"You cannot have a discount greater than the basket."* Checked
                // BEFORE the ceiling, because "that is more than the basket" is true regardless of
                // who is signed in — asking a supervisor to step up and authorise an impossible
                // discount would waste their walk to the till and still fail.
                var headroomDecision = Plutus.SharedKernel.BasketDiscounts.Authorise(
                    requestedPence, SaleLinesGrossPence(), DiscountAlreadyPence());

                if (!headroomDecision.IsAllowed)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(), DiscountRefusalMessage(headroomDecision), "OK".Translate());
                    return;
                }

                // ── Gate 2: WHY (default 22c) ─────────────────────────────────────────────────────
                // ⚠ Matt, 2026-08-13: *"All discounts need to be tracked — till, logged-in employee
                // and reason."* The till and the employee were already on the sale header; the
                // reason was collected NOWHERE, on any till.
                //
                // ⚠ BEFORE THE CEILING, deliberately, so a supervisor is approving a REASON and not a
                // bare number. Fetching them first and asking why afterwards means the person with
                // the authority never sees what they authorised.
                //
                // ⚠ The refusal is HERE, where the operator can still act on it — not at commit,
                // where the money is already on the basket and dropping the sale would cost more
                // than the missing string is worth.
                var reason = await AskForDiscountReasonAsync();
                if (reason is null) return;   // cancelled — the basket is untouched

                // ── Gate 2: the operator's ceiling, now that the AMOUNT is known ──────────────────
                // ⚠⚠ THIS IS WHY THE CHECK MOVED. The gate at the top of this method asks only "may
                // this operator discount AT ALL?" — it runs before the amount exists, so
                // `pos.discount`'s ceiling could never bite and the supervisor prompt never appeared
                // however large the discount. Found 2026-08-13; the comment up there already said
                // "nothing was asking for it".
                //
                // ⚠ BOTH gates stay. The early one refuses someone who may not discount at all
                // before making them type an amount they can never apply; this one refuses the
                // amount. Removing either brings back a defect.
                var amountGate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosDiscount, requestedPence);

                // ⚠ `stepUpWasRequired` is what the GATE said, not whether a grant turned up. Inferring
                // it from the grant would make a step-up that silently failed to record an authoriser
                // read as "no step-up was needed" — the one case the audit rule exists to catch.
                var stepUpWasRequired = !amountGate.Allowed;
                SupervisorGrant? grant = null;

                if (!amountGate.Allowed)
                {
                    grant = amountGate.NeedsOverride
                        ? await RequestSupervisorOverrideAsync(amountGate.Permission, amountGate.AmountPence)
                        : null;

                    if (grant is null)
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Hmm".Translate(), amountGate.Message, "OK".Translate());
                        return;
                    }
                }

                // ── Gate 4: can what just happened be WRITTEN DOWN? ───────────────────────────────
                // ⚠ The rule is shared (`SharedKernel.DiscountAudit`) so the web till cannot enforce
                // a different one. It re-checks self-approval that `OperatorLogin` already refused —
                // two guards on one rule, the first protecting the ACT and this one the RECORD,
                // because an audit trail saying a cashier approved their own £50 discount is worse
                // than no trail at all.
                var operatorId = App.GetViewModel().SignedInOperator?.UserId ?? Guid.Empty;

                var (auditVerdict, authority) = Plutus.SharedKernel.DiscountAudit.Authorise(
                    reason, requestedPence, operatorId,
                    stepUpWasRequired, grant?.AuthorisedByUserId, grant?.AuthorisedByName);

                if (auditVerdict != Plutus.SharedKernel.DiscountAuditVerdict.Recordable)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(), DiscountAuditRefusalMessage(auditVerdict), "OK".Translate());
                    return;
                }

                // ⚠ Stamped on every pending alteration BEFORE any of them reaches the basket, so a
                // discount can never be added without its attribution. `CheckoutCommit` splits the
                // reason across the lines the money apportions onto.
                foreach (var a in pending)
                {
                    a.DiscountReason = authority.Reason;
                    a.RequestedByUserId = authority.RequestedByUserId;
                    a.AuthorisedByUserId = authority.AuthorisedByUserId;
                    a.AuthorisedByName = authority.AuthorisedByName;
                }

                foreach (var a in pending) Basket.Add(a);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Stored Transactions
        private async void ExecuteStoreTransaction()
        {
            if (IsBusy == true || Basket.Count == 0)
                return;
            IsBusy = true;
            try
            {
                IValidator[] validators = {
                    new RequiredValidator()
                };

                ViewElementData[] elements = {
                    new ViewElementData(1, "Name".Translate(), "", validators.AsEnumerable(), false, true)
                };

                string transName;
                var firstRun = true;
                do
                {
                    if (!firstRun)
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "NameMustBeUniqueMesg".Translate(), "OK".Translate());

                    _ = (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), false, "TransactionName".Translate(), "Cancel".Translate())).TryGetValue(1, out transName);

                    if (string.IsNullOrEmpty(transName))
                    {

                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    firstRun = false;
                } while (StoredTransactions.Any(sT => sT.Name.Equals(transName)));


                var basketRecords = new IBasketRecord[Basket.Count];
                Basket.CopyTo(basketRecords, 0);

                // ⚠ THE V2 STORE, AND CONTRACT JSON (cutover step 18, binding default 15). This
                // wrote to the LEGACY database — which on a portal-provisioned till is an empty
                // file the app creates on first use — and serialised with Newtonsoft
                // `TypeNameHandling.Auto`, embedding .NET type names in the blob. Those stop
                // resolving the moment a namespace moves, and this codebase has renamed the
                // namespace, the assembly AND the classes: that is what made a discounted parked
                // basket crash the app on recall.
                var parkId = Uuid7.New();
                var saved = false;
                try
                {
                    await Services.Storage.TillStoreAccess.UseAsync(async s =>
                    {
                        await s.SaveBasketAsync(parkId, transName, Services.Storage.ParkedBasket.ToJson(basketRecords));
                        return true;
                    });
                    saved = true;
                }
                catch (Exception ex)
                {
                    CrashLog.Write("TillViewModel.ExecuteStoreTransaction", ex);
                }

                if (!saved)
                {
                    // ⚠ The basket is NOT cleared. Clearing after a failed park loses it entirely,
                    // and the operator believes it is safely put aside.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                    return;
                }

                StoredTransactions.Add(new SavedTransactionModel { Id = parkId.ToString("D"), Name = transName });
                Basket.Clear();

                // ⚠ THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a

                // just-sold item being re-added — see the guard in ExecuteItemAdd.

                SelectedBasketRecord = null;

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteRetrieveTransaction()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                // ⚠⚠ NOTHING PARKED IS NOT AN ERROR, AND IT USED TO BE A CRASH. The `else` branch
                // below calls `StoredTransactions.First()`, which throws `InvalidOperationException`
                // on an empty collection — out of an `async void`, so the till died rather than the
                // action failing. The Retrieve button is disabled when this is empty, but a disabled
                // button is a UI state and this is the method: both guards, because only one of them
                // is a guarantee.
                if (StoredTransactions.Count == 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Nothing saved",
                        "There are no parked baskets to open. Save one with \"Save Transaction\" first.",
                        "OK".Translate());
                    return;
                }

                SavedTransactionModel storedTransaction;
                if (StoredTransactions.Count > 1)
                {
                    var baskets = new string[StoredTransactions.Count];
                    for (int i = 0; i < StoredTransactions.Count; i++)
                        baskets[i] = StoredTransactions.ElementAt(i).Name;
                    var action = await Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync("Baskets".Translate(), "Cancel".Translate(), null, baskets);
                    if (action == "Cancel".Translate())
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Retrieved", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    storedTransaction = StoredTransactions.First(sT => sT.Name.Equals(action));
                }
                else
                {
                    storedTransaction = StoredTransactions.First();
                }

                if (Basket.Count > 0)
                    if (!await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "BasketWillBeClearedMesg".Translate(), "OK".Translate(), "Cancel".Translate()))
                        return;

                if (!Guid.TryParse(storedTransaction.Id, out var parkId))
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That parked basket was saved by an older version of this till and can't be opened.",
                        "OK".Translate());
                    StoredTransactions.Remove(storedTransaction);
                    return;
                }

                // ⚠ READ IT BEFORE DELETING IT. The old code deleted the row and then deserialised
                // the copy it happened to be holding — so a blob that failed to parse (which the
                // `$type` metadata made a real possibility) destroyed the basket AND lost it.
                var contractJson = await Services.Storage.TillStoreAccess.UseAsync(async s =>
                    (await s.ListBasketsAsync()).FirstOrDefault(b => b.Id == parkId)?.ContractJson);

                var basket = Services.Storage.ParkedBasket.FromJson(contractJson);

                if (basket.Count == 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That parked basket is empty or couldn't be read, so it hasn't been opened.",
                        "OK".Translate());
                    return;
                }

                // ⚠ DELETE ONLY ONCE THE CONTENTS ARE IN HAND, and only if the row was really there:
                // a silent no-op would let the same basket be recalled twice and sold twice.
                var removed = await Services.Storage.TillStoreAccess.UseAsync(s => s.DeleteBasketAsync(parkId));
                if (!removed)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Someone else has already opened that basket on this till.", "OK".Translate());
                    StoredTransactions.Remove(storedTransaction);
                    return;
                }

                StoredTransactions.Remove(storedTransaction);

                Basket.Clear();


                // ⚠ THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a


                // just-sold item being re-added — see the guard in ExecuteItemAdd.


                SelectedBasketRecord = null;
                foreach (var item in basket)
                    Basket.Add(item);

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        private async void ExecuteCheckoutTransaction()
        {

            if (IsBusy) return;

            using var activity = Plutus.Frontend.AppClient.Services.Analytics.Observability.ActivitySource.StartActivity("Till.CheckoutTransaction");

            IsBusy = true;
            try
            {
                // ⚠ THIS LINE USED TO READ `App.GetViewModel().EmployeeId` AND IT CLOSED THE APP.
                // That property is `Employees.Last().Id`, and `Employees` is populated ONLY by the
                // legacy local login — the portal roster path sets `SignedInOperator` and never
                // touches it. So on every portal-provisioned till the read threw
                // `InvalidOperationException: Sequence contains no elements`, from an `async void`
                // with no catch, BEFORE the first await — which reposts to the UI thread as an
                // unhandled exception and terminates the process. Pressing Checkout killed the till
                // mid-sale, with a full basket and a customer at the counter, and no dialog.
                //
                // ⚠ And it fed NOTHING. The value was set on a `SaleModel` that step 11 stopped
                // persisting; the sale is attributed from `SignedInOperator.UserId` at commit. A
                // read with no consumer was the single thing preventing any sale on any new till.
                // ⚠ `Notes` is no longer initialised here — nothing fills it and nothing reads it
                // since the receipt started taking its notes from the basket (step 11b). What is
                // left of this legacy model on the checkout path is `Total` (the tender loop and
                // the confirm dialog) and `PaySales` (tenders, drawer, receipt method names); both
                // go with L6.
                var sale = new SaleModel
                {
                    DateOfSale = DateTime.Now,
                    Total = 0.0m,
                    PaySales = new List<PaymentMethod_SaleModel>(),
                };

                var change = 0.0m;

                // ⚠ Lifted to `CheckoutCommit.IsRefundOnly` on 2026-08-21 (step 11b), predicate
                // unchanged. It decides which tenders are offered, whether the finding-Y refund caps
                // apply and whether a card surcharge is charged — three money behaviours that had no
                // test between them while the decision sat inside an `async void` nothing can reach.
                var refundOnly = Services.Storage.CheckoutCommit.IsRefundOnly(Basket);

                // ⚠ A REFUND GOES BACK THE WAY IT WAS PAID. Matt, 2026-08-11: *"Refunds need to
                // ONLY offer the method that was used to pay. E.g. if it was a card payment, needs
                // to go back to card."*
                //
                // ⚠ Not tidiness — refunding a card sale in cash is the oldest till fraud there is,
                // and the honest version empties the drawer just as effectively: a day of card
                // sales refunded in cash leaves the drawer short and the card takings untouched.
                //
                // ⚠ Only for a refund-ONLY basket. A mixed basket is a net SALE; restricting how
                // the customer may pay the balance because one line is a return would be nonsense.
                var payMeths = GenPaymentMethodActions(
                    refundOnly ? await OriginTenderTypesAsync() : null);

                // ⚠ FINDING Y: how much each tender may take back, not merely WHICH may be used.
                var refundCaps = refundOnly
                    ? await OriginTenderCapacitiesAsync()
                    : Array.Empty<SharedKernel.TenderCapacity>();

                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);

                // ⚠ ONE SUM, IN PENCE — see `SaleIncTax`. `sale.Total` feeds the checkout screen's
                // heading and the confirm dialog, and `CheckoutCommit` reconciles the payload against
                // `BasketMoneyPence`; deriving them separately is how a screen and a payload come to
                // disagree by a penny.
                sale.Total = Services.Storage.CheckoutCommit.BasketMoneyPence(Basket) / 100m;
                sale.TotalExTax = Services.Storage.CheckoutCommit.BasketMoneyExPence(Basket) / 100m;

                // ⚠ `testStyles` and `lastPickedName` were HERE and are gone (2026-08-19): they served
                // the sequential amount prompt, which no longer exists. The pence parser is now
                // `TenderSettlement.ParsePence`, a C2 twin of the web till's `money.ts parsePence` —
                // and deliberately STRICTER than the `NumberStyles` this used, which accepted a leading
                // sign, so "-5.00" parsed as a negative tender.
                //
                // ⚠ `chosenMethods` went with them: it existed so two payments on ONE method could
                // share a `PaymentMethodModel`, which only the sequential loop could produce.

                // ⚠ THE TENDER SEQUENCE NOW LIVES IN `Client.Core.TenderLoop` (cutover step 11b).
                //
                // It was ~90 lines here, inside a ~200-line `async void` that also assembles the
                // sale, adds the surcharge line, commits and prints — so NOTHING about taking money
                // could be exercised without a running UI host. All three tendering defects found
                // on 2026-08-10 shipped as a result, and every one was a loop that could not
                // terminate: a cancel that fell through and appended a £0 payment, a `0` tender that
                // did the same, and an amount prompt with no exit at all.
                //
                // The loop is now 19 unit tests and three mutation checks. What was left here was the
                // ASKING — dialogs — and mapping the answer onto the legacy sale model.
                //
                // ⚠⚠ AND THE ASKING HAS NOW MOVED TOO (2026-08-19). What follows is one call.
                //
                // THE ONE-SCREEN CHECKOUT (§5c item 2, 2026-08-19).
                //
                // ⚠⚠ MATT: *"I need the functionality and look and feel to be the same across both
                // tills. So if a user swaps between the two, it doesnt matter and they would
                // understand how to use it."* That superseded 2026-08-17's *"parity in FUNCTIONALITY,
                // not in how the functions operate"*, which was the only thing justifying the
                // sequential prompt chain that used to live here. Every method is now on one screen
                // with a live Paid / Remaining / Change, exactly like `CheckoutDialog.tsx`.
                //
                // ⚠⚠ AND IT KILLS THE DOUBLE-TAKE BY CONSTRUCTION. The loop asked for a method and an
                // amount over and over, so a capped tender could be picked twice and take its cap each
                // time — the money defect of 2026-08-19, fixed there with an accumulating guard. One
                // box per method means there is no second pass to take.
                //
                // ⚠ `TenderLoop` IS NOT DELETED. It is still the C1 home of the sequential rules and
                // is still what a headless caller would use; the arithmetic this screen runs on
                // (`TenderSettlement`) is the C2 twin of the web till's `tendering.ts`, pinned on both
                // sides. **Nothing about money is decided in this method.**
                var tender = await TakeTendersOnOneScreenAsync(sale, payMeths, refundCaps, refundOnly);

                // ⚠ ABANDONED TAKES NOTHING AND LEAVES THE BASKET ALONE. It is not a partial
                // success: `tender.Payments` is empty by construction. Clearing the basket here
                // would lose the sale and the evidence together.
                if (tender.Abandoned)
                {
                    Logger.LogEvent(AppLogLevel.Info, "Sale Processing",
                        new Dictionary<string, string> { { "Canceled", "True" } });
                    return;
                }

                foreach (var taken in tender.Payments)
                {
                    sale.PaySales.Add(new PaymentMethod_SaleModel
                    {
                        // ⚠ A FRESH MODEL PER PAYMENT IS NOW CORRECT (2026-08-19). This used to consult
                        // a `chosenMethods` cache so two payments on ONE method shared an instance —
                        // which the sequential loop could produce, because it asked over and over. One
                        // box per method means one payment per method, so there is nothing to share,
                        // and the cache was left holding nothing.
                        TempPayMethod = payMeths[taken.MethodName](),
                        Amount = taken.AmountPence / 100m,
                        Change = taken.ChangePence / 100m,
                    });
                }

                change = tender.ChangePence / 100m;

                if (sale.PaySales.Any(p => p.TempPayMethod.IsCashBackable) && CashbackEnabled)
                {
                    //Cash-back stuff here
                }

                // ⚠ The receipt's notes are read straight off the BASKET now
                // (`CheckoutCommit.ReceiptNotesFrom`, step 11b) rather than copied into
                // `sale.Notes` here and read back out at print time. One collection, one order, and
                // the rule is testable — it was two hops through a legacy model that nothing else
                // ever looked at.

                var @continue = await App.Current.MainPage.DisplayAlert(
                    "Hmm".Translate(),
                    string.Format(
                        "TransacContinue".Translate(),
                        Math.Round(sale.Total,
                            2,
                            MidpointRounding.AwayFromZero).ToString("C", CultureInfo.CurrentCulture)),
                    "Yes".Translate(),
                    "Cancel".Translate());

                if (!@continue) return;

                // ⚠⚠ THE LEGACY SALE GRAPH IS GONE (step 11b, 2026-08-14) — ~35 lines that built
                // `sale.Transactions`, `sale.Refunds` and their `CheckoutItemChangeModel`s.
                //
                // ⚠ IT WAS BUILT AND NEVER READ. Traced before deleting: `FinaliseTransation` — the
                // only thing `sale` is passed to — touches `PaySales` (tenders, the drawer decision,
                // the receipt's method names) and `Notes`, and nothing else. Nothing persists it
                // either: step 11 removed the `db.Save()`, so this object graph was assembled in
                // memory on every single checkout and dropped on the floor.
                //
                // ⚠ The only readers of `.Transactions`/`.Refunds` are `SalesReportsViewModel` and
                // `StockOuttakeViewModel`, and they `.Include(...)` them **from the legacy database**
                // — which this checkout has not written to since step 11. They were reading a table
                // this code was no longer filling; deleting the write changes nothing they see.
                //
                // ⚠ It also carried a defect nobody would ever have seen fire: the discount lookup
                // took `FirstOrDefault` over the alterations, so an item discounted by TWO
                // alterations recorded only the first — and the whole record was discarded anyway.
                // The real attribution now travels on the wire as `LineMeta.discountAuthority`.
                //
                // ⚠ This is the last coupling from the money path to `TransactionModel` /
                // `RefundModel` / `CheckoutItemChangeModel` — see L6.

                // ⚠ ONE GATE, AGAINST THE OPERATOR'S OWN CEILING (cutover step 12). What was here
                // could not work on a portal-provisioned till and had a hole in it besides:
                //
                //   · it looked up string action names ("Till", "Refund20", "Refund100") in a local
                //     AuthActions table that such a till does not have;
                //   · the hardcoded £20/£100/unlimited bands ignored each operator's actual ceiling;
                //   · ⚠ the refund total summed each return line's UNIT price and IGNORED QUANTITY,
                //     so five £30 returns tested as £30 and went straight through the £100 band;
                //   · and the do/while "escalation" re-tested the SAME operator every pass while
                //     RequestAuthorisedUserInput never assigned the id it returned — so entering
                //     correct supervisor credentials re-prompted for ever and only Cancel escaped.
                //     Supervisor override on this till has never once succeeded.
                //
                // ⚠ A NULL operator BLOCKS. "We don't know who this is" must never mean "let them".
                var gate = Services.Security.TillGate.CheckCheckout(
                    App.GetViewModel().SignedInOperator, Basket);

                if (!gate.Allowed)
                {
                    if (!gate.NeedsOverride)
                    {
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                        return;
                    }

                    // ⚠ A real override: a SECOND person authenticates, and OperatorLogin refuses
                    // self-authorisation, applies the SUPERVISOR's own ceiling and window, and names
                    // both people for the audit trail. Declining leaves the basket untouched.
                    //
                    // ⚠ ESCALATE WHAT THE GATE ACTUALLY REFUSED. `CheckCheckout` tests `pos.sell`
                    // FIRST, so hardcoding "refund" here asked a supervisor to authorise a £0.00
                    // refund — a basket with no returns refunds nothing — and then let an ordinary
                    // sale through on the strength of it, nobody having been asked whether this
                    // operator may sell. Any supervisor holding `pos.refund`, including one
                    // explicitly denied `pos.sell`, would have waved it through.
                    if (await RequestSupervisorOverrideAsync(gate.Permission, gate.AmountPence) is null)
                        return;
                }

                FinaliseTransation(sale, change);
                return;
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — WITHOUT THIS THE TILL CLOSES. This method had a `try`/`finally`
                // and no `catch` for its entire life, so anything that escaped went straight to the
                // dispatcher as an unhandled exception and took the process with it, mid-sale, with
                // a full basket. It happened on 2026-08-10: entering `0` at the payment prompt is
                // refused and the tender loop asks again, and WinUI threw a COMException building
                // the second action sheet while the first popup was still tearing down
                // (`ActionSheetContent..ctor` → `UserControl..ctor`). The loop was right; the
                // absence of a catch is what turned a glitch into a closed till.
                //
                // ⚠ THE BASKET IS LEFT ALONE. If the sale committed before the fault, it is safely
                // queued and clearing would hide it; if it did not, the operator still has their
                // basket. Either way, losing it is the one outcome that cannot be undone at a
                // counter.
                Services.Analytics.CrashLog.Write("TillViewModel.Checkout", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Something went wrong taking payment. Your basket is still here — please try again.",
                    "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Throw the basket away.
        ///
        /// ⚠ ASKS FIRST. This cleared a full basket on a single tap with no confirmation and no
        /// undo — a customer's whole order, mid-transaction, from a mis-tap on a busy counter.
        ///
        /// ⚠ NOT gated on `pos.void`, deliberately. Nothing here has been paid for or committed:
        /// the sale does not exist until checkout, so this is a correction, not a void. Requiring a
        /// supervisor to undo a mis-scan would put one at the counter for the most ordinary event
        /// on a till, and the operators would find a way around it — which is worse than the gate
        /// being absent. `pos.void` belongs on voiding a RECORDED sale, which this till cannot do.
        /// </summary>
        private async void ExecuteCancelTransaction()
        {
            if (Basket.Count == 0) return;

            if (!await Application.Current.MainPage.DisplayAlert(
                    "Hmm".Translate(),
                    $"Clear this basket? {Basket.Count} line(s) will be removed and this can't be undone.",
                    "Yes".Translate(), "Cancel".Translate()))
                return;

            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Cancelled",
                new Dictionary<string, string> { { "Lines", Basket.Count.ToString() } });

            Basket.Clear();


            // ⚠ THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a


            // just-sold item being re-added — see the guard in ExecuteItemAdd.


            SelectedBasketRecord = null;
        }
        #endregion
        #endregion

        #region Operations
        private async void FinaliseTransation(SaleModel sale, decimal change)
        {
            {
                var itemHasNoStock = false;

                // ⚠ THE PER-LINE STOCK DECREMENT IS GONE (cutover step 11). It read a legacy
                // StockModel and called db.Save() ONCE PER LINE with no transaction, so a crash
                // halfway through a basket left some lines decremented and some not, with nothing
                // to reconcile against. v2 holds no local stock at all: the SERVER attributes
                // movement from `LineMeta.itemIdOne` on the sale it receives, which is one
                // authority instead of one per till.

                // ⚠ COMMIT BEFORE PRINTING, and this ordering is the whole point of the block.
                // The receipt is printed from what was COMMITTED — so a printer failure is a
                // reprint problem, never a money problem. The reverse order loses a sale that a
                // customer has already been handed a receipt for.
                var tenders = Services.Storage.CheckoutCommit.TendersFrom(
                    sale.PaySales.Select(p => ((string?)p.TempPayMethod?.Name, p.Amount, p.Change)));

                // ⚠⚠ STORE CREDIT IS SPENT BEFORE THE SALE IS RECORDED, AND A FAILURE ABORTS.
                //
                // The SERVER owns the balance, so anything it will refuse — an overdraw, a repeat —
                // has to be refused while the sale can still be abandoned. Redeeming afterwards
                // would leave a recorded sale that was never fully paid, with the customer gone and
                // nothing on the till to say so. Same order, and the same reason, as the web till
                // (`api.ts:1090`) and as FE7's gift cards.
                //
                // ⚠⚠ NOTHING IRREVERSIBLE UNTIL THE SALE IS KNOWN TO BE RECORDABLE (2026-08-19).
                // Everything below this line spends — or creates — value on the SERVER, and the commit
                // that follows can still refuse afterwards. `RefuseIfDayClosedAsync` was extracted to
                // be "ONE METHOD, EVERY DOOR" (see its remarks) and was wired to both add-to-basket
                // doors and NOT to this one, which is the door where it costs a customer money:
                //
                //   Z-close the day → ring a basket paid with store credit → the redeem SUCCEEDS →
                //   `CommitAsync` refuses at its own day-closed gate → the operator reads
                //   "Nothing has been taken" → the balance is gone and no sale exists.
                //
                // No operator error is required. ⚠ It fails OPEN on a lookup error, deliberately, so
                // hoisting it here cannot turn a bad local read into a closed shop, and the ledger's
                // own gate in `CheckoutCommit` still sits behind it — two gates, two jobs.
                if (await RefuseIfDayClosedAsync()) return;

                // ⚠⚠ WHAT HAS ALREADY MOVED, read BEFORE anything moves. The three calls below hit the
                // server in order, and every failure path used to be a bare `return` carrying the
                // sentence "Nothing has been taken — the basket is still here". That sentence is true
                // of the FIRST guard only; after it, the till was telling the operator the opposite of
                // the truth about a customer's money.
                var creditTakenPence = tenders
                    .Where(t => t.TenderType == SharedKernel.Tenders.Credit)
                    .Sum(t => t.AmountPence);
                var giftTakenPence = tenders
                    .Where(t => t.TenderType == SharedKernel.Tenders.GiftCard)
                    .Sum(t => t.AmountPence);
                var creditSpent = false;
                var giftSpent = false;

                // ⚠ Names the value that HAS moved, so the operator does not re-ring the sale and
                // charge the customer twice. ⚠⚠ It must not offer a retry: the redeem's idempotency
                // key is minted inline and discarded, so a second attempt spends the balance again.
                async Task WarnValueAlreadyTakenAsync()
                {
                    if (!creditSpent && !giftSpent) return;

                    var gone = creditSpent && giftSpent
                        ? $"{creditTakenPence / 100m:C2} of store credit and {giftTakenPence / 100m:C2} from the gift card"
                        : creditSpent
                            ? $"{creditTakenPence / 100m:C2} of store credit"
                            : $"{giftTakenPence / 100m:C2} from the gift card";

                    Services.Analytics.CrashLog.Write("TillViewModel.CheckoutAbortedAfterValueTaken",
                        new InvalidOperationException(
                            $"Checkout aborted after value had already been taken. credit={creditTakenPence} "
                            + $"gift={giftTakenPence} customer={AttachedCustomer?.Id.ToString() ?? "none"}"));

                    await Application.Current.MainPage.DisplayAlert("Money has already moved",
                        $"This sale was NOT recorded, but {gone} has already been taken in Plutus.\n\n"
                        + "Do not simply ring it again — a supervisor must put that value back first, "
                        + "or the customer pays twice.", "OK".Translate());
                }

                // ⚠ THE ONLY ONE OF THE FOUR THAT IS SAFE AS A BARE RETURN: nothing has moved yet.
                if (!await TryRedeemStoreCreditAsync(tenders)) return;
                creditSpent = creditTakenPence > 0;

                if (!await TryRedeemGiftCardAsync(tenders)) { await WarnValueAlreadyTakenAsync(); return; }
                giftSpent = giftTakenPence > 0;

                // ⚠ ACTIVATE LAST OF THE THREE, and still before the commit. A card being SOLD is
                // the only one of these that creates value rather than spending it — so if a redeem
                // above has already refused, no card has been loaded that a cancelled sale would
                // leave live in the customer's hand.
                if (!await TryActivateGiftCardsAsync()) { await WarnValueAlreadyTakenAsync(); return; }

                var outcome = await Services.Storage.CheckoutCommit.CommitAsync(
                    Basket, tenders, App.GetViewModel().SignedInOperator?.UserId);

                if (!outcome.Committed)
                {
                    // ⚠ The basket is deliberately NOT cleared. Nothing was recorded, so the sale
                    // is still there to retry — clearing it would lose the sale and the evidence.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), outcome.Message, "OK".Translate());

                    // ⚠⚠ AND `outcome.Message` SAYS "Nothing has been taken". Once a redeem above has
                    // succeeded that is false, so the truth follows it rather than replacing it — the
                    // operator needs both the reason the sale failed and the fact that money moved.
                    await WarnValueAlreadyTakenAsync();
                    return;
                }

                var tasks = new Task[3];

                var printerMgr = new PosPrinterManager();

                var trackEventArgs = new Dictionary<string, string>
                {
                    { "Canceled", "False" }
                };

                if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
                {
                    // ⚠ WHICH PRINTER ROUTE, DECIDED ONCE, BEFORE ANYTHING IS DISPATCHED.
                    //
                    // The till now prints the way the WEB till always has: it POSTs a rendered
                    // document to the Plutus Till Agent on this PC, which drives the printer
                    // through the ordinary Windows print queue. The old OPOS route stays as a
                    // fallback for tills with a genuine PointOfService device — but it is the
                    // reason Matt could not find a printer the web till uses every day, because
                    // `PointOfService` enumerates a driver profile almost no receipt printer ships
                    // and the empty picker then volunteers "Wireless is turned off".
                    //
                    // ⚠ Resolved HERE, not inside the print call, because the DRAWER decision
                    // depends on it: the agent kicks the drawer as part of the print job, so
                    // dispatching an OPOS drawer task as well would kick it twice.
                    // ⚠ Free on a till nobody has paired — `ResolveAsync` does not even probe.
                    var agent = await Services.Printing.TillAgentPrinting.ResolveAsync();

                    var wantsDrawer = TryCashDrawer
                        && sale.PaySales.Any(pay => pay.TempPayMethod.IsChangeable.Equals(true));

                    if (!AskForReceipt || await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ReceiptRequired".Translate(), "Yes".Translate(), "No".Translate()))
                    {
                        trackEventArgs.Add("Receipt Requested", "True");
                        // ⚠ Built from the COMMITTED payload, not from `sale` — the receipt states
                        // what the platform accepted, and its barcode carries the platform saleId
                        // (the legacy `sale.Id` has been empty since step 11 removed the save that
                        // assigned it, so every receipt printed a blank barcode).
                        var receipt = Services.Printing.ReceiptSale.From(
                            outcome.Request,
                            sale.PaySales.Select(p => p.TempPayMethod?.Name).ToList(),
                            // ⚠ From the BASKET, which is still intact here — `Basket.Clear()` runs
                            // at the end of this method, after the receipt has been built.
                            Services.Storage.CheckoutCommit.ReceiptNotesFrom(Basket));

                        trackEventArgs.Add("Printer Route", agent is not null ? "agent" : "opos");

                        tasks[0] = Task.Run(async () =>
                        {
                            if (agent is not null)
                            {
                                // The agent prints the receipt AND kicks the drawer in one job, so
                                // the drawer opens as the paper starts moving — as it does on the
                                // native till. ⚠ It never throws: a wedged agent or an off printer
                                // returns false, and no receipt is worth losing a committed sale.
                                var printed = await Services.Printing.TillAgentPrinting.TryPrintSaleAsync(
                                    receipt, Basket, App.GetViewModel().Store, wantsDrawer, agent);

                                // ⚠ HONESTLY, including when it did not print. This used to be
                                // added only on the success path, so a failure left the key absent
                                // and every telemetry reader had to guess what absent meant.
                                trackEventArgs["Receipt Printed Successfully"] = printed ? "True" : "False";
                                return;
                            }

                            _ = await printerMgr.InitPrinter();
                            await printerMgr.SetUpSalePrint(receipt, Basket, App.GetViewModel().Store);
                            await printerMgr.ExecuteOposOrPdfAsync();

                            trackEventArgs["Receipt Printed Successfully"] = "True";
                        });
                    }

                    if (TryCashDrawer)
                    {
                        trackEventArgs.Add("Cash Drawer Open Requested", "True");
                        if (wantsDrawer)
                        {
                            // ⚠ ONLY when the print job did not already carry it. `tasks[0]` is
                            // null when the operator declined a receipt — and a cash sale still has
                            // to open the drawer, receipt or no receipt.
                            if (agent is null)
                                tasks[1] = printerMgr.OpenCashDrawer();
                            else if (tasks[0] is null)
                                tasks[1] = Services.Printing.TillAgentPrinting.OpenDrawerAsync();

                            trackEventArgs.Add("Cash Drawer Opened Successfully", "True");
                        }
                    }
                }

                if (change != default)
                {
                    tasks[2] = Application.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("CashBack".Translate(), change), "OK".Translate());
                }

                try
                {
                    await Task.WhenAll(tasks.Where(t => t != null));
                    _ = await printerMgr.CloseConnection();
                    printerMgr.Dispose();
                }
                catch (POSObjectException pOSObjectException)
                {
                    Logger.LogError(pOSObjectException);
                    switch (pOSObjectException.POSTargetObjectType)
                    {
                        case CommonPOSLibrary.Enums.POSTargetObjectType.Printer:
                            trackEventArgs.Add("Receipt Printed Successfully", "False");
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "There was a problem with the POS Printer. Transaction has succeeded but a receipt is currently unavailable.", "OK".Translate());
                            break;
                        case CommonPOSLibrary.Enums.POSTargetObjectType.CashDrawer:
                            trackEventArgs.Remove("Cash Drawer Opened Successfully");
                            trackEventArgs.Add("Cash Drawer Opened Successfully", "False");

                            // ⚠ THE "SILENCE" BUTTON SILENCED NOTHING. This line SET
                            // `CashDrawerWarningSilenced` and **nothing anywhere read it** — the
                            // alert was raised unconditionally on every drawer failure. So an
                            // operator who pressed Silence got the same modal on the very next cash
                            // sale, and on every cash sale after that, with no way to stop it.
                            //
                            // ⚠ It matters more than a nuisance: until the missing `return` in
                            // `POSCashDrawer.InitPOSObject` was fixed (same commit), a WORKING
                            // drawer threw `NotClaimable` on its own success path — so this modal
                            // fired on every cash sale on every till, and could not be dismissed
                            // for good. The setting existed, the button existed, the guard did not.
                            if (!CashDrawerWarningSilenced)
                            {
                                CashDrawerWarningSilenced = !await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CashDrawerErrorWarning".Translate(), "OK".Translate(), "Silence".Translate());
                            }
                            break;
                    }
                }
                catch (POSPrinterException pOSPrinterException)
                {
                    if (pOSPrinterException.POSPrinterExceptionType == CommonPOSLibrary.Enums.POSPrinterExceptionType.PrinterNotClaimed)
                    {
                        Logger.LogError(pOSPrinterException);
                    }
                    trackEventArgs.Add("Receipt Printed Successfully", "False");
                    trackEventArgs.Add("Printed Not Selected", "True");
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "There is no POS Printer selected. Transaction has succeeded but a receipt is currently unavailable.", "OK".Translate());
                }
                Basket.Clear();

                // ⚠ THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a

                // just-sold item being re-added — see the guard in ExecuteItemAdd.

                SelectedBasketRecord = null;
                await Application.Current.MainPage.DisplayAlert("Transaction".Translate(), "TransConfMesg".Translate(), "OK".Translate());

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Sale Processing", trackEventArgs);

                if (!itemHasNoStock)
                {
                    return;
                }
                //put in stock warning
            }
        }

        /// <summary>
        /// The ways this till can take money (cutover step 13b).
        ///
        /// ⚠ THIS USED TO READ THE LEGACY `PaymentMethodModel` TABLE, AND THAT STOPPED A NEW TILL
        /// SELLING AT ALL. The table is seeded only by `Database.Init()` — the legacy first-run
        /// path — which a portal-provisioned till never runs, and it has no local database anyway.
        /// So the payment sheet rendered ZERO buttons and the `paid != sale.Total` loop above could
        /// never terminate: the operator was stuck in a checkout with nothing to press but Cancel,
        /// with a customer in front of them. Every dev machine hid it, because they were migrated
        /// from legacy installs that had already seeded Card and Cash.
        ///
        /// The tenders now come from the FIXED shared set (binding default 13). The legacy model is
        /// still the carrier because the rest of this checkout reads `IsChangeable`/`IsCashBackable`
        /// off it; step 11b retires the model itself. ⚠ `Charge`/`MinimumCharge` stay ZERO — the
        /// card surcharge is the TENANT's gateway setting now, not a per-row legacy field on a
        /// GLOBAL table where one client's fee would have been every client's.
        /// </summary>
        /// <summary>
        /// How the sales being returned in this basket were originally PAID.
        ///
        /// ⚠ THE UNION ACROSS EVERY RETURN LINE. A basket can hold returns against more than one
        /// sale, and if one was cash and another card the operator has to be able to settle both —
        /// the tender loop supports a split, so offering both is right and offering neither is not.
        ///
        /// ⚠ NULL WHEN NOTHING COULD BE READ, which is the honest answer for a cross-till refund or
        /// a sale older than local history. `OfferedForRefund` treats null as "offer everything"
        /// rather than blocking a legitimate refund over a fact this till does not have.
        ///
        /// ⚠ Reads THIS TILL's own record only — no network. A refund must work with the line down.
        /// </summary>
        /// <summary>
        /// Is the business day Z-closed? If so, SAY so and return true.
        ///
        /// ⚠ ONE METHOD, CALLED BY EVERY PATH THAT PUTS SOMETHING IN THE BASKET. There are two —
        /// the scan box (<see cref="ExecuteItemAdd"/>) and the item list's "Add to till"
        /// (<see cref="ExecuteItemAddArg"/>, reached by the `AddToBasket` message) — and on 1.49.2
        /// only the first one checked. Matt found the other in ten seconds (A8).
        ///
        /// ⚠ THIS PROTECTS THE OPERATOR, NOT THE BOOKS. The ledger's gate stays where it is, in
        /// `CheckoutCommit`: a till cannot be the only thing enforcing a rule about the platform's
        /// own records, because an older build or a replayed queue reaches the endpoint without
        /// passing through any screen. Two gates, two different jobs.
        ///
        /// ⚠ Fails OPEN on a lookup error, deliberately: refusing to sell because a local read threw
        /// would turn a bad day into a closed shop. The commit gate is still behind it.
        /// </summary>
        private async Task<bool> RefuseIfDayClosedAsync()
        {
            bool closed;
            try
            {
                closed = await Services.Storage.TillStoreAccess.UseAsync(
                    s => s.IsDayClosedAsync(SharedKernel.BusinessDay.Wire(SharedKernel.BusinessDay.Today())));
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillViewModel.RefuseIfDayClosed", ex);
                return false;
            }

            if (!closed) return false;

            await Application.Current.MainPage.DisplayAlert("Till closed",
                "This day has been closed with a Z read, so nothing more can be rung up against "
                + "it.\n\nIf the shop is still trading, a supervisor can reopen the day: "
                + "Cash → \"Reopen the day\".", "OK".Translate());
            return true;
        }

        /// <summary>
        /// How much may go back to each tender the origin sale used — finding Y, 2026-08-13.
        ///
        /// ⚠⚠ WHAT IT FIXES. Matt: *"when I try to return an item that was split, it wants to put the
        /// full amount to that card."* `OriginTenderTypesAsync` (below) gathers the SET of tenders and
        /// nothing more, so a £2.00 cash + £2.40 card sale offered both — correctly — and then let the
        /// whole £4.40 go on the card.
        ///
        /// ⚠ LOCAL SALES ONLY, AND THAT IS A REAL LIMIT, NOT AN OVERSIGHT. `SaleDto`
        /// (`GET /api/v1/sales/{saleId}`) carries lines and adjustments and **no tenders at all**, so a
        /// sale rung up on ANOTHER till cannot be capped per tender here. The server gate added the same
        /// day catches it (quarantine 202) — so the money is protected either way — but the operator gets
        /// a quarantine after the fact instead of a refusal at the counter. Closing it properly means
        /// adding `tenders` to that contract: captured as finding Y piece 4b.
        ///
        /// ⚠ NO PER-TENDER DEDUCTION FOR EARLIER REFUNDS, deliberately: nothing local records which
        /// tender a past refund went back to. So these caps are what each tender TOOK, and a second
        /// visit could in principle overpay one across two refunds — which is exactly the case the
        /// pooled server gate exists to refuse. The till's job here is to make the right thing easy;
        /// the platform's is to make the wrong thing impossible.
        /// </summary>
        private async Task<IReadOnlyList<SharedKernel.TenderCapacity>> OriginTenderCapacitiesAsync()
        {
            try
            {
                var originIds = Basket.OfType<BasketItem>().Where(r => r.IsReturn)
                    .Select(r => r.ReturnSaleId)
                    .Where(id => Guid.TryParse(id, out _))
                    .Select(Guid.Parse)
                    .Distinct()
                    .ToList();

                if (originIds.Count == 0) return Array.Empty<SharedKernel.TenderCapacity>();

                var took = new List<KeyValuePair<byte, long>>();
                foreach (var originId in originIds)
                {
                    var origin = await Services.Storage.TillStoreAccess.TryUseAsync(
                        s => s.FindLocalSaleAsync(originId));

                    if (origin?.Tenders is { Count: > 0 })
                    {
                        foreach (var tender in origin.Tenders)
                            took.Add(new KeyValuePair<byte, long>(tender.TenderType, tender.AmountPence));
                        continue;
                    }

                    // ⚠ NOT OURS — ASK THE PLATFORM (piece 4b). A sale rung up on another till is not
                    // in this store, and that is the case a cap matters MOST in: the operator has no
                    // receipt knowledge to fall back on. The server has always sent the tenders; until
                    // today nothing read them.
                    //
                    // ⚠ Silence here is not an error. No operator token, no network, or a sale the
                    // platform does not have all mean "no capacities", which means no caps — the
                    // behaviour before this existed, with the ingest gate still behind it.
                    var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                    if (api is null) continue;

                    var dto = await api.GetSaleAsync(originId);
                    if (dto is null) continue;

                    took.AddRange(Plutus.Client.Core.SaleDtoTenders.TenderPairs(dto));
                }

                return SharedKernel.RefundRules.RefundCapacities(took);
            }
            catch (Exception ex)
            {
                // ⚠ NEVER BLOCK A REFUND OVER THIS — the same rule as the tender SET below. No
                // capacities means no caps, and the server gate is still behind it.
                Services.Analytics.CrashLog.Write("TillViewModel.OriginTenderCapacities", ex);
                return Array.Empty<SharedKernel.TenderCapacity>();
            }
        }

        private async Task<IReadOnlyCollection<byte>> OriginTenderTypesAsync()
        {
            try
            {
                var originIds = Basket.OfType<BasketItem>().Where(r => r.IsReturn)
                    .Select(r => r.ReturnSaleId)
                    .Where(id => Guid.TryParse(id, out _))
                    .Select(Guid.Parse)
                    .Distinct()
                    .ToList();

                if (originIds.Count == 0) return null;

                var types = new HashSet<byte>();
                foreach (var originId in originIds)
                {
                    var origin = await Services.Storage.TillStoreAccess.TryUseAsync(
                        s => s.FindLocalSaleAsync(originId));

                    if (origin?.Tenders is null) continue;

                    foreach (var tender in origin.Tenders) types.Add(tender.TenderType);
                }

                return types.Count > 0 ? types : null;
            }
            catch (Exception ex)
            {
                // ⚠ NEVER BLOCK A REFUND OVER THIS. If the lookup fails the operator gets the full
                // sheet, which is exactly where we were before the restriction existed.
                Services.Analytics.CrashLog.Write("TillViewModel.OriginTenders", ex);
                return null;
            }
        }

        /// <param name="refundToTenderTypes">⚠ On a refund-only basket, the tenders the ORIGIN sale
        /// used — so the money goes back the way it came. Null means "offer everything": either this
        /// is not a refund, or the origin is not on this till and the restriction cannot be
        /// applied. See <see cref="Services.Sales.TillTenders.OfferedForRefund"/>.</param>
        /// <summary>
        /// Take the money on ONE screen — §5c item 2, 2026-08-19.
        ///
        /// ⚠⚠ THIS METHOD DECIDES NOTHING ABOUT MONEY. It builds the rows, shows the screen, and hands
        /// back what `Client.Core.TenderSettlement` settled — the C2 twin of the web till's
        /// `tendering.ts`, pinned on both sides and mutation-checked. Anything here that looks like
        /// arithmetic is a bug.
        ///
        /// ⚠ It returns a <see cref="Plutus.Client.Core.TenderOutcome"/>, the same shape the sequential
        /// loop returned, so **everything downstream — the sale model, the drawer, the receipt, the
        /// commit — is untouched by this change.** Replacing how the money is ASKED FOR should not
        /// reach the code that records it.
        ///
        /// ⚠ The gift-card button closes the screen, scans, and REOPENS it. MAUI cannot stack two
        /// Mopups pages sensibly — the second lands behind the first, which reads as a frozen till —
        /// so this is the same close-then-reopen dance `CustomerDetailHelper` documents.
        /// </summary>
        private async Task<Plutus.Client.Core.TenderOutcome> TakeTendersOnOneScreenAsync(
            SaleModel sale,
            Dictionary<string, Func<PaymentMethodModel>> payMeths,
            IReadOnlyList<SharedKernel.TenderCapacity> refundCaps,
            bool refundOnly)
        {
            // ⚠ Cached so every row reuses the SAME `PaymentMethodModel` instance, as the loop did —
            // the sale's payment rows point at these, and re-running the factory per row would give
            // two payments on one method two different objects.
            var models = payMeths.ToDictionary(kv => kv.Key, kv => kv.Value());

            // ⚠ ONE round trip for both. WP14's card display and the surcharge come off the SAME
            // `GET /api/v1/payments/gateway/active` the checkout already made — see `GatewaySettings`,
            // which used to keep two of the answer's five fields and throw the other three away.
            var gateway = await Services.Storage.GatewaySettings.GetAsync();
            var (surchargeBp, surchargeFlat) = (gateway.Bp, gateway.FlatPence);

            while (true)
            {
                var rows = payMeths.Keys.Select(name =>
                {
                    var type = SharedKernel.Tenders.FromMethodName(name);

                    // ⚠ ONE RULE ABOUT HOW MUCH A TENDER MAY TAKE, three callers of it — the same
                    // three the sequential loop had. A cap here is what stops "rest" offering a number
                    // the redeem would refuse (Matt, 2026-08-19: *"There is No point putting the full
                    // number in"*), and on a refund it is finding Y's per-tender capacity.
                    long? cap = null;
                    if (refundOnly) cap = SharedKernel.RefundRules.CapacityFor(refundCaps, type);
                    else if (type == SharedKernel.Tenders.Credit) cap = CreditAvailablePence;
                    else if (type == SharedKernel.Tenders.GiftCard) cap = GiftCardAvailablePence;

                    return new Views.CustomViews.CheckoutAlert.Row
                    {
                        // ⚠ A STABLE ID PER METHOD NAME. The settlement keys on it, the change map
                        // keys on it, and the payment rows are ordered by it — so it must not shuffle
                        // between one opening of this screen and the next.
                        PayId = System.Array.IndexOf(payMeths.Keys.ToArray(), name),
                        Name = name,
                        IsChangeable = models[name].IsChangeable,
                        CapPence = cap,

                        // ⚠ SAY THE CEILING, as the web till does — a number the operator cannot use
                        // should never be a number they have to discover by being refused.
                        Hint = cap is long c ? $"(up to {(c / 100m):C2})" : null,
                    };
                }).ToList();

                var alert = await ShowCheckoutAsync(sale, rows, refundOnly, surchargeBp, surchargeFlat, gateway.Card);

                // ⚠ THREE OUTCOMES, NOT TWO. "They want the gift-card box" is not a cancel: the basket
                // stays, the card is scanned, and the screen reopens with a gift-card row on it.
                if (alert.WantsGiftCard)
                {
                    // ⚠ The EXISTING present-a-card path, unchanged — `TryPresentGiftCardAsync` talks
                    // to the server, which owns the balance, and already refuses a card this sale may
                    // not take. This only asks for the code.
                    await PromptForGiftCardAsync();

                    // ⚠ Rebuilt from `payMeths` next time round the loop, because attaching a card is
                    // what makes the gift-card tender appear at all (`TillTenders.Offered`).
                    payMeths = GenPaymentMethodActions(refundOnly ? await OriginTenderTypesAsync() : null);
                    models = payMeths.ToDictionary(kv => kv.Key, kv => kv.Value());
                    continue;
                }

                if (alert.IsAbandoned) return Plutus.Client.Core.TenderOutcome.GaveUp();

                var payments = alert.Payments
                    .Select(p => new Plutus.Client.Core.TenderPayment(
                        rows.First(r => r.Name == p.MethodName).Name, p.AmountPence, p.ChangePence))
                    .ToList();

                return new Plutus.Client.Core.TenderOutcome(
                    false, payments, alert.ChangePence, 0,
                    // ⚠ Straight off the basket in pence, not `Pence.FromDecimal(sale.Total)` —
                    // one derivation, and the one `CheckoutCommit` reconciles against.
                    Services.Storage.CheckoutCommit.BasketMoneyPence(Basket));
            }
        }

        /// <summary>
        /// A code the catalogue does not know, that IS a member number — attach that member (WP-T2).
        ///
        /// ⚠ Returns true when it handled the code, so the caller stops rather than also offering to
        /// create an item with it.
        ///
        /// ⚠⚠ IT ASKS FIRST. A six-digit code that is both a plausible member number and a genuine
        /// mistype is common, and silently attaching a stranger to somebody's sale — with their
        /// discount and their credit on offer — is a worse failure than one extra tap. The web till's
        /// twin asks in the same words.
        ///
        /// ⚠ `TryCanonicalise` NORMALISES as well as validating (it is what turns `482` into the full
        /// number with its check character), so the lookup runs on its answer, never on the raw text.
        ///
        /// ⚠ A member number that canonicalises but matches NOBODY falls through to the item offer —
        /// the code was not a member after all, and saying "no such member" would be a dead end where
        /// "shall I add this item?" is a route forward.
        /// </summary>
        private async Task<bool> TryAttachTypedMemberNumberAsync(string typed)
        {
            if (SharedKernel.MemberNumbers.TryCanonicalise(typed) is not string memberNo) return false;

            var confirmed = await Application.Current.MainPage.DisplayAlert(
                "Member number?".Translate(),
                string.Format(
                    "Nothing in the catalogue matches “{0}”, but it looks like member number {1}. "
                    + "Attach that member to this sale?".Translate(),
                    typed, memberNo),
                "Attach".Translate(),
                "Cancel".Translate());

            if (!confirmed) return false;

            // ⚠ THE SAME ATTACH PATH A SCAN USES — it searches, disambiguates and reports "no match"
            // in one place. A second attach route is how two ways of doing one thing come to disagree.
            await SearchAndAttachAsync(memberNo);
            return true;
        }

        /// <summary>
        /// Ask for a gift-card code, then present it — the checkout's "🎁 Pay with a gift card" button.
        ///
        /// ⚠ Matt, 2026-08-19: *"can the Giftcare, go below and say 'Pay with Gift Card' button, which
        /// then pops the box to scan. There is no point showing it all, unless you ahve a card."*
        ///
        /// ⚠⚠ D4 RULE 4 — backing out yields an EMPTY DICTIONARY, not null, so this guards on
        /// `TryGetValue` failing. A `== null` test here would be dead code that never fires, and
        /// reading an absent key throws `KeyNotFoundException` from an `async` UI path.
        ///
        /// ⚠ It does not decide anything about the card. `TryPresentGiftCardAsync` asks the server,
        /// which owns the balance, and refuses a card this sale may not take.
        /// </summary>
        private async Task PromptForGiftCardAsync()
        {
            var viewElements = new[]
            {
                new ViewElementData(1, "Gift card code", string.Empty,
                    new IValidator[] { new RequiredValidator() }.AsEnumerable(),
                    isPassword: false, isEnabled: true, prefillWithPlaceholder: false),
            };

            var typed = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                viewElements, "Check card", false, "Pay with a gift card", "Cancel".Translate());

            if (!typed.TryGetValue(1, out var code) || string.IsNullOrWhiteSpace(code)) return;

            await TryPresentGiftCardAsync(code.Trim());
        }

        /// <summary>
        /// Show the screen once, applying the CARD FEE live while it is open.
        ///
        /// ⚠⚠ THE FEE IS A BASKET LINE, so it is taxed, printed and reported like anything else — and
        /// so it must go into `Basket`, which is bound to the till screen. The screen therefore says
        /// *"a card is being used now"* and this adds or removes the line and restates the total.
        ///
        /// ⚠ ON THE UI THREAD. `Basket` is an `ObservableCollection` a `CollectionView` is bound to.
        ///
        /// ⚠ THE FEE COMES OFF AGAIN when the card row is cleared. The sequential loop could only ever
        /// add it — pick card, change your mind, and the fee stayed on the basket. On one screen the
        /// operator can see it appear, so it has to be able to disappear.
        /// </summary>
        private async Task<Helpers.CustomViews.CheckoutHelper.Result> ShowCheckoutAsync(
            SaleModel sale,
            IReadOnlyList<Views.CustomViews.CheckoutAlert.Row> rows,
            bool refundOnly,
            int surchargeBp,
            long surchargeFlat,
            Plutus.Client.Core.CardPaymentDisplay card)
        {
            var note = refundOnly
                ? "↩ This basket returns more than it sells, so it is a refund: enter how much goes "
                  + "back on each method. The sale is recorded with negative totals."
                : null;

            var body = new Views.CustomViews.CheckoutAlert(
                // ⚠ Pence off the basket, never `Pence.FromDecimal(sale.Total)` — see `SaleIncTax`.
                Services.Storage.CheckoutCommit.BasketMoneyPence(Basket),
                rows,
                note,
                // ⚠ Not on a refund, not when a card is already held (its row is on screen saying what
                // it holds), and not when the basket SELLS a card — paying with one in that sale
                // would launder an expiring balance onto a fresh card. The same three cases the web
                // till suppresses the button in.
                offerGiftCard: !refundOnly
                    && GiftCardAvailablePence == 0
                    && !Basket.Any(r => r is BasketItem b
                        && string.Equals(b.Item?.Id, SharedKernel.GiftCards.ItemIdOne,
                                         StringComparison.OrdinalIgnoreCase)),
                card);

            body.CardTenderedChanged += (_, cardNow) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var had = Services.Storage.CheckoutCommit.HasSurcharge(Basket);

                    if (cardNow && !had)
                    {
                        var feeLine = Services.Storage.CheckoutCommit.SurchargeItem(
                            Basket, surchargeBp, surchargeFlat);

                        // ⚠ Null when the tenant charges no fee, which is the common case — then
                        // nothing is added and nothing is said.
                        if (feeLine == null) return;
                        Basket.Add(feeLine);
                    }
                    else if (!cardNow && had)
                    {
                        var feeLine = Basket.FirstOrDefault(
                            b => Services.Storage.CheckoutCommit.HasSurcharge(new[] { b }));
                        if (feeLine == null) return;
                        Basket.Remove(feeLine);
                    }
                    else
                    {
                        return;
                    }

                    // ⚠⚠ ONE SUM, IN PENCE, AND THIS SITE IS WHY IT MATTERS. It used to hand
                    // `Pence.FromDecimal(sale.Total)` to the screen — rounding the SUM rather than the
                    // lines — while `CheckoutCommit` guarded the commit with a per-record pence sum.
                    // `BasketMoneyPence`'s own header forbids exactly that conversion, and the number
                    // it produced is the one the operator tenders against. A penny apart and the
                    // commit refuses a sale the operator has already taken money for on screen.
                    var totalPence = Services.Storage.CheckoutCommit.BasketMoneyPence(Basket);
                    sale.Total = totalPence / 100m;
                    sale.TotalExTax = Services.Storage.CheckoutCommit.BasketMoneyExPence(Basket) / 100m;

                    var feePence = Basket
                        .Where(b => Services.Storage.CheckoutCommit.HasSurcharge(new[] { b }))
                        .Sum(b => b.PricePence * b.Quantity);

                    // ⚠⚠ SAY THE FEE, ITEMISED, BEFORE the sale completes. A total that jumps when the
                    // card row is filled and explains nothing is the surcharge complaint every time.
                    body.SetTotal(
                        totalPence,
                        feePence > 0
                            ? $"💳 Card fee {feePence / 100m:C2} added — it comes off if the card row is cleared."
                            : null);
                });
            };

            return await Helpers.CustomViews.CheckoutHelper.ShowAsync(body);
        }

        private Dictionary<string, Func<PaymentMethodModel>> GenPaymentMethodActions(
            IReadOnlyCollection<byte> refundToTenderTypes = null)
        {
            var refundOnly = !Basket.Any(bR => bR is BasketItem && !bR.IsReturn);

            var offered = refundOnly
                ? Services.Sales.TillTenders.OfferedForRefund(refundToTenderTypes)
                // ⚠ The attached customer's LIVE balance, read at attach. Zero when nobody is
                // attached — which is what keeps the button off the sheet unless it can be spent.
                : Services.Sales.TillTenders.Offered(false, CreditAvailablePence, GiftCardAvailablePence);

            return offered.ToDictionary<
                Services.Sales.TillTender, string, Func<PaymentMethodModel>>(
                t => t.Name,
                t => () => new PaymentMethodModel
                {
                    Name = t.Name,
                    IsChangeable = t.GivesChange,
                    IsCashBackable = t.GivesCashback,
                    Charge = 0m,
                    MinimumCharge = 0m,
                });
        }

        /// <summary>
        /// Resolve a scanned or typed code against the V2 CATALOGUE (cutover step 10).
        ///
        /// ⚠ THIS FIXES TWO LIVE DEFECTS, not just a data source.
        ///   1. `Database.SearchId` matched on `Id` alone and **did not honour tombstones**, so an
        ///      item the portal had BINNED was still sellable on this till — indefinitely, because
        ///      nothing local ever learned it had gone. `FindByBarcodeAsync` excludes `Removed`,
        ///      which is precisely why the changes feed carries tombstones rather than upserts.
        ///   2. The price came from a stored column, so a price scheduled for 02:00 only applied if
        ///      a sync happened to land after it. It now comes from `EffectivePricePairAsync`,
        ///      evaluated at LOOKUP time against the effective-dated timeline — the change lands on
        ///      the minute on a till that has been offline for a week.
        ///
        /// ⚠ IT STILL RETURNS AN `ItemModel`, AND THAT IS TEMPORARY SCAFFOLDING. The basket holds
        /// `BasketItem.Item` as a legacy entity, and reshaping that means reshaping
        /// `BasketReturnItem`, ~14 call sites, both Mapster configs and the template selector — all
        /// of which have to land WITH `CommitSaleAsync` in step 11, or the till would build v2
        /// baskets and still save legacy sales, which is a worse half-state than either end.
        /// Only Id/Name/Price/ExPrice/Vat.Name are ever read off this object (verified by grep), so
        /// nothing needs the navigations the legacy query used to Include.
        /// **Step 11 deletes this projection.**
        /// </summary>
        /// <summary>How many matches an operator is offered before being asked to narrow it down.</summary>
        private const int SearchPickerLimit = 25;

        /// <summary>
        /// The outcome of asking for an item. ⚠ "Nothing matched" and "the operator changed their
        /// mind" are DIFFERENT and must not share a return value: telling somebody who just pressed
        /// Cancel that the item does not exist is how a working catalogue gets reported as broken.
        /// </summary>
        /// <param name="CategoryId">⚠⚠ THE PLATFORM CATEGORY, AND IT HAS TO TRAVEL SEPARATELY. The
        /// legacy <see cref="ItemModel"/> this basket carries has an <b>int</b> `CatId` pointing at the
        /// NatApp `Categories` table, which is EMPTY and permanently so on a portal till — so there is
        /// nowhere on that model to put the v2 catalogue's Guid. Without this field a category-targeted
        /// rule would silently match nothing on this till while working perfectly on the web till:
        /// exactly the shape of the Gold-member money difference (retrofit step 27), found the same
        /// way.</param>
        /// <param name="ScannedBarcode">⚠ The code the operator actually scanned, when it was NOT the
        /// item's own (multi-barcode, 2026-08-20). A snapshot for the day a supplier's barcode
        /// migration goes wrong — never an identity. Null on every ordinary lookup.</param>
        private sealed record ItemLookup(
            // ⚠ `TillItem` since L5/L6 (2026-08-23) — this lookup already read the v2 store; only
            // the shape it hands back has changed.
            Models.TillItem Item, bool Cancelled, Guid? CategoryId = null, string ScannedBarcode = null)
        {
            public static readonly ItemLookup NotFound = new(null, false);
            public static readonly ItemLookup Abandoned = new(null, true);
        }

        /// <summary>
        /// Turn what is in the scan box into an item — by BARCODE first, then by NAME.
        ///
        /// ⚠ SEARCHING BY NAME DID NOT EXIST HERE, and its absence read as an empty catalogue.
        /// This method called `FindByBarcodeAsync` and nothing else, so anything an operator TYPED
        /// — "BAT" — was tried as an exact barcode, missed, and produced *"We can't find an item
        /// with that ID"*. With 20,000 items synced and sellable. The message even said "that ID",
        /// which was accurate and completely misleading: it was never searching.
        ///
        /// ⚠ `TillStore.SearchAsync` had been built, correct and tested since 2026-08-09 — and was
        /// called from NOWHERE in the app. That is the third time a finished component has sat
        /// unwired behind a screen that looked broken (the outbox drain, the catalogue browse, this)
        /// and it is worth naming as a pattern: a test suite proves a component works, never that
        /// anything uses it.
        ///
        /// ⚠ BARCODE FIRST, ALWAYS. A scan is the hot path and must stay exact and instant; a real
        /// barcode that happens to appear inside another item's name must never open a picker in
        /// front of a queue.
        /// </summary>
        private async Task<ItemLookup> FindItem(string needle = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(needle)) return ItemLookup.NotFound;

                var typed = needle.Trim();

                var found = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.FindByBarcodeAsync(typed));

                // Not a code this till holds — so it was typed. Search names.
                if (found == null)
                {
                    var chosen = await SearchForOneAsync(typed);
                    if (chosen.Cancelled) return ItemLookup.Abandoned;
                    found = chosen.Item;
                }

                if (found == null) return ItemLookup.NotFound;

                var price = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.EffectivePricePairAsync(found.Id));

                // The band's display name, for the Tax column. ⚠ Null is a legitimate answer — the
                // portal may not have decided which band this tax row means — and it must render as
                // blank rather than being guessed at.
                var bandName = await Services.Storage.VatBands.DisplayNameForItemAsync(found.Id);

                // ⚠⚠ THIS WAS THE WHOLE REASON THE BASKET STILL SPOKE LEGACY. Everything above reads
                // the **v2 store**; this line then packed the answer into a `Database.Models.ItemModel`
                // purely because `BasketItem` demanded one. The legacy entity was a costume v2 data
                // wore, and it pinned `Helpers/Database` and the 55-file `Plutus/Data/Database`
                // project (L5, L6) behind a basket that never actually read either.
                return new ItemLookup(new Models.TillItem
                {
                    // ⚠ IdOne, not the GUID: every screen and the basket key on this string, and it
                    // IS the barcode.
                    Id = found.IdOne,
                    Name = found.Name,
                    // ⚠ Pence → decimal pounds only because the carrier is decimal. Deliberately
                    // inline rather than a SharedKernel helper: money is integer pence end-to-end
                    // (architecture §4.1) and a shared pence→decimal converter would legitimise the
                    // conversion everywhere instead of confining it to this one seam.
                    Price = price.IncPence / 100m,
                    ExPrice = price.ExPence / 100m,
                    VatName = bandName ?? string.Empty,
                    // ⚠ The v2 catalogue's category rides alongside, not on this model — see
                    // `ItemLookup.CategoryId` for why it cannot go on `CatId`.
                    //
                    // ⚠⚠ MULTI-BARCODE: `found.IdOne` is the CANONICAL code even when an ADDITIONAL
                    // one was scanned — `FindByBarcodeAsync` resolves the alias and hands back the
                    // item. The scanned string is recorded beside it and goes no further than the
                    // sale line's metadata (plan D2).
                }, false, found.CategoryId,
                    string.Equals(typed, found.IdOne, StringComparison.Ordinal) ? null : typed);
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillViewModel.FindItem", ex);
                return ItemLookup.NotFound;
            }
        }

        /// <summary>
        /// Nothing matched what was scanned — offer to put it in the catalogue (cutover step 25).
        ///
        /// ⚠ THE BARCODE TRAVELS WITH THE OFFER, which is the point. Reading a code off a packet
        /// and typing it in again is the step where a digit gets dropped, and the result is a second
        /// item nothing will ever scan to — invisible on the shelf, invisible in stock, and only
        /// discovered when the real one is added later and the insert is refused.
        ///
        /// ⚠ IT IS AN OFFER, NOT AN AUTOMATIC JUMP. A mistyped search is far commoner than a new
        /// product, and a screen that leaps into "create item" every time someone fat-fingers the
        /// scan box is a screen people learn to fight.
        ///
        /// ⚠ Adding is REFUSED POLITELY without `portal.prices.manage` — a cashier scanning an
        /// unknown code should be told the item is not in the catalogue, not offered a door that
        /// closes in their face. `TillGate` supplies the wording.
        /// </summary>
        private async Task OfferToAddUnknownAsync(string barcode)
        {
            var typed = (barcode ?? "").Trim();

            // ⚠⚠ WP-T2 — A BARE MEMBER NUMBER, TYPED, ATTACHES ITS MEMBER (2026-08-19).
            //
            // Matt, testing 1.101.0: *"How do I get the credit though? I have people with credit. But
            // there is no way to select them?"* Typing a member number returned *"We can't find an item
            // with that ID"* — a missing `C` prefix reported as a broken scanner.
            //
            // ⚠⚠ WHY IT IS SAFE HERE AND WOULD NOT BE ON THE SCAN ROUTER. `LooksLikeMemberScan` is
            // strict on purpose: it demands the `C` prefix so a six-digit PRODUCT barcode can never be
            // hijacked into a customer lookup. `TryCanonicalise` is the loose one — it accepts a bare
            // `482` for somebody reading a card down the phone — and it runs **only after the item
            // lookup has already failed**, so the collision the strict test guards against is
            // impossible by construction: a real product barcode would have been found.
            //
            // ⚠ THE STRICT TEST STAYS. It is what protects the scan path, and it is mutation-checked.
            // This adds a second, later door; it does not widen the first one.
            //
            // ⚠ Before the offer to CREATE an item, because "that is a member number" is a better
            // answer than "shall I add it to the catalogue?" — and creating an item called `482` is
            // exactly the ghost barcode that offer exists to prevent.
            if (await TryAttachTypedMemberNumberAsync(typed)) return;

            var allowed = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PortalPricesManage).Allowed;

            if (!allowed || string.IsNullOrWhiteSpace(typed))
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                return;
            }

            const string add = "Add it to the catalogue…";

            // ⚠ Through `Modal`, because saying yes leads straight into a run of further dialogs,
            // and two modals in quick succession is what threw the COMException that closed the
            // till at the payment prompt.
            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                    $"Nothing in the catalogue matches “{typed}”.", "Cancel".Translate(), null, add));

            if (picked != add) return;

            // ⚠ The inventory viewmodel owns item creation, and it is reached directly rather than
            // duplicated here — the barcode check, the band list, the ex-price derivation and the
            // opening-stock call are one flow, and a second copy on the till screen would be the
            // exact drift `till-design.md` C2 exists to prevent. It re-syncs the catalogue when it
            // finishes, so the operator can scan the item again immediately.
            new Inventory.Items.ViewAllViewModel().ExecuteCreateItem(typed);
        }

        /// <summary>
        /// Name search, and the choice that follows when more than one thing matches.
        ///
        /// ⚠ MATCHING IS NOT DECIDED HERE. `TillStore.SearchAsync` runs `SharedKernel.ItemSearch`,
        /// the single home for what a typed query finds (till-design C1). A `LIKE` written in this
        /// file would be a fourth copy of a rule that was deliberately reduced to one, and the
        /// symptom of drift is two tills in the same shop disagreeing about the same query.
        ///
        /// ⚠ One match is added WITHOUT a prompt. Somebody typing a distinctive title wants the
        /// item, not a confirmation step, and a picker containing one row is a keystroke tax paid on
        /// every sale.
        /// </summary>
        /// <summary>
        /// Find a sale the PLATFORM holds — from any till (WP11 / cutover step 26).
        ///
        /// ⚠ THIS IS WHAT MAKES A CROSS-TILL REFUND REACHABLE. Goods bought at another branch exist
        /// only on the platform, and until now the only way to name one was typing a UUID off a
        /// receipt — so in practice they were not refundable at all unless the customer still had a
        /// printed receipt AND somebody was willing to type 36 characters.
        ///
        /// ⚠ REFUNDS ARE EXCLUDED, exactly as they are in the local picker. A refund is itself a sale
        /// with a negative gross; offering one defeats the cap entirely, which cost £13.99 twice on
        /// 2026-08-10.
        ///
        /// ⚠ Needs an OPERATOR token and a connection, and says so plainly when it has neither. This
        /// is the one refund path that genuinely cannot work offline — the local list is what covers
        /// that case.
        /// </summary>
        /// <returns>The chosen sale id, or null if the operator backed out or there was nothing.</returns>
        private async Task<string> PickPlatformSaleAsync()
        {
            var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Looking up another till's sale needs someone signed in and a connection to "
                    + "Plutus. This till's own sales are in the previous list.", "OK".Translate());
                return null;
            }

            // ⚠ A FORTNIGHT, not "everything". Refunds are overwhelmingly recent, and a picker
            // holding months of sales is one nobody reads — the server clamps `take` at 500 anyway.
            var today = SharedKernel.BusinessDay.Today();
            var sales = await api.GetSalesAsync(today.AddDays(-14), today, tillId: null, take: 40);

            var purchases = sales?.Where(s => s.GrossPence > 0).ToList();
            if (purchases is null || purchases.Count == 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    sales is null
                        ? "Plutus couldn't be reached, so other tills' sales can't be listed."
                        : "Plutus has no sales in the last fortnight to refund against.",
                    "OK".Translate());
                return null;
            }

            var thisTill = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.GetGuidMetaAsync(Plutus.Client.Storage.MetaKeys.TillId));

            var labels = purchases
                .Select(s => $"{s.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} · {s.GrossPence / 100m:C}"
                           // ⚠ Says WHOSE sale it is. Without it the operator cannot tell a
                           // neighbouring till's sale from one of their own, which is the entire
                           // question this list exists to answer.
                           + (thisTill is Guid t && s.TillId == t ? " · this till" : " · another till")
                           + (string.Equals(s.Channel, "Till", StringComparison.OrdinalIgnoreCase)
                               ? "" : $" · {s.Channel}"))
                .ToList();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                    "Which sale, from Plutus?", "Cancel".Translate(), null, labels.ToArray()));

            if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate()) return null;

            var index = labels.IndexOf(picked);
            return index >= 0 && index < purchases.Count ? purchases[index].Id.ToString("D") : null;
        }

        private async Task<SearchChoice> SearchForOneAsync(string typed)
        {
            // ⚠ Ask for one MORE than we will show, so "there are others" is known rather than
            // guessed. A silently truncated list reads as a complete one, and the operator concludes
            // the item is not in stock.
            var matches = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.SearchAsync(typed, new ViewModels.Settings().MatchAllWordsSetting, SearchPickerLimit + 1));

            if (matches == null || matches.Count == 0) return SearchChoice.Nothing;
            if (matches.Count == 1) return new SearchChoice(matches[0], false);

            var shown = matches.Take(SearchPickerLimit).ToList();

            // ⚠ The barcode is in the label because it is the only field guaranteed UNIQUE.
            // `DisplayActionSheet` hands back the chosen STRING, so two items sharing a name and
            // price would be indistinguishable and the first would always win — quietly ringing up
            // the wrong variant.
            var choices = shown
                .Select(m => $"{m.Name} · {m.IdOne} · {(m.PricePence / 100m):C}")
                .ToArray();

            var title = matches.Count > SearchPickerLimit
                ? $"Showing the first {SearchPickerLimit} matches — type more to narrow it down"
                : $"{shown.Count} matches for \"{typed}\"";

            var picked = await Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                title, "Cancel".Translate(), null, choices);

            // ⚠ Cancel — and dismissing by tapping away, which returns null — is ABANDONED, not
            // NOT-FOUND. The two produce different messages and only one of them is a lie.
            if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate())
                return SearchChoice.Abandoned;

            var index = Array.IndexOf(choices, picked);
            return index < 0 ? SearchChoice.Abandoned : new SearchChoice(shown[index], false);
        }

        /// <summary>What the name search settled on: an item, nothing, or a change of mind.</summary>
        private sealed record SearchChoice(Plutus.Client.Storage.CatalogueItem Item, bool Cancelled)
        {
            public static readonly SearchChoice Nothing = new(null, false);
            public static readonly SearchChoice Abandoned = new(null, true);
        }
        #endregion

        #region INotifyPropertyChanged
        private void BasketRecordOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Basket));
            OnPropertyChanged(nameof(SaleExTax));
            OnPropertyChanged(nameof(SaleIncTax));
        }
        #endregion
    }
}
