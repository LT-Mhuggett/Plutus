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
        /// ÃÂ¢ÃÂÃÂ  CLAMPED TO AT LEAST 1, IN THE SETTER. This used to be a Syncfusion `SfNumericEntry`
        /// whose `Minimum="1"` did the clamping, so the rule lived in a XAML attribute on a control
        /// the till no longer uses (Matt, 2026-08-10: *"I am not going to renew Syncfusion"*). A
        /// plain `Entry` will happily hand over 0, or ÃÂ¢ÃÂÃÂ3, and `IncrementQuantity(0)` adds a line
        /// that charges nothing while looking exactly like a sale.
        /// ÃÂ¢ÃÂÃÂ  A rule that lives in a control's markup is a rule that leaves with the control.
        /// </summary>
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value < 1 ? 1 : value);
        }

        /// <summary>The ÃÂ¢ÃÂÃÂ and + either side of the quantity box, which is how a touch till changes
        /// it. ÃÂ¢ÃÂÃÂ  The decrement cannot go below 1: the setter refuses, so the button is safe to
        /// press repeatedly.</summary>
        Command _quantityUpCommand;
        public Command QuantityUpCommand => _quantityUpCommand ??= new Command(() => Quantity += 1);

        Command _quantityDownCommand;
        public Command QuantityDownCommand => _quantityDownCommand ??= new Command(() => Quantity -= 1);
        public ObservableCollection<SavedTransactionModel> StoredTransactions { get; } = new ObservableCollection<SavedTransactionModel>();
        public ObservableCollection<IBasketRecord> Basket { get; } = new ObservableCollection<IBasketRecord>();
        public IBasketRecord SelectedBasketRecord
        {
            get => _selectedBasketRecord;
            set => SetProperty(ref _selectedBasketRecord, value);
        }
        public decimal SaleExTax => Basket.Sum(bR => bR.PriceExTax * (bR.IsReturn ? -1 : 1) * bR.Quantity);

        public decimal SaleIncTax => Basket.Sum(bR => bR.Price * (bR.IsReturn ? -1 : 1) * bR.Quantity);
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
        /// ÃÂ¢ÃÂÃÂ  VESTIGIAL, and kept only so the removal is visible. It drove `SfPicker.IsOpen`; the
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

            // ÃÂ¢ÃÂÃÂ  THE BASKET OWNS THE CATALOGUE SYNC. A sync mid-basket rewrites prices under the
            // operator's hands: a line added before the tick and one added after would come from
            // different price lists, in one sale, and the receipt would be the only evidence it
            // happened. The heartbeat and the outbox drain are NOT held ÃÂ¢ÃÂÃÂ neither touches the
            // catalogue, and a queued sale should not wait for a customer to finish paying.
            Services.Sync.TillCadence.BasketIsOpen = () => Basket.Count > 0;

            #region Events
            StoredTransactions.CollectionChanged += (sender, e) =>
            {
                if (StoredTransactions.Count == 0)
                {
                    var toolbarItem = Shell.Current.CurrentPage.ToolbarItems.FirstOrDefault(tI => tI.Text.Equals("Baskets".Translate()));
                    if (toolbarItem == null) return;

                    Shell.Current.CurrentPage.ToolbarItems.Remove(toolbarItem);
                    App.GetViewModel().ToolbarItemsChanged = true;
                }
                else
                {
                    if (Shell.Current.CurrentPage.ToolbarItems.Any(tI => tI.Text.Equals("Baskets".Translate()))) return;

                    Shell.Current.CurrentPage.ToolbarItems.Insert(0, new IconToolbarItem
                    {
                        Text = "Baskets".Translate(),
                        IconImageSource = "md-shopping-basket",
                        IconColor = Colors.White,
                        Command = RetrieveTransactionCommand,
                        IsVisible = true
                    });
                    App.GetViewModel().ToolbarItemsChanged = true;
                }
            };
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
                // ⚠ RE-ENTRANT BY CONSTRUCTION: the refresh adds and removes a `BasketAlteration`,
                // which fires this very handler. `RefreshMemberDiscount` guards on a flag and is a
                // no-op while it is running.
                RefreshMemberDiscount();
            };
            Alterations.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(Alterations));
                OnPropertyChanged(nameof(AlterationNames));
            };
            #endregion
            // ÃÂ¢ÃÂÃÂ  OFF THE UI THREAD, and off the legacy database (cutover step 18). This opened a
            // SQLite connection and read it SYNCHRONOUSLY inside `BeginInvokeOnMainThread` ÃÂ¢ÃÂÃÂ i.e.
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

            MessagingCenter.Subscribe<Inventory.Items.ViewAllViewModel, string>(this, "AddToBasket", (sender, arg) =>
            {
                ExecuteItemAddArg(arg);
            });

            #endregion
            IsDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
            Quantity = 1;
        }

        #region Commands
        #region Items
        #region Adding
        #region Manual
        private Command _manualAddCommand;

        public Command ManualAddCommand => _manualAddCommand ?? (_manualAddCommand = new Command(ExecuteItemAdd, () => !string.IsNullOrEmpty(ItemId)));

        #endregion
        #region Manual with arg

        private Command _manualAddCommandArg;

        public Command ManualAddCommandArg => _manualAddCommandArg ?? (_manualAddCommandArg = new Command<string>(ExecuteItemAddArg, (id) => !string.IsNullOrEmpty(id)));

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

        public Command RevertReturnCommandArg => _revertReturnCommandArg ?? (_revertReturnCommandArg = new Command<BasketReturnItem>(ExecuteRevertReturn));

        #endregion
        #endregion
        #region Transactions
        #region Alter

        private Command _alterTransactionSelectorCommand;

        public Command AlterTransactionSelectorCommand => _alterTransactionSelectorCommand ?? (_alterTransactionSelectorCommand = new Command(ExecuteAlterTransactionSelector));

        private Command _alterTransactionCommand;

        public Command AlterTransactionCommand => _alterTransactionCommand ?? (_alterTransactionCommand = new Command<int>(ExecuteAlterTransaction));

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

                // ÃÂ¢ÃÂÃÂ  A cancelled picker leaves the box ALONE and says nothing. The operator is
                // mid-decision; clearing what they typed or telling them the item does not exist
                // both undo work they are still doing.
                if (lookup.Cancelled) return;

                var item = lookup.Item;
                if (item == null)
                {
                    // ÃÂ¢ÃÂÃÂ  AN UNKNOWN BARCODE IS USUALLY A NEW PRODUCT, NOT A MISTAKE (cutover step
                    // 25). Until now the till said "we can't find an item with that ID" and stopped
                    // ÃÂ¢ÃÂÃÂ so the only way to sell something newly delivered was to leave the counter,
                    // find another machine, and add it there. That is how shops end up ringing new
                    // stock through as a "miscellaneous" line, which loses the sale from every
                    // stock figure and every category report it should appear in.
                    await OfferToAddUnknownAsync(ItemId);
                    return;
                }

                BasketItem tempItem;

                // Ã¢ÂÂ Ã¢ÂÂ  `Basket.Contains` IS THE FIX, AND ITS ABSENCE COST A SALE. Matt, 2026-08-11:
                // *"I am trying to add Ã¢ÂÂAll star batman tp vol1Ã¢ÂÂ which I just sold and it wont let me
                // add it to the till."*
                //
                // `SelectedBasketRecord` is bound to the basket listÃ¢ÂÂs selection and is NOT cleared
                // when the basket is emptied after a sale. So it kept pointing at the BasketItem for
                // the line just sold Ã¢ÂÂ an object no longer IN `Basket`. Scanning that same item matched
                // it here, incremented the quantity of a DETACHED GHOST, and added nothing to the
                // screen. No error, no line, and the scan box cleared as though it had worked.
                //
                // Ã¢ÂÂ  It could only ever affect the item that was just sold, which is exactly what was
                // reported Ã¢ÂÂ and exactly what makes it look like the item is broken rather than the till.
                //
                // Ã¢ÂÂ  Guarding on membership rather than only nulling the selection: this makes the bug
                // structurally impossible however the selection comes to dangle.
                if (SelectedBasketRecord != null &&
                    Basket.Contains(SelectedBasketRecord) &&
                    (SelectedBasketRecord is BasketItem) &&
                    ((BasketItem)SelectedBasketRecord).Item.Id.Equals(item.Id) &&
                    !SelectedBasketRecord.IsReturn)
                {
                    tempItem = SelectedBasketRecord as BasketItem;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => bI.Item.Id.Equals(item.Id) &&
                            bI.Price.Equals(item.Price) &&
                            bI.PriceExTax.Equals(item.ExPrice) &&
                            !bI.IsReturn);
                }

                if (tempItem == default)
                    Basket.Add(new BasketItem(item, Quantity));
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

                // Ã¢ÂÂ Ã¢ÂÂ  `Basket.Contains` IS THE FIX, AND ITS ABSENCE COST A SALE. Matt, 2026-08-11:
                // *"I am trying to add Ã¢ÂÂAll star batman tp vol1Ã¢ÂÂ which I just sold and it wont let me
                // add it to the till."*
                //
                // `SelectedBasketRecord` is bound to the basket listÃ¢ÂÂs selection and is NOT cleared
                // when the basket is emptied after a sale. So it kept pointing at the BasketItem for
                // the line just sold Ã¢ÂÂ an object no longer IN `Basket`. Scanning that same item matched
                // it here, incremented the quantity of a DETACHED GHOST, and added nothing to the
                // screen. No error, no line, and the scan box cleared as though it had worked.
                //
                // Ã¢ÂÂ  It could only ever affect the item that was just sold, which is exactly what was
                // reported Ã¢ÂÂ and exactly what makes it look like the item is broken rather than the till.
                //
                // Ã¢ÂÂ  Guarding on membership rather than only nulling the selection: this makes the bug
                // structurally impossible however the selection comes to dangle.
                if (SelectedBasketRecord != null &&
                    Basket.Contains(SelectedBasketRecord) &&
                    (SelectedBasketRecord is BasketItem) &&
                    ((BasketItem)SelectedBasketRecord).Item.Id.Equals(item.Id) &&
                    !SelectedBasketRecord.IsReturn)
                {
                    tempItem = SelectedBasketRecord as BasketItem;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => bI.Item.Id.Equals(item.Id) &&
                            bI.Price.Equals(item.Price) &&
                            bI.PriceExTax.Equals(item.ExPrice) &&
                            !bI.IsReturn);
                }

                if (tempItem == default)
                    Basket.Add(new BasketItem(item, Quantity));
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
        /// ÃÂ¢ÃÂÃÂ  REPLACES `Authorisation.RequestAuthorisedUserInput`, which is not ported and must not
        /// be: it declares `string authEmpId = default`, loops `while (string.IsNullOrEmpty(authEmpId))`
        /// and **never assigns it** ÃÂ¢ÃÂÃÂ so correct credentials re-prompt indefinitely and the only
        /// exit is Cancel. Its caller then re-checked the ORIGINAL operator anyway, so the
        /// authoriser was collected and thrown away.
        ///
        /// ÃÂ¢ÃÂÃÂ  The real rule lives in `OperatorLogin.AuthoriseOverrideAsync`: it refuses
        /// self-authorisation, applies the SUPERVISOR's own ceiling, window and staleness tier ÃÂ¢ÃÂÃÂ
        /// nothing about being an override relaxes any of it ÃÂ¢ÃÂÃÂ and names both people.
        /// </summary>
        /// <summary>
        /// Σ (price × qty) over SALE lines only, in pence — the room a discount has.
        ///
        /// ⚠ RETURNS ARE EXCLUDED, not subtracted. `SaleIncTax` nets them off, which is right for a
        /// total and wrong here: £10 of goods beside a £30 refund has £10 of discount headroom, not
        /// −£20. `VatLineMath.ForLine` drops a discount on a return by design, so money apportioned
        /// onto one vanishes and the sale then fails the server's reconcile invariant.
        /// </summary>
        private long SaleLinesGrossPence() =>
            Basket.OfType<BasketItem>()
                  .Where(b => !b.IsReturn)
                  .Sum(b => Pence.FromDecimal(b.Price) * Math.Max(1, b.Quantity));

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
                if (credentials is null) return null;   // cancelled ÃÂ¢ÃÂÃÂ the basket is untouched

                var login = new Plutus.Client.Core.OperatorLogin(
                    new Services.Connectivity.FileOperatorStore());

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
                // ÃÂ¢ÃÂÃÂ  An override that errors is an override that did NOT happen.
                CrashLog.Write("TillViewModel.RequestSupervisorOverrideAsync", ex);
                return null;
            }
        }

        private async void ExecuteAdjustItem(BasketItem basketItem)
        {
            if (IsBusy) return;

            // ÃÂ¢ÃÂÃÂ  THIS HAD NO PERMISSION CHECK AT ALL. Anyone who could reach the till could retype
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
                ViewElementData[] elements = {
                    new ViewElementData(1, "PriceExTax".Translate(), basketItem.PriceExTax.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true),
                    new ViewElementData(2, "Price".Translate(), basketItem.Price.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true)
                };

                var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Adjust".Translate());

                _ = data.TryGetValue(1, out string priceExTax);
                _ = data.TryGetValue(2, out string priceTax);
                if (priceExTax != null && priceTax != null)
                {
                    basketItem.PriceExTax = decimal.Parse(priceExTax, numberStyles, CultureInfo.CurrentCulture);
                    basketItem.Price = decimal.Parse(priceTax, numberStyles, CultureInfo.CurrentCulture);
                }
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Adjustment");
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
            if (IsBusy) return;

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

            await Application.Current.MainPage.DisplayAlert("Which item is coming back?",
                "Scan or search for the item the customer is returning so it is in the basket, tap its "
                + "line to select it, then press this again.\n\nYou will be asked which sale it came "
                + "from — pick it from this till's recent sales, or type the number on the receipt.",
                "OK".Translate());
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

                var returnItem = basketItem.Adapt<BasketReturnItem>();

                // ÃÂ¢ÃÂÃÂ  PICK THE SALE, DON'T TYPE ITS UUID. Until 2026-08-10 this dialog's first field
                // was "Sale id" and nothing in the app could produce one ÃÂ¢ÃÂÃÂ no list, no search. The
                // only source was the barcode on a PRINTED RECEIPT, so a till with no printer could
                // not refund anything at all, and the refund rule (complete since steps 15ÃÂ¢ÃÂÃÂ17) had
                // no door. Reported by Matt as *"In MAUI I cannot do a refund?"*.
                //
                // ÃÂ¢ÃÂÃÂ  Reads this till's OWN sales, so it works with the line down ÃÂ¢ÃÂÃÂ which is when a
                // shop most needs to hand money back. Typing an id stays available for goods bought
                // on ANOTHER till, where only the server knows the sale.
                var recent = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.ListRecentSalesAsync(20, purchasesOnly: true));

                const string anotherTill = "Sold on another till ÃÂ¢ÃÂÃÂ look it up in PlutusÃÂ¢ÃÂÃÂ¦";
                const string typeItInstead = "Enter a sale IDÃÂ¢ÃÂÃÂ¦";
                string saleIdFromPicker = null;

                if (recent is { Count: > 0 })
                {
                    var labels = recent
                        .Select(r => $"{r.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} ÃÂÃÂ· "
                                   + $"{r.GrossPence / 100m:C} ÃÂÃÂ· "
                                   + $"{r.LineCount} item{(r.LineCount == 1 ? "" : "s")}"
                                   + (string.IsNullOrWhiteSpace(r.FirstItemIdOne) ? "" : $" ÃÂÃÂ· {r.FirstItemIdOne}"))
                        .ToList();

                    // ÃÂ¢ÃÂÃÂ  THIS TILL'S SALES FIRST, ALWAYS. They are the common case and the only ones
                    // available with the line down. The platform lookup is the second step, not the
                    // default, so a refund never depends on the network unless it has to.
                    labels.Add(anotherTill);
                    labels.Add(typeItInstead);

                    var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                        Application.Current.MainPage.DisplayActionSheet(
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

                // ÃÂ¢ÃÂÃÂ  Only ask for what is still unknown. Having just chosen the sale from a list,
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

                    // ÃÂ¢ÃÂÃÂ  AN EMPTY DICTIONARY IS "THEY BACKED OUT" ÃÂ¢ÃÂÃÂ checked BEFORE anything is
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

                    // ÃÂ¢ÃÂÃÂ  The sale id comes from the picker when there was one ÃÂ¢ÃÂÃÂ the dialog only ever
                    // carries the fields it actually asked for.
                    if (saleIdFromPicker is not null)
                        alertReturnValues[1] = saleIdFromPicker;

                    // ÃÂ¢ÃÂÃÂ  BOTH answers are required, and the old test said the opposite. It was
                    // `!TryGetValue(1, ÃÂ¢ÃÂÃÂ¦) & !TryGetValue(2, ÃÂ¢ÃÂÃÂ¦)` ÃÂ¢ÃÂÃÂ a non-short-circuit AND, so it
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

                    // ÃÂ¢ÃÂÃÂ  CANCEL MUST ESCAPE, and it did not. `InputAlert`'s Cancel button blanks the
                    // entries and then fires the CONFIRM handler, so the dictionary comes back with
                    // its keys present and their values null ÃÂ¢ÃÂÃÂ `TryGetValue` returns true, the
                    // error branch above is skipped, and this branch re-opened the dialog. The
                    // operator was trapped in a modal with no way out but killing the app, losing
                    // the basket with it. Blank input IS the cancel signal here; there is no other.
                    if (string.IsNullOrEmpty(saleIdText) || string.IsNullOrEmpty(reasonText))
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Return", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    // ÃÂ¢ÃÂÃÂ  THE WHOLE LEGACY LOOKUP IS GONE (cutover step 16). It queried the local
                    // legacy `Trans` table ÃÂ¢ÃÂÃÂ empty on a portal-provisioned till, and permanently
                    // so, because sales are committed to the new store ÃÂ¢ÃÂÃÂ and then called
                    // `trans.First()`, which throws from this `async void` with no catch the
                    // moment the item is not on that sale. Scanning the wrong receipt, an ordinary
                    // counter mistake, closed the application. Its "refunds left" test counted
                    // QUANTITIES on this machine only, so goods bought on another till could be
                    // refunded here in full, twice.
                    //
                    // `ReturnLookup` prefers the SERVER (only it knows what other tills have given
                    // back) and falls back to this till's own record only INSIDE the rolling
                    // window. `RefundRules.Authorise` ÃÂ¢ÃÂÃÂ the shared rule ÃÂ¢ÃÂÃÂ decides.
                    var resolution = await Services.Sales.ReturnLookup.ResolveAsync(
                        saleIdText, basketItem.Item?.Id, basketItem.Quantity);

                    if (!resolution.IsAllowed)
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Hmm".Translate(), resolution.Decision.Reason, "OK".Translate());
                        return;
                    }

                    // ÃÂ¢ÃÂÃÂ  SAY SO WHEN IT WAS CAPPED. `RefundDecision` is explicit that handing over
                    // less than was asked for without saying so is how a refund becomes an argument
                    // at the counter ÃÂ¢ÃÂÃÂ the customer expects the figure they asked for.
                    if (resolution.Decision.WasCapped &&
                        !await Application.Current.MainPage.DisplayAlert(
                            "Hmm".Translate(),
                            $"Only {resolution.Decision.AllowedPence / 100m:C2} of this sale is left to refund "
                            + $"({resolution.Decision.AlreadyRefundedPence / 100m:C2} has already been given back). "
                            + "Refund that instead?",
                            "Yes".Translate(), "Cancel".Translate()))
                        return;

                    // ÃÂ¢ÃÂÃÂ  THE PRICE THE CUSTOMER ACTUALLY PAID, from the original sale ÃÂ¢ÃÂÃÂ not today's
                    // catalogue price. A price that moved since would refund the wrong amount, and
                    // the direction it goes wrong is whichever way the shop loses.
                    returnItem.Price = resolution.UnitIncPence / 100m;
                    returnItem.PriceExTax = resolution.UnitExPence / 100m;
                    returnItem.SetItemReturn(reasonText, saleIdText);
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

        private void ExecuteRevertReturn(BasketReturnItem basketReturnItem)
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
            }
        }

        public bool HasAttachedCustomer => _attachedCustomer != null;

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
                    Application.Current.MainPage.DisplayActionSheet(
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
            RefreshMemberDiscount();

            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member attached",
                new Dictionary<string, string>
                {
                    { "CustomerId", detail.Id.ToString() },
                    { "Tier", detail.Membership?.Tier ?? "none" },
                    { "Expired", (detail.Membership?.Expired ?? false).ToString() },
                });

            // ⚠ Say what changed. An operator who cannot see that a discount was applied will apply
            // one by hand as well — finding W's lesson, on a different screen.
            if (MemberDiscountBasketOn() is BasketAlteration applied)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Member".Translate(),
                    string.Format("{0} — {1} off this basket.",
                        AttachedCustomerLabel, Math.Abs(applied.Price).ToString("C2", CultureInfo.CurrentCulture)),
                    "OK".Translate());
            }
        }

        private Command _addMemberCommand;
        public Command AddMemberCommand => _addMemberCommand ??= new Command(ExecuteAddMember);

        private Command _setTierCommand;
        public Command SetTierCommand => _setTierCommand ??= new Command(ExecuteSetTier);

        /// <summary>Can this operator add a member? Drives the button's visibility as well as the
        /// gate, so a cashier without the grant is not shown a control that will refuse them.</summary>
        public bool MayAddCustomers =>
            Services.Security.TillGate.CheckAny(
                App.GetViewModel().SignedInOperator, null,
                PermissionCatalogue.PosCustomersAdd, PermissionCatalogue.CustomersManage).Allowed;

        /// <summary>Can this operator change a member's tier? Supervisor and up.</summary>
        public bool MayManageCustomers =>
            Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.CustomersManage).Allowed;

        /// <summary>
        /// Sign a new member up at the till — binding default 20, *"Till operator to add new loyalty
        /// members"*.
        ///
        /// ⚠⚠ **NO TIER PICKER IN THIS DIALOG, AND THAT IS THE WEB TILL'S SCAR, NOT A SIMPLIFICATION.**
        /// Creating a member and setting their tier are two calls with two different permissions:
        /// `pos.customers.add` creates, `customers.manage` sets the tier. A dialog that offers both
        /// to a cashier gets **201 on the create and 403 on the tier** — an error message in front of
        /// a customer, for a member who HAS actually been added, whose natural retry creates a
        /// duplicate. So the tier is a separate, separately-gated action (`SetTierCommand`), and this
        /// dialog says who to ask.
        ///
        /// ⚠ ONLINE-ONLY, deliberately and permanently: the membership number comes from a
        /// tenant-wide counter, so two offline tills would mint the same one. The refusal says so
        /// rather than implying a retry will help.
        ///
        /// ⚠ CREATE-ONLY. There is no edit path on this till at all — changing a member's email
        /// quietly redirects their account.
        /// </summary>
        private async void ExecuteAddMember()
        {
            if (IsBusy) return;

            var gate = Services.Security.TillGate.CheckAny(
                App.GetViewModel().SignedInOperator, null,
                PermissionCatalogue.PosCustomersAdd, PermissionCatalogue.CustomersManage);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            IsBusy = true;
            try
            {
                IValidator[] required = { new RequiredValidator() };

                ViewElementData[] elements =
                {
                    new ViewElementData(1, "Name".Translate(), "", required.AsEnumerable(), false, true),
                    new ViewElementData(2, "Email (optional)", "", new List<IValidator>(), false, false),
                    new ViewElementData(3, "Phone (optional)", "", new List<IValidator>(), false, false),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Add".Translate(), false, "New member", "Cancel".Translate());

                answers.TryGetValue(1, out var name);
                if (string.IsNullOrWhiteSpace(name)) return;

                answers.TryGetValue(2, out var email);
                answers.TryGetValue(3, out var phone);

                var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "A member can only be added while the till is online — their membership number comes from Plutus. Nothing has been saved.",
                        "OK".Translate());
                    return;
                }

                var (ok, id, memberNo, problem) = await api.CreateCustomerAsync(name, email, phone);

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "That member couldn't be added.", "OK".Translate());
                    return;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member added",
                    new Dictionary<string, string> { { "CustomerId", id.ToString() }, { "MemberNo", memberNo ?? "" } });

                // ⚠ Attach them straight away. They were added mid-sale, so the operator's next
                // action is always to put them on it — and re-reading gives us the membership shape
                // the discount needs rather than assuming a new member has none.
                var detail = await api.GetCustomerAsync(id);
                if (detail != null)
                {
                    AttachedCustomer = detail;
                    RefreshMemberDiscount();
                }

                await Application.Current.MainPage.DisplayAlert(
                    "Member".Translate(),
                    string.Format("{0} added. Membership number {1}.", name, memberNo ?? "(pending)")
                    + (MayManageCustomers
                        ? ""
                        : " " + "A supervisor can set their tier."),
                    "OK".Translate());
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.ExecuteAddMember", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That member couldn't be added. Nothing has been saved.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Set or change the attached member's tier — **Supervisor and up** (`customers.manage`).
        ///
        /// ⚠ THE TILL SENDS A TIER ID AND NOTHING ELSE. The tier owns its discount rate and renewal
        /// length, so re-rating "Gold" in the portal moves every Gold member at once instead of
        /// leaving a snapshot behind on whichever till assigned it. A till never types a rate.
        ///
        /// ⚠ The customer is RE-READ afterwards rather than patched locally: the server decides the
        /// renewal date and the expiry verdict, and the discount on this basket must follow the
        /// server's answer, not this till's guess at it.
        /// </summary>
        private async void ExecuteSetTier()
        {
            if (IsBusy) return;

            if (AttachedCustomer is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Attach a member first, then set their tier.", "OK".Translate());
                return;
            }

            var gate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.CustomersManage);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            IsBusy = true;
            try
            {
                var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Tiers can only be changed while the till is online.", "OK".Translate());
                    return;
                }

                var tiers = (await api.GetLoyaltyTiersAsync())?.Where(t => t.Active).ToList();

                // ⚠ Tiers are created in the PORTAL only (binding default 20). An empty list is a
                // configuration answer, not an error, and saying so stops an operator hunting for a
                // till setting that does not exist.
                if (tiers is null || tiers.Count == 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "No membership tiers have been set up yet. They're created in the Plutus portal.",
                        "OK".Translate());
                    return;
                }

                var names = tiers
                    .Select(t => $"{t.Name} · {SharedKernel.MemberDiscount.Label(t.Name, t.AutoDiscountRate)}")
                    .ToArray();

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Application.Current.MainPage.DisplayActionSheet(
                        "Which tier?", "Cancel".Translate(), null, names));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                var index = Array.IndexOf(names, picked);
                if (index < 0) return;

                var (ok, problem) = await api.SetMembershipAsync(AttachedCustomer.Id, tiers[index].Id);

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "That tier couldn't be set.", "OK".Translate());
                    return;
                }

                // ⚠ Re-read, never patch locally — see the header.
                var refreshed = await api.GetCustomerAsync(AttachedCustomer.Id);
                if (refreshed != null)
                {
                    AttachedCustomer = refreshed;
                    RefreshMemberDiscount();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.ExecuteSetTier", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That tier couldn't be set.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Take the member off this sale.
        ///
        /// ⚠ Removes the members' discount ONLY — an operator's own manual discounts stay, because
        /// they were a separate decision. `MemberDiscountBasket.ExistingOn` finds it by sentinel id
        /// for exactly this reason.
        /// </summary>
        private void ExecuteDetachCustomer()
        {
            AttachedCustomer = null;
            RefreshMemberDiscount();
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

        private BasketAlteration MemberDiscountBasketOn() =>
            Services.Storage.MemberDiscountBasket.ExistingOn(Basket);

        /// <summary>
        /// Rebuild the members' discount to match the basket as it stands.
        ///
        /// ⚠⚠ RE-ENTRANCY IS THE WHOLE RISK HERE. This is called FROM `Basket.CollectionChanged`, and
        /// it adds and removes a basket record — so without the flag it would recurse until the
        /// stack ran out, on the first scan a member ever made.
        ///
        /// ⚠ REMOVE THEN REBUILD, never edit in place: the alteration's associated-items list is
        /// what decides where the money lands, and mutating it would leave a stale association
        /// pointing at lines that are no longer in the basket.
        ///
        /// ⚠ Silent by design. A member discount that cannot be computed must not interrupt a sale;
        /// the worst case is the pre-existing behaviour (no discount), which is visible on screen.
        /// </summary>
        private void RefreshMemberDiscount()
        {
            if (_refreshingMemberDiscount) return;
            _refreshingMemberDiscount = true;

            try
            {
                var existing = MemberDiscountBasketOn();
                if (existing != null) Basket.Remove(existing);

                var m = AttachedCustomer?.Membership;
                if (m is null) return;

                var rebuilt = Services.Storage.MemberDiscountBasket.Build(
                    Basket,
                    m.Tier,
                    m.AutoDiscountRate,
                    hasMembership: true,
                    expired: m.Expired,
                    operatorUserId: App.GetViewModel().SignedInOperator?.UserId ?? Guid.Empty);

                if (rebuilt != null) Basket.Add(rebuilt);
            }
            catch (Exception ex)
            {
                CrashLog.Write("TillViewModel.RefreshMemberDiscount", ex);
            }
            finally
            {
                _refreshingMemberDiscount = false;
                OnPropertyChanged(nameof(SaleExTax));
                OnPropertyChanged(nameof(SaleIncTax));
            }
        }

        #endregion

        #region Transaction
        #region Alter
        // ÃÂ¢ÃÂÃÂ  `async void` because it is a Command handler ÃÂ¢ÃÂÃÂ so it MUST NOT let an exception escape.
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

                // ÃÂ¢ÃÂÃÂ  `App.GetViewModel().EmployeeId` used to be passed here and it CRASHED THE APP on
                // a portal-provisioned till: the property threw on an empty legacy roster, out of a
                // plain `void` command handler, straight through Button.Clicked to the UI thread.
                // Tapping the leftmost button on the till screen closed the application. It now
                // returns null, and the audit user is the SIGNED-IN OPERATOR, which is the person
                // who actually applied the discount on either sign-in path.
                var auditUser = App.GetViewModel().SignedInOperator?.UserId.ToString()
                                ?? App.GetViewModel().EmployeeId;

                using (var db = new Helpers.Database.Database(databaseProvider, auditUser))
                {
                    foreach (var discount in db.Get<DiscountModel>())
                        Alterations.Add(discount);
                }

                // ÃÂ¢ÃÂÃÂ  THE PICKER MUST NOT OPEN EMPTY. This was an `SfPicker`, whose `SelectedIndex` on
                // a column with no rows is 0, not null, so the view's SelectionChanged fired
                // `AlterTransactionCommand.Execute(0)` and `Alterations.ElementAt(0)` threw
                // ArgumentOutOfRangeException in another `async void` ÃÂ¢ÃÂÃÂ an empty dialog whose OK
                // button closed the app. The legacy Discounts table is empty on a portal till and
                // was never seeded even on legacy ones. ÃÂ¢ÃÂÃÂ  The guard STAYS even though an action
                // sheet cannot do that: an empty sheet is still a dead end for the operator.
                if (Alterations.Count == 0)
                {
                    Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(),
                        "There are no discounts set up for this till yet.",
                        "OK".Translate());
                    return;
                }

                // ÃÂ¢ÃÂÃÂ  AN ACTION SHEET, NOT A SYNCFUSION PICKER (2026-08-10). Matt is not renewing the
                // licence, and this app already uses `DisplayActionSheet` for tenders, item search
                // and refund origins ÃÂ¢ÃÂÃÂ so this is the control operators here already know, and it
                // has no markup that can go stale against a package version.
                //
                // ÃÂ¢ÃÂÃÂ  Through `Modal`, because choosing a discount leads straight into ANOTHER dialog
                // (the amount prompt), and two modals in quick succession is what threw the
                // COMException that closed the till at the payment prompt.
                var names = AlterationNames.ToArray();
                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Application.Current.MainPage.DisplayActionSheet(
                        "Alterations".Translate(), "Cancel".Translate(), null, names));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                var index = Array.IndexOf(names, picked);
                if (index < 0) return;

                // ÃÂ¢ÃÂÃÂ  Released BEFORE dispatching, because `ExecuteAlterTransaction` opens with the
                // same `if (IsBusy) return;` guard ÃÂ¢ÃÂÃÂ leaving it set here would make the discount
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
                // ÃÂ¢ÃÂÃÂ  THIS HAD NO PERMISSION CHECK AT ALL, and it is the one place on the till where
                // an operator types a money amount straight off the goods ÃÂ¢ÃÂÃÂ a cash discount or a
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

                // ÃÂ¢ÃÂÃÂ  THE V2 STORE, AND CONTRACT JSON (cutover step 18, binding default 15). This
                // wrote to the LEGACY database ÃÂ¢ÃÂÃÂ which on a portal-provisioned till is an empty
                // file the app creates on first use ÃÂ¢ÃÂÃÂ and serialised with Newtonsoft
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
                    // ÃÂ¢ÃÂÃÂ  The basket is NOT cleared. Clearing after a failed park loses it entirely,
                    // and the operator believes it is safely put aside.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                    return;
                }

                StoredTransactions.Add(new SavedTransactionModel { Id = parkId.ToString("D"), Name = transName });
                Basket.Clear();

                // â  THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a

                // just-sold item being re-added â see the guard in ExecuteItemAdd.

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
                SavedTransactionModel storedTransaction;
                if (StoredTransactions.Count > 1)
                {
                    var baskets = new string[StoredTransactions.Count];
                    for (int i = 0; i < StoredTransactions.Count; i++)
                        baskets[i] = StoredTransactions.ElementAt(i).Name;
                    var action = await Application.Current.MainPage.DisplayActionSheet("Baskets".Translate(), "Cancel".Translate(), null, baskets);
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

                // ÃÂ¢ÃÂÃÂ  READ IT BEFORE DELETING IT. The old code deleted the row and then deserialised
                // the copy it happened to be holding ÃÂ¢ÃÂÃÂ so a blob that failed to parse (which the
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

                // ÃÂ¢ÃÂÃÂ  DELETE ONLY ONCE THE CONTENTS ARE IN HAND, and only if the row was really there:
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


                // â  THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a


                // just-sold item being re-added â see the guard in ExecuteItemAdd.


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
                // ÃÂ¢ÃÂÃÂ  THIS LINE USED TO READ `App.GetViewModel().EmployeeId` AND IT CLOSED THE APP.
                // That property is `Employees.Last().Id`, and `Employees` is populated ONLY by the
                // legacy local login ÃÂ¢ÃÂÃÂ the portal roster path sets `SignedInOperator` and never
                // touches it. So on every portal-provisioned till the read threw
                // `InvalidOperationException: Sequence contains no elements`, from an `async void`
                // with no catch, BEFORE the first await ÃÂ¢ÃÂÃÂ which reposts to the UI thread as an
                // unhandled exception and terminates the process. Pressing Checkout killed the till
                // mid-sale, with a full basket and a customer at the counter, and no dialog.
                //
                // ÃÂ¢ÃÂÃÂ  And it fed NOTHING. The value was set on a `SaleModel` that step 11 stopped
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

                var refundOnly = !Basket.Any(bR => bR is BasketItem && !bR.IsReturn);

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

                sale.Total = Basket.Sum(bR => bR.Price * (bR.IsReturn ? -1 : 1) * bR.Quantity);
                sale.TotalExTax = Basket.Sum(bR => bR.PriceExTax * (bR.IsReturn ? -1 : 1) * bR.Quantity);

                const NumberStyles testStyles = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                    | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;

                // ÃÂ¢ÃÂÃÂ  Remembered across the two callbacks: the amount prompt names the tender the
                // operator just picked, and each payment row must reuse the SAME
                // `PaymentMethodModel` instance rather than re-running the factory.
                var chosenMethods = new Dictionary<string, PaymentMethodModel>();
                var lastPickedName = string.Empty;

                // ÃÂ¢ÃÂÃÂ  THE TENDER SEQUENCE NOW LIVES IN `Client.Core.TenderLoop` (cutover step 11b).
                //
                // It was ~90 lines here, inside a ~200-line `async void` that also assembles the
                // sale, adds the surcharge line, commits and prints ÃÂ¢ÃÂÃÂ so NOTHING about taking money
                // could be exercised without a running UI host. All three tendering defects found
                // on 2026-08-10 shipped as a result, and every one was a loop that could not
                // terminate: a cancel that fell through and appended a ÃÂÃÂ£0 payment, a `0` tender that
                // did the same, and an amount prompt with no exit at all.
                //
                // The loop is now 19 unit tests and three mutation checks. What is left here is the
                // ASKING ÃÂ¢ÃÂÃÂ dialogs ÃÂ¢ÃÂÃÂ and mapping the answer onto the legacy sale model.
                var tender = await Plutus.Client.Core.TenderLoop.RunAsync(
                    Pence.FromDecimal(sale.Total),

                    // Which tender? ÃÂ¢ÃÂÃÂ  Also where the surcharge line is added, because choosing CARD
                    // is what creates it. The fee is returned to the loop, which applies it to the
                    // outstanding balance AT MOST ONCE ÃÂ¢ÃÂÃÂ a split card payment must not be charged a
                    // flat fee twice, and that is now the loop's rule rather than this method's.
                    chooseMethod: async (outstanding, paidSoFar) =>
                    {
                        var payMethNames = payMeths.Keys.ToArray();

                        // ⚠⚠ SAY WHAT HAS BEEN TAKEN AND WHAT IS LEFT. Matt, 2026-08-13, on a £4.40
                        // basket: *"I press cash, put in £2, it takes me back to the 'Card or cash'
                        // screen but doesn't tell me anything has been paid or there is X to pay. I
                        // assume its not actually working."*
                        //
                        // ⚠ It WAS working — `A_split_payment_accumulates_to_exactly_the_total` has
                        // pinned that since the loop was extracted — and this title said only
                        // "Payment Method", on the second pass exactly as on the first. The balance
                        // appeared one screen LATER, in the amount prompt, which is no use to someone
                        // deciding whether the till has just eaten £2 in front of a customer.
                        //
                        // ⚠ An operator who cannot tell a working split payment from a broken one will
                        // stop using split payments, or worse, take the money twice.
                        //
                        // ⚠ The web till shows a standing "Paid / Remaining" pair on one form
                        // (`CheckoutDialog.tsx`); MAUI asks in sequence, so the same two numbers ride
                        // in the title instead. Same words, same money, different shape — the shapes
                        // converge when the checkout screen is rebuilt (step 11b).
                        var owed = Math.Abs(outstanding) / 100m;
                        var taken = Math.Abs(paidSoFar) / 100m;
                        var title = paidSoFar == 0
                            ? $"{"PayMeth".Translate()} — {owed:C} to {(refundOnly ? "refund" : "pay")}"
                            : refundOnly
                                ? $"Refunded {taken:C} — {owed:C} left to refund"
                                : $"Paid {taken:C} — {owed:C} left to pay";
                        // ÃÂ¢ÃÂÃÂ  Through `Modal` ÃÂ¢ÃÂÃÂ one dialog at a time, with a settle between them.
                        // Entering `0` refuses and loops back HERE, and raising this action sheet
                        // while the amount popup was still tearing down threw a COMException out of
                        // an `async void` and closed the till (2026-08-10).
                        var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                            Application.Current.MainPage.DisplayActionSheet(
                                title, "Cancel".Translate(), null, payMethNames));

                        if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate()
                            || !payMeths.ContainsKey(picked))
                            return Plutus.Client.Core.TenderChoice.Abandoned;

                        var method = payMeths[picked]();
                        chosenMethods[picked] = method;
                        lastPickedName = picked;

                        long feePence = 0;

                        // ÃÂ¢ÃÂÃÂ  THE SURCHARGE IS THE TENANT'S GATEWAY SETTING, NOT THE LEGACY
                        // `PaymentMethod.Charge`. That field lives on a GLOBAL table ÃÂ¢ÃÂÃÂ one tenant's
                        // fee would have been every tenant's ÃÂ¢ÃÂÃÂ and its old path was broken twice
                        // over: a misspelt resource key, and a money-carrying `BasketNote` the
                        // commit guard refuses.
                        //
                        // ÃÂ¢ÃÂÃÂ  A REAL LINE against the provisioned CARD-SURCHARGE item, priced by the
                        // shared rules: the fee is further consideration for the main supply (Bookit
                        // C-607/14 / NEC C-130/15), so its VAT FOLLOWS THE BASKET ÃÂ¢ÃÂÃÂ zero on
                        // zero-rated goods, blended on a mixed basket, never a hardcoded rate. Card
                        // tenders only, never on refunds.
                        if (SharedKernel.Tenders.FromMethodName(method.Name) == SharedKernel.Tenders.Card
                            && !refundOnly
                            && !Services.Storage.CheckoutCommit.HasSurcharge(Basket))
                        {
                            var (surchargeBp, surchargeFlat) = await Services.Storage.GatewaySurcharge.GetAsync();
                            var feeLine = Services.Storage.CheckoutCommit.SurchargeItem(Basket, surchargeBp, surchargeFlat);
                            if (feeLine != null)
                            {
                                // ⚠ ON THE UI THREAD. `Basket` is bound, and `TenderLoop` awaits this
                                // callback with `ConfigureAwait(false)` — so from the second pass
                                // onwards (any pass after a refusal) we are on a pool thread, where a
                                // CollectionChanged notification into WinUI is the same crash class as
                                // A4's dialog. ⚠ Currently DORMANT rather than fixed-in-time: Kapow's
                                // surcharge rate is zero, so `SurchargeItem` returns null and nothing
                                // is added. It would have surfaced on the first tenant that charges a
                                // card fee, as a crash nobody could reproduce here.
                                await MainThread.InvokeOnMainThreadAsync(() => Basket.Add(feeLine));
                                // ÃÂ¢ÃÂÃÂ  The BASKET stays authoritative for `sale.Total` ÃÂ¢ÃÂÃÂ the commit
                                // guard compares the header against the sum of the lines, so a total
                                // computed anywhere else is a second opinion about money.
                                sale.Total = Basket.Sum(bR => bR.Price * (bR.IsReturn ? -1 : 1) * bR.Quantity);
                                sale.TotalExTax = Basket.Sum(bR => bR.PriceExTax * (bR.IsReturn ? -1 : 1) * bR.Quantity);
                                feePence = Pence.FromDecimal(feeLine.Price * feeLine.Quantity);
                            }
                        }

                        // ⚠ FINDING Y: the cap travels with the choice, so the loop can enforce it and
                        // the amount prompt can pre-fill a number that will actually be accepted. Zero
                        // when this is not a refund, or when the origin's tenders are unknown (a sale
                        // from another till — see `OriginTenderCapacitiesAsync`).
                        var capPence = refundCaps
                            .Where(c => c.TenderType == SharedKernel.Tenders.FromMethodName(method.Name))
                            .Select(c => c.RemainingPence)
                            .DefaultIfEmpty(0)
                            .First();

                        // ⚠ STORE CREDIT IS CAPPED AT THE BALANCE, through the SAME mechanism finding
                        // Y built for refunds — the loop already knows how to refuse a tender that
                        // exceeds its own limit and to word the prompt from it. Inventing a second
                        // cap here would be a second rule about how much a tender may take.
                        //
                        // ⚠ It caps what can be TYPED. The server still refuses an overdraw at redeem
                        // time, and that refusal still aborts the sale — this only means the operator
                        // meets the limit before the customer does.
                        if (SharedKernel.Tenders.FromMethodName(method.Name) == SharedKernel.Tenders.Credit)
                            capPence = CreditAvailablePence;

                        // ⚠ Same mechanism again for the gift card's balance — one rule about how
                        // much a tender may take, three callers of it.
                        if (SharedKernel.Tenders.FromMethodName(method.Name) == SharedKernel.Tenders.GiftCard)
                            capPence = GiftCardAvailablePence;

                        return new Plutus.Client.Core.TenderChoice(
                            picked, method.IsChangeable, feePence, capPence);
                    },

                    // How much? ÃÂ¢ÃÂÃÂ  WITH A CANCEL BUTTON. Raised without one, and with
                    // `interuptable: false` and an `OnBackButtonPressed` that swallowed Escape, this
                    // dialog had no exit of any kind ÃÂ¢ÃÂÃÂ the operator could only leave by killing the
                    // process, mid-sale.
                    askAmount: async (outstanding, mostAllowed) =>
                    {
                        // ⚠ FINDING Y: the box is pre-filled and worded from what this METHOD may take,
                        // not from the sale's balance. On a split-paid refund those differ, and a
                        // default the loop is about to refuse teaches an operator to type over it.
                        var outstandingDecimal = mostAllowed / 100m;
                        var balanceDecimal = outstanding / 100m;
                        var cappedByTender = Math.Abs(mostAllowed) < Math.Abs(outstanding);

                        IValidator[] validators = {
                            new RequiredValidator(),
                            new CurrencyValueValidator(testStyles)
                        };

                        ViewElementData[] elements = {
                            new ViewElementData(1, "Amount", "", validators.AsEnumerable(), false, true)
                        };

                        // ⚠⚠ DO NOT WRAP THIS IN `Modal`. `InputAlertHelper` ALREADY DOES, INTERNALLY.
                        //
                        // ⚠ This call was wrapped here on 2026-08-11 while fixing *"something went
                        // wrong taking payment"*, on the belief that the prompt did not go through the
                        // gate. It did — `InputAlertHelper.ShowAsync` has gated every input alert
                        // since the `Modal` gate was written the day before. So the flow took a
                        // non-reentrant `SemaphoreSlim(1,1)` TWICE and waited on itself for ever.
                        //
                        // ⚠⚠ THE TILL COULD NOT TAKE A SALE (Matt, 2026-08-13, on 1.48.0): the tender
                        // sheet closed, the amount box never appeared, and because the deadlock is
                        // inside the `try`, `finally { IsBusy = false; }` never ran — so the scan box,
                        // which opens `if (IsBusy) return;`, silently stopped searching too. One
                        // redundant guard, three symptoms, no exception and no log line.
                        //
                        // ⚠ The COMException the wrap was reaching for is real, and it is handled
                        // where it belongs: the gate inside the helper serialises the push against
                        // whatever dialog is still tearing down. `Modal` is now re-entrancy-safe as
                        // well, so this mistake cannot hang the app again — but a redundant wrap is
                        // still a lie about who owns the gate, so it is gone rather than tolerated.
                        var tendered = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                            elements,
                            "Confirm".Translate(),
                            false,
                            true,
                            outstandingDecimal,
                            // ⚠ When a tender's own limit is the binding one, SAY WHICH NUMBER IS WHICH.
                            // "There is £4.40 left to refund" beside a box that will only accept £2.40 is
                            // how an operator concludes the till is broken — which is exactly what
                            // happened with the split payment that told them nothing (finding W).
                            cappedByTender
                                ? $"How much to refund with {lastPickedName}? That method took "
                                  + $"{Math.Abs(Math.Round(outstandingDecimal, 2, MidpointRounding.AwayFromZero)):C} "
                                  + $"of this sale, and {Math.Abs(Math.Round(balanceDecimal, 2, MidpointRounding.AwayFromZero)):C} "
                                  + "is left to refund altogether — the rest goes back the way it was paid."
                                : string.Format(
                                    refundOnly ? "HowMuchRefund".Translate() : "HowMuchPM".Translate(),
                                    lastPickedName,
                                    Math.Round(outstandingDecimal, 2, MidpointRounding.AwayFromZero)),
                            "Cancel".Translate());

                        _ = tendered.TryGetValue(1, out var amountText);

                        if (tendered.Count == 0 || string.IsNullOrWhiteSpace(amountText))
                            return Plutus.Client.Core.TenderAmount.Abandoned;

                        // ÃÂ¢ÃÂÃÂ  TryParse, not Parse. The validators run in the dialog, but this string
                        // has crossed a UI boundary and a `FormatException` here is thrown from an
                        // `async void` ÃÂ¢ÃÂÃÂ which closes the till rather than rejecting the input.
                        if (!decimal.TryParse(amountText, testStyles, CultureInfo.CurrentCulture, out var typed))
                            return Plutus.Client.Core.TenderAmount.Abandoned;

                        return Plutus.Client.Core.TenderAmount.Of(Pence.FromDecimal(typed));
                    },

                    ct: default,

                    // ÃÂ¢ÃÂÃÂ  SAY WHY, BEFORE ASKING AGAIN. Matt, 2026-08-11: over-paying on a card and
                    // under-paying in cash both produced *"Something went wrong"*. Neither is a
                    // fault ÃÂ¢ÃÂÃÂ they are ordinary operator actions the loop correctly refuses ÃÂ¢ÃÂÃÂ but a
                    // prompt that reappears without a word reads as the till ignoring what was
                    // typed, in front of a customer.
                    //
                    // ÃÂ¢ÃÂÃÂ  THE WORDING IS MATT'S. It names what happened and says the basket is safe,
                    // because the fear at a counter is that a mistake has cost the sale.
                    onRefused: async (reason, outstanding) =>
                    {
                        var owed = (outstanding / 100m).ToString("C2");

                        var message = reason switch
                        {
                            Plutus.Client.Core.TenderRefusal.OverpaidWithoutChange =>
                                $"You cannot over pay with {lastPickedName}. Your basket is still here, please try again.",

                            // ÃÂ¢ÃÂÃÂ  Under-payment is NOT refused ÃÂ¢ÃÂÃÂ the loop takes it and asks for the
                            // rest, which is how split payments work. Reaching here with Zero means
                            // they entered nothing at all.
                            Plutus.Client.Core.TenderRefusal.Zero =>
                                $"Enough {lastPickedName.ToLowerInvariant()} has not been taken. {owed} is still to pay.",

                            // ⚠⚠ FINDING Y. Not "too much" in the sale's terms — too much for THIS
                            // method. The sentence has to say where the rest goes, or the operator's
                            // next move is to try a different card rather than to split the refund.
                            Plutus.Client.Core.TenderRefusal.OverTenderCapacity =>
                                $"That is more than {lastPickedName.ToLowerInvariant()} took on this sale, so it "
                                + "cannot all go back that way. Refund what this method paid, then pick the "
                                + $"other one for the rest — {owed} is left to refund altogether.",

                            _ => $"That amount can't settle this. {owed} is still to pay.",
                        };

                        // ÃÂ¢ÃÂÃÂ  Through `Modal` for the same reason as the prompts themselves: this
                        // sits BETWEEN two dialogs, which is precisely where the COMException lived.
                        // ÃÂ¢ÃÂÃÂ  `Modal.ShowAsync` needs a Task<T>; a plain three-button DisplayAlert
                        // returns a bare Task, so it is wrapped rather than bypassed.
                        await Services.UIHandeling.Modal.ShowAsync(async () =>
                        {
                            await Application.Current.MainPage.DisplayAlert(
                                "Hmm".Translate(), message, "OK".Translate());
                            return true;
                        });
                    });

                // ÃÂ¢ÃÂÃÂ  ABANDONED TAKES NOTHING AND LEAVES THE BASKET ALONE. It is not a partial
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
                        TempPayMethod = chosenMethods.TryGetValue(taken.MethodName, out var m)
                            ? m
                            : payMeths[taken.MethodName](),
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

                // ÃÂ¢ÃÂÃÂ  ONE GATE, AGAINST THE OPERATOR'S OWN CEILING (cutover step 12). What was here
                // could not work on a portal-provisioned till and had a hole in it besides:
                //
                //   ÃÂÃÂ· it looked up string action names ("Till", "Refund20", "Refund100") in a local
                //     AuthActions table that such a till does not have;
                //   ÃÂÃÂ· the hardcoded ÃÂÃÂ£20/ÃÂÃÂ£100/unlimited bands ignored each operator's actual ceiling;
                //   ÃÂÃÂ· ÃÂ¢ÃÂÃÂ  the refund total summed each return line's UNIT price and IGNORED QUANTITY,
                //     so five ÃÂÃÂ£30 returns tested as ÃÂÃÂ£30 and went straight through the ÃÂÃÂ£100 band;
                //   ÃÂÃÂ· and the do/while "escalation" re-tested the SAME operator every pass while
                //     RequestAuthorisedUserInput never assigned the id it returned ÃÂ¢ÃÂÃÂ so entering
                //     correct supervisor credentials re-prompted for ever and only Cancel escaped.
                //     Supervisor override on this till has never once succeeded.
                //
                // ÃÂ¢ÃÂÃÂ  A NULL operator BLOCKS. "We don't know who this is" must never mean "let them".
                var gate = Services.Security.TillGate.CheckCheckout(
                    App.GetViewModel().SignedInOperator, Basket);

                if (!gate.Allowed)
                {
                    if (!gate.NeedsOverride)
                    {
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                        return;
                    }

                    // ÃÂ¢ÃÂÃÂ  A real override: a SECOND person authenticates, and OperatorLogin refuses
                    // self-authorisation, applies the SUPERVISOR's own ceiling and window, and names
                    // both people for the audit trail. Declining leaves the basket untouched.
                    //
                    // ÃÂ¢ÃÂÃÂ  ESCALATE WHAT THE GATE ACTUALLY REFUSED. `CheckCheckout` tests `pos.sell`
                    // FIRST, so hardcoding "refund" here asked a supervisor to authorise a ÃÂÃÂ£0.00
                    // refund ÃÂ¢ÃÂÃÂ a basket with no returns refunds nothing ÃÂ¢ÃÂÃÂ and then let an ordinary
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
                // ÃÂ¢ÃÂÃÂ  `async void` ÃÂ¢ÃÂÃÂ WITHOUT THIS THE TILL CLOSES. This method had a `try`/`finally`
                // and no `catch` for its entire life, so anything that escaped went straight to the
                // dispatcher as an unhandled exception and took the process with it, mid-sale, with
                // a full basket. It happened on 2026-08-10: entering `0` at the payment prompt is
                // refused and the tender loop asks again, and WinUI threw a COMException building
                // the second action sheet while the first popup was still tearing down
                // (`ActionSheetContent..ctor` ÃÂ¢ÃÂÃÂ `UserControl..ctor`). The loop was right; the
                // absence of a catch is what turned a glitch into a closed till.
                //
                // ÃÂ¢ÃÂÃÂ  THE BASKET IS LEFT ALONE. If the sale committed before the fault, it is safely
                // queued and clearing would hide it; if it did not, the operator still has their
                // basket. Either way, losing it is the one outcome that cannot be undone at a
                // counter.
                Services.Analytics.CrashLog.Write("TillViewModel.Checkout", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Something went wrong taking payment. Your basket is still here ÃÂ¢ÃÂÃÂ please try again.",
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
        /// ÃÂ¢ÃÂÃÂ  ASKS FIRST. This cleared a full basket on a single tap with no confirmation and no
        /// undo ÃÂ¢ÃÂÃÂ a customer's whole order, mid-transaction, from a mis-tap on a busy counter.
        ///
        /// ÃÂ¢ÃÂÃÂ  NOT gated on `pos.void`, deliberately. Nothing here has been paid for or committed:
        /// the sale does not exist until checkout, so this is a correction, not a void. Requiring a
        /// supervisor to undo a mis-scan would put one at the counter for the most ordinary event
        /// on a till, and the operators would find a way around it ÃÂ¢ÃÂÃÂ which is worse than the gate
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


            // â  THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a


            // just-sold item being re-added â see the guard in ExecuteItemAdd.


            SelectedBasketRecord = null;
        }
        #endregion
        #endregion

        #region Operations
        private async void FinaliseTransation(SaleModel sale, decimal change)
        {
            {
                var itemHasNoStock = false;

                // ÃÂ¢ÃÂÃÂ  THE PER-LINE STOCK DECREMENT IS GONE (cutover step 11). It read a legacy
                // StockModel and called db.Save() ONCE PER LINE with no transaction, so a crash
                // halfway through a basket left some lines decremented and some not, with nothing
                // to reconcile against. v2 holds no local stock at all: the SERVER attributes
                // movement from `LineMeta.itemIdOne` on the sale it receives, which is one
                // authority instead of one per till.

                // ÃÂ¢ÃÂÃÂ  COMMIT BEFORE PRINTING, and this ordering is the whole point of the block.
                // The receipt is printed from what was COMMITTED ÃÂ¢ÃÂÃÂ so a printer failure is a
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
                // ⚠ NOTHING HAS BEEN TAKEN when this refuses: no sale row, no outbox entry. The
                // basket survives, which is what lets the operator take the money another way.
                if (!await TryRedeemStoreCreditAsync(tenders)) return;
                if (!await TryRedeemGiftCardAsync(tenders)) return;

                // ⚠ ACTIVATE LAST OF THE THREE, and still before the commit. A card being SOLD is
                // the only one of these that creates value rather than spending it — so if a redeem
                // above has already refused, no card has been loaded that a cancelled sale would
                // leave live in the customer's hand.
                if (!await TryActivateGiftCardsAsync()) return;

                var outcome = await Services.Storage.CheckoutCommit.CommitAsync(
                    Basket, tenders, App.GetViewModel().SignedInOperator?.UserId);

                if (!outcome.Committed)
                {
                    // ÃÂ¢ÃÂÃÂ  The basket is deliberately NOT cleared. Nothing was recorded, so the sale
                    // is still there to retry ÃÂ¢ÃÂÃÂ clearing it would lose the sale and the evidence.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), outcome.Message, "OK".Translate());
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
                    // ÃÂ¢ÃÂÃÂ  WHICH PRINTER ROUTE, DECIDED ONCE, BEFORE ANYTHING IS DISPATCHED.
                    //
                    // The till now prints the way the WEB till always has: it POSTs a rendered
                    // document to the Plutus Till Agent on this PC, which drives the printer
                    // through the ordinary Windows print queue. The old OPOS route stays as a
                    // fallback for tills with a genuine PointOfService device ÃÂ¢ÃÂÃÂ but it is the
                    // reason Matt could not find a printer the web till uses every day, because
                    // `PointOfService` enumerates a driver profile almost no receipt printer ships
                    // and the empty picker then volunteers "Wireless is turned off".
                    //
                    // ÃÂ¢ÃÂÃÂ  Resolved HERE, not inside the print call, because the DRAWER decision
                    // depends on it: the agent kicks the drawer as part of the print job, so
                    // dispatching an OPOS drawer task as well would kick it twice.
                    // ÃÂ¢ÃÂÃÂ  Free on a till nobody has paired ÃÂ¢ÃÂÃÂ `ResolveAsync` does not even probe.
                    var agent = await Services.Printing.TillAgentPrinting.ResolveAsync();

                    var wantsDrawer = TryCashDrawer
                        && sale.PaySales.Any(pay => pay.TempPayMethod.IsChangeable.Equals(true));

                    if (!AskForReceipt || await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ReceiptRequired".Translate(), "Yes".Translate(), "No".Translate()))
                    {
                        trackEventArgs.Add("Receipt Requested", "True");
                        // ÃÂ¢ÃÂÃÂ  Built from the COMMITTED payload, not from `sale` ÃÂ¢ÃÂÃÂ the receipt states
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
                                // the drawer opens as the paper starts moving ÃÂ¢ÃÂÃÂ as it does on the
                                // native till. ÃÂ¢ÃÂÃÂ  It never throws: a wedged agent or an off printer
                                // returns false, and no receipt is worth losing a committed sale.
                                var printed = await Services.Printing.TillAgentPrinting.TryPrintSaleAsync(
                                    receipt, Basket, App.GetViewModel().Store, wantsDrawer, agent);

                                // ÃÂ¢ÃÂÃÂ  HONESTLY, including when it did not print. This used to be
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
                            // ÃÂ¢ÃÂÃÂ  ONLY when the print job did not already carry it. `tasks[0]` is
                            // null when the operator declined a receipt ÃÂ¢ÃÂÃÂ and a cash sale still has
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

                            // ÃÂ¢ÃÂÃÂ  THE "SILENCE" BUTTON SILENCED NOTHING. This line SET
                            // `CashDrawerWarningSilenced` and **nothing anywhere read it** ÃÂ¢ÃÂÃÂ the
                            // alert was raised unconditionally on every drawer failure. So an
                            // operator who pressed Silence got the same modal on the very next cash
                            // sale, and on every cash sale after that, with no way to stop it.
                            //
                            // ÃÂ¢ÃÂÃÂ  It matters more than a nuisance: until the missing `return` in
                            // `POSCashDrawer.InitPOSObject` was fixed (same commit), a WORKING
                            // drawer threw `NotClaimable` on its own success path ÃÂ¢ÃÂÃÂ so this modal
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

                // â  THE SELECTION GOES WITH THE LINES. A dangling selection is what stopped a

                // just-sold item being re-added â see the guard in ExecuteItemAdd.

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
        /// ÃÂ¢ÃÂÃÂ  THIS USED TO READ THE LEGACY `PaymentMethodModel` TABLE, AND THAT STOPPED A NEW TILL
        /// SELLING AT ALL. The table is seeded only by `Database.Init()` ÃÂ¢ÃÂÃÂ the legacy first-run
        /// path ÃÂ¢ÃÂÃÂ which a portal-provisioned till never runs, and it has no local database anyway.
        /// So the payment sheet rendered ZERO buttons and the `paid != sale.Total` loop above could
        /// never terminate: the operator was stuck in a checkout with nothing to press but Cancel,
        /// with a customer in front of them. Every dev machine hid it, because they were migrated
        /// from legacy installs that had already seeded Card and Cash.
        ///
        /// The tenders now come from the FIXED shared set (binding default 13). The legacy model is
        /// still the carrier because the rest of this checkout reads `IsChangeable`/`IsCashBackable`
        /// off it; step 11b retires the model itself. ÃÂ¢ÃÂÃÂ  `Charge`/`MinimumCharge` stay ZERO ÃÂ¢ÃÂÃÂ the
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
                var originIds = Basket.OfType<BasketReturnItem>()
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
                var originIds = Basket.OfType<BasketReturnItem>()
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
        /// ÃÂ¢ÃÂÃÂ  THIS FIXES TWO LIVE DEFECTS, not just a data source.
        ///   1. `Database.SearchId` matched on `Id` alone and **did not honour tombstones**, so an
        ///      item the portal had BINNED was still sellable on this till ÃÂ¢ÃÂÃÂ indefinitely, because
        ///      nothing local ever learned it had gone. `FindByBarcodeAsync` excludes `Removed`,
        ///      which is precisely why the changes feed carries tombstones rather than upserts.
        ///   2. The price came from a stored column, so a price scheduled for 02:00 only applied if
        ///      a sync happened to land after it. It now comes from `EffectivePricePairAsync`,
        ///      evaluated at LOOKUP time against the effective-dated timeline ÃÂ¢ÃÂÃÂ the change lands on
        ///      the minute on a till that has been offline for a week.
        ///
        /// ÃÂ¢ÃÂÃÂ  IT STILL RETURNS AN `ItemModel`, AND THAT IS TEMPORARY SCAFFOLDING. The basket holds
        /// `BasketItem.Item` as a legacy entity, and reshaping that means reshaping
        /// `BasketReturnItem`, ~14 call sites, both Mapster configs and the template selector ÃÂ¢ÃÂÃÂ all
        /// of which have to land WITH `CommitSaleAsync` in step 11, or the till would build v2
        /// baskets and still save legacy sales, which is a worse half-state than either end.
        /// Only Id/Name/Price/ExPrice/Vat.Name are ever read off this object (verified by grep), so
        /// nothing needs the navigations the legacy query used to Include.
        /// **Step 11 deletes this projection.**
        /// </summary>
        /// <summary>How many matches an operator is offered before being asked to narrow it down.</summary>
        private const int SearchPickerLimit = 25;

        /// <summary>
        /// The outcome of asking for an item. ÃÂ¢ÃÂÃÂ  "Nothing matched" and "the operator changed their
        /// mind" are DIFFERENT and must not share a return value: telling somebody who just pressed
        /// Cancel that the item does not exist is how a working catalogue gets reported as broken.
        /// </summary>
        private sealed record ItemLookup(ItemModel Item, bool Cancelled)
        {
            public static readonly ItemLookup NotFound = new(null, false);
            public static readonly ItemLookup Abandoned = new(null, true);
        }

        /// <summary>
        /// Turn what is in the scan box into an item ÃÂ¢ÃÂÃÂ by BARCODE first, then by NAME.
        ///
        /// ÃÂ¢ÃÂÃÂ  SEARCHING BY NAME DID NOT EXIST HERE, and its absence read as an empty catalogue.
        /// This method called `FindByBarcodeAsync` and nothing else, so anything an operator TYPED
        /// ÃÂ¢ÃÂÃÂ "BAT" ÃÂ¢ÃÂÃÂ was tried as an exact barcode, missed, and produced *"We can't find an item
        /// with that ID"*. With 20,000 items synced and sellable. The message even said "that ID",
        /// which was accurate and completely misleading: it was never searching.
        ///
        /// ÃÂ¢ÃÂÃÂ  `TillStore.SearchAsync` had been built, correct and tested since 2026-08-09 ÃÂ¢ÃÂÃÂ and was
        /// called from NOWHERE in the app. That is the third time a finished component has sat
        /// unwired behind a screen that looked broken (the outbox drain, the catalogue browse, this)
        /// and it is worth naming as a pattern: a test suite proves a component works, never that
        /// anything uses it.
        ///
        /// ÃÂ¢ÃÂÃÂ  BARCODE FIRST, ALWAYS. A scan is the hot path and must stay exact and instant; a real
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

                // Not a code this till holds ÃÂ¢ÃÂÃÂ so it was typed. Search names.
                if (found == null)
                {
                    var chosen = await SearchForOneAsync(typed);
                    if (chosen.Cancelled) return ItemLookup.Abandoned;
                    found = chosen.Item;
                }

                if (found == null) return ItemLookup.NotFound;

                var price = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.EffectivePricePairAsync(found.Id));

                // The band's display name, for the Tax column. ÃÂ¢ÃÂÃÂ  Null is a legitimate answer ÃÂ¢ÃÂÃÂ the
                // portal may not have decided which band this tax row means ÃÂ¢ÃÂÃÂ and it must render as
                // blank rather than being guessed at.
                var bandName = await Services.Storage.VatBands.DisplayNameForItemAsync(found.Id);

                return new ItemLookup(new ItemModel
                {
                    // ÃÂ¢ÃÂÃÂ  IdOne, not the GUID: every legacy screen and the basket key on this string,
                    // and it IS the barcode.
                    Id = found.IdOne,
                    Name = found.Name,
                    // ÃÂ¢ÃÂÃÂ  Pence ÃÂ¢ÃÂÃÂ decimal pounds ONLY because the legacy model is decimal. Deliberately
                    // inline rather than a SharedKernel helper: money is integer pence end-to-end
                    // (architecture ÃÂÃÂ§4.1) and a shared penceÃÂ¢ÃÂÃÂdecimal converter would legitimise the
                    // conversion everywhere instead of confining it to this scaffolding.
                    Price = price.IncPence / 100m,
                    ExPrice = price.ExPence / 100m,
                    Vat = new TaxModel { Name = bandName ?? string.Empty },
                }, false);
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillViewModel.FindItem", ex);
                return ItemLookup.NotFound;
            }
        }

        /// <summary>
        /// Nothing matched what was scanned ÃÂ¢ÃÂÃÂ offer to put it in the catalogue (cutover step 25).
        ///
        /// ÃÂ¢ÃÂÃÂ  THE BARCODE TRAVELS WITH THE OFFER, which is the point. Reading a code off a packet
        /// and typing it in again is the step where a digit gets dropped, and the result is a second
        /// item nothing will ever scan to ÃÂ¢ÃÂÃÂ invisible on the shelf, invisible in stock, and only
        /// discovered when the real one is added later and the insert is refused.
        ///
        /// ÃÂ¢ÃÂÃÂ  IT IS AN OFFER, NOT AN AUTOMATIC JUMP. A mistyped search is far commoner than a new
        /// product, and a screen that leaps into "create item" every time someone fat-fingers the
        /// scan box is a screen people learn to fight.
        ///
        /// ÃÂ¢ÃÂÃÂ  Adding is REFUSED POLITELY without `portal.prices.manage` ÃÂ¢ÃÂÃÂ a cashier scanning an
        /// unknown code should be told the item is not in the catalogue, not offered a door that
        /// closes in their face. `TillGate` supplies the wording.
        /// </summary>
        private async Task OfferToAddUnknownAsync(string barcode)
        {
            var typed = (barcode ?? "").Trim();

            var allowed = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PortalPricesManage).Allowed;

            if (!allowed || string.IsNullOrWhiteSpace(typed))
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                return;
            }

            const string add = "Add it to the catalogueÃÂ¢ÃÂÃÂ¦";

            // ÃÂ¢ÃÂÃÂ  Through `Modal`, because saying yes leads straight into a run of further dialogs,
            // and two modals in quick succession is what threw the COMException that closed the
            // till at the payment prompt.
            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayActionSheet(
                    $"Nothing in the catalogue matches ÃÂ¢ÃÂÃÂ{typed}ÃÂ¢ÃÂÃÂ.", "Cancel".Translate(), null, add));

            if (picked != add) return;

            // ÃÂ¢ÃÂÃÂ  The inventory viewmodel owns item creation, and it is reached directly rather than
            // duplicated here ÃÂ¢ÃÂÃÂ the barcode check, the band list, the ex-price derivation and the
            // opening-stock call are one flow, and a second copy on the till screen would be the
            // exact drift `till-design.md` C2 exists to prevent. It re-syncs the catalogue when it
            // finishes, so the operator can scan the item again immediately.
            new Inventory.Items.ViewAllViewModel().ExecuteCreateItem(typed);
        }

        /// <summary>
        /// Name search, and the choice that follows when more than one thing matches.
        ///
        /// ÃÂ¢ÃÂÃÂ  MATCHING IS NOT DECIDED HERE. `TillStore.SearchAsync` runs `SharedKernel.ItemSearch`,
        /// the single home for what a typed query finds (till-design C1). A `LIKE` written in this
        /// file would be a fourth copy of a rule that was deliberately reduced to one, and the
        /// symptom of drift is two tills in the same shop disagreeing about the same query.
        ///
        /// ÃÂ¢ÃÂÃÂ  One match is added WITHOUT a prompt. Somebody typing a distinctive title wants the
        /// item, not a confirmation step, and a picker containing one row is a keystroke tax paid on
        /// every sale.
        /// </summary>
        /// <summary>
        /// Find a sale the PLATFORM holds ÃÂ¢ÃÂÃÂ from any till (WP11 / cutover step 26).
        ///
        /// ÃÂ¢ÃÂÃÂ  THIS IS WHAT MAKES A CROSS-TILL REFUND REACHABLE. Goods bought at another branch exist
        /// only on the platform, and until now the only way to name one was typing a UUID off a
        /// receipt ÃÂ¢ÃÂÃÂ so in practice they were not refundable at all unless the customer still had a
        /// printed receipt AND somebody was willing to type 36 characters.
        ///
        /// ÃÂ¢ÃÂÃÂ  REFUNDS ARE EXCLUDED, exactly as they are in the local picker. A refund is itself a sale
        /// with a negative gross; offering one defeats the cap entirely, which cost ÃÂÃÂ£13.99 twice on
        /// 2026-08-10.
        ///
        /// ÃÂ¢ÃÂÃÂ  Needs an OPERATOR token and a connection, and says so plainly when it has neither. This
        /// is the one refund path that genuinely cannot work offline ÃÂ¢ÃÂÃÂ the local list is what covers
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

            // ÃÂ¢ÃÂÃÂ  A FORTNIGHT, not "everything". Refunds are overwhelmingly recent, and a picker
            // holding months of sales is one nobody reads ÃÂ¢ÃÂÃÂ the server clamps `take` at 500 anyway.
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
                .Select(s => $"{s.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} ÃÂÃÂ· {s.GrossPence / 100m:C}"
                           // ÃÂ¢ÃÂÃÂ  Says WHOSE sale it is. Without it the operator cannot tell a
                           // neighbouring till's sale from one of their own, which is the entire
                           // question this list exists to answer.
                           + (thisTill is Guid t && s.TillId == t ? " ÃÂÃÂ· this till" : " ÃÂÃÂ· another till")
                           + (string.Equals(s.Channel, "Till", StringComparison.OrdinalIgnoreCase)
                               ? "" : $" ÃÂÃÂ· {s.Channel}"))
                .ToList();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayActionSheet(
                    "Which sale, from Plutus?", "Cancel".Translate(), null, labels.ToArray()));

            if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate()) return null;

            var index = labels.IndexOf(picked);
            return index >= 0 && index < purchases.Count ? purchases[index].Id.ToString("D") : null;
        }

        private async Task<SearchChoice> SearchForOneAsync(string typed)
        {
            // ÃÂ¢ÃÂÃÂ  Ask for one MORE than we will show, so "there are others" is known rather than
            // guessed. A silently truncated list reads as a complete one, and the operator concludes
            // the item is not in stock.
            var matches = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.SearchAsync(typed, new ViewModels.Settings().MatchAllWordsSetting, SearchPickerLimit + 1));

            if (matches == null || matches.Count == 0) return SearchChoice.Nothing;
            if (matches.Count == 1) return new SearchChoice(matches[0], false);

            var shown = matches.Take(SearchPickerLimit).ToList();

            // ÃÂ¢ÃÂÃÂ  The barcode is in the label because it is the only field guaranteed UNIQUE.
            // `DisplayActionSheet` hands back the chosen STRING, so two items sharing a name and
            // price would be indistinguishable and the first would always win ÃÂ¢ÃÂÃÂ quietly ringing up
            // the wrong variant.
            var choices = shown
                .Select(m => $"{m.Name} ÃÂÃÂ· {m.IdOne} ÃÂÃÂ· {(m.PricePence / 100m):C}")
                .ToArray();

            var title = matches.Count > SearchPickerLimit
                ? $"Showing the first {SearchPickerLimit} matches ÃÂ¢ÃÂÃÂ type more to narrow it down"
                : $"{shown.Count} matches for \"{typed}\"";

            var picked = await Application.Current.MainPage.DisplayActionSheet(
                title, "Cancel".Translate(), null, choices);

            // ÃÂ¢ÃÂÃÂ  Cancel ÃÂ¢ÃÂÃÂ and dismissing by tapping away, which returns null ÃÂ¢ÃÂÃÂ is ABANDONED, not
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
