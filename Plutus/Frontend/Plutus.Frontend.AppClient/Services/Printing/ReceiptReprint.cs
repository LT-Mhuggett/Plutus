using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Services.Printing
{
    /// <summary>
    /// Print another copy of a receipt for a sale already taken (cutover step 26).
    ///
    /// ⚠ WHY IT IS NEEDED AT ALL. A customer who has lost their receipt cannot be refunded: the
    /// barcode on the paper is what the refund flow looks the sale up by, and until 2026-08-10
    /// nothing in the app could list past sales either. The sale picker closed half of that; this
    /// closes the other half — the shop can hand over the paper again rather than telling somebody
    /// their purchase cannot be found.
    ///
    /// ⚠ THE COPY IS MARKED, and that is a money rule. A reprint indistinguishable from the
    /// original is a second receipt for one sale, and this till's own refund flow accepts a sale
    /// found by a receipt barcode. Two identical papers for one purchase is the shape of a double
    /// refund — which has already happened here once, from a different cause.
    ///
    /// ⚠ THE DRAWER DOES NOT OPEN. No money is moving. A reprint that kicks the drawer teaches
    /// operators that the drawer opening means nothing, which is worse than it not opening.
    /// </summary>
    internal static class ReceiptReprint
    {
        /// <summary>
        /// Pick a past sale from THIS till and print it again. Returns quietly if the operator
        /// backs out at any point.
        ///
        /// ⚠ Reads this till's OWN record, so it works with the line down — the same reasoning as
        /// the refund picker. A sale rung up on another till is the server's to know about, and
        /// reprinting somebody else's paperwork is not what this is for.
        /// </summary>
        public static async Task PickAndReprintAsync()
        {
            // ⚠ `purchasesOnly: false` — UNLIKE the refund picker, and deliberately. A refund is
            // its own sale with a negative gross, the customer gets a receipt for it, and they can
            // lose that one too. Refunding a refund is invalid; REPRINTING one is not.
            var recent = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.ListRecentSalesAsync(20, purchasesOnly: false));

            if (recent is not { Count: > 0 })
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This till hasn't recorded any sales yet, so there's nothing to reprint.",
                    "OK".Translate());
                return;
            }

            var labels = recent
                .Select(r => $"{r.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} · "
                           + $"{r.GrossPence / 100m:C}"
                           + (r.GrossPence < 0 ? " · REFUND" : "")
                           + $" · {r.LineCount} item{(r.LineCount == 1 ? "" : "s")}"
                           + (string.IsNullOrWhiteSpace(r.FirstItemIdOne) ? "" : $" · {r.FirstItemIdOne}"))
                .ToList();

            // ⚠⚠ THE SAME TWO-STEP SHAPE AS THE REFUND PICKER, and deliberately so — step 26 says
            // these are one screen with two callers. THIS TILL'S SALES FIRST, because they are the
            // common case and the only ones available with the line down; the platform lookup is an
            // explicit second step, so a reprint never depends on the network unless it has to.
            labels.Add(AnotherTill);

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayActionSheet(
                    "Which receipt?", "Cancel".Translate(), null, labels.ToArray()));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

            if (picked == AnotherTill)
            {
                if (await PickPlatformSaleAsync() is Guid fromPlatform)
                    await ReprintAsync(fromPlatform);
                return;
            }

            var index = labels.IndexOf(picked);
            if (index < 0 || index >= recent.Count) return;

            await ReprintAsync(recent[index].SaleId);
        }

        /// <summary>Print a copy of one known sale.</summary>
        public static async Task ReprintAsync(Guid saleId)
        {
            var stored = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.FindLocalSaleAsync(saleId));

            // ⚠⚠ CROSS-TILL REPRINT (step 26). This used to stop here with "it may have been rung up
            // on another till" — which is true, unhelpful, and precisely the half a customer asks
            // for at the counter: they bought it at the other branch and want their receipt.
            //
            // ⚠ The platform's record is a PROJECTION of the sale, not the payload this till sent —
            // `SaleDto` says so itself. For another till's sale that projection IS the authority, so
            // printing from it is right; what must not happen is deriving figures from anything else.
            var input = stored is not null
                ? await InputForAsync(stored)
                : await InputFromPlatformAsync(saleId);

            if (input is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That sale isn't on this till, and Plutus couldn't be reached to fetch it. "
                    + "Try again when the connection is back.",
                    "OK".Translate());
                return;
            }
            var status = await TillAgentPrinting.ResolveAsync();

            if (status is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "No printer is set up on this till. Set one up under Settings → Receipt printer.",
                    "OK".Translate());
                return;
            }

            var doc = ReceiptDocumentBuilder.Build(input with { Columns = status.Columns });
            var ok = await new TillAgentClient(Http).PrintAsync(doc, App.GetViewModel().TillAgentTokenSetting ?? "");

            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                ok ? "Sent to the printer." : "The printer didn't take it. Nothing has been changed.",
                "OK".Translate());
        }

        private const string AnotherTill = "Sold on another till — look it up in Plutus…";

        /// <summary>
        /// Pick a sale from the PLATFORM — the only path to another till's receipt.
        ///
        /// ⚠ A FORTNIGHT, not everything: reprints are overwhelmingly recent, and the server clamps
        /// `take` anyway. A picker holding months of sales is one nobody reads.
        ///
        /// ⚠ REFUNDS ARE INCLUDED HERE, unlike the refund picker's platform list. A refund is its own
        /// sale, the customer was handed a receipt for it, and they can lose that one too —
        /// reprinting a refund is legitimate where refunding one is not.
        /// </summary>
        private static async Task<Guid?> PickPlatformSaleAsync()
        {
            var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Looking up another till's sale needs someone signed in and a connection to "
                    + "Plutus. This till's own sales are in the previous list.", "OK".Translate());
                return null;
            }

            var today = Plutus.SharedKernel.BusinessDay.Today();
            var sales = await api.GetSalesAsync(today.AddDays(-14), today, tillId: null, take: 40);

            if (sales is null || sales.Count == 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    sales is null
                        ? "Plutus couldn't be reached, so other tills' sales can't be listed."
                        : "Plutus has no sales in the last fortnight.",
                    "OK".Translate());
                return null;
            }

            var thisTill = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.GetGuidMetaAsync(Plutus.Client.Storage.MetaKeys.TillId));

            var labels = sales
                .Select(s => $"{s.OccurredAtUtc.ToLocalTime():dd MMM HH:mm} · {s.GrossPence / 100m:C}"
                           + (s.GrossPence < 0 ? " · REFUND" : "")
                           // ⚠ Says WHOSE sale it is — without it the operator cannot tell a
                           // neighbouring till's from one of their own.
                           + (thisTill is Guid t && s.TillId == t ? " · this till" : " · another till"))
                .ToList();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayActionSheet(
                    "Which sale, from Plutus?", "Cancel".Translate(), null, labels.ToArray()));

            if (string.IsNullOrEmpty(picked) || picked == "Cancel".Translate()) return null;

            var index = labels.IndexOf(picked);
            return index >= 0 && index < sales.Count ? sales[index].Id : null;
        }

        // ⚠ One client for the app's lifetime — a new one per reprint leaks sockets into TIME_WAIT.
        private static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient();

        /// <summary>
        /// Build the paper for a sale THIS TILL NEVER TOOK, from the platform's record.
        ///
        /// ⚠ Returns null when the platform cannot be reached or does not know the sale — the caller
        /// says so rather than printing a blank receipt, which a customer would take as proof of a
        /// purchase nobody can find.
        ///
        /// ⚠⚠ EVERY FIGURE IS THE SERVER'S. Names, quantities, unit prices, discounts, tenders and
        /// the totals all come from the projection — nothing is re-derived from this till's catalogue.
        /// A reprint that priced from today's catalogue would hand a customer a different receipt
        /// from the one they were given, which is the whole reason the local path stores its payload.
        ///
        /// ⚠ It is marked as a COPY, like every reprint, and carries the platform sale id so the
        /// paper stays searchable.
        /// </summary>
        private static async Task<ReceiptDocInput> InputFromPlatformAsync(Guid saleId)
        {
            var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
            if (api is null) return null;

            var sale = await api.GetSaleAsync(saleId);
            if (sale is null) return null;

            var store = App.GetViewModel().Store ?? new Database.Models.StoreModel();

            var lines = sale.Lines
                .Select(l => new ReceiptDocLine(
                    // ⚠ The projection CARRIES the name, unlike the wire payload — so unlike the
                    // local path there is no catalogue lookup here, and nothing to go stale.
                    string.IsNullOrWhiteSpace(l.ItemName) ? l.ItemIdOne ?? "(item)" : l.ItemName,
                    Math.Abs(l.Qty),
                    l.UnitPricePence,
                    l.LineGrossPence,
                    // ⚠ A return line is NEGATIVE on the wire. The paper says so rather than
                    // printing a minus quantity nobody reads as a refund.
                    IsReturn: l.Qty < 0,
                    DiscountPence: l.DiscountPence))
                .ToList();

            var tenders = sale.Tenders
                .Select(t => new ReceiptDocTender(t.TenderType ?? "", t.AmountPence, t.ChangePence))
                .ToList();

            return new ReceiptDocInput
            {
                StoreName = store.StoreName,
                AddressLines = AddressOf(store),
                VatNumber = store.VatIN,
                Phone = store.ContactNumber,
                WhenLocal = sale.OccurredAtUtc.ToLocalTime(),
                OperatorName = sale.OperatorName,
                Lines = lines,
                ExPence = sale.GrossPence - sale.VatPence,
                GrossPence = sale.GrossPence,
                Tenders = tenders,
                SaleId = sale.Id.ToString("D"),
                IsReprint = true,
            };
        }

        /// <summary>The store's address lines, blanks dropped — a receipt with an empty line in the
        /// middle of the address looks misprinted.</summary>
        private static IReadOnlyList<string> AddressOf(Database.Models.StoreModel store) =>
            new[] { store.AdLine1, store.AdLine2, store.City, store.PostCode, store.Country }
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

        /// <summary>
        /// Rebuild the paper from the payload the platform ACCEPTED.
        ///
        /// ⚠ EVERY FIGURE IS THE STORED ONE. A reprint that re-derives totals from today's
        /// catalogue would hand a customer a different receipt from the one they were given —
        /// after a price change, a different total for the same purchase. The stored payload is the
        /// sale; nothing here is allowed to have a second opinion about it.
        /// </summary>
        internal static async Task<ReceiptDocInput> InputForAsync(IngestSaleRequest sale)
        {
            var store = App.GetViewModel().Store ?? new Database.Models.StoreModel();

            // ⚠ NAMES ARE LOOKED UP, because the wire does not carry them: `IngestLine` has an item
            // GUID and, in its meta, the barcode — no name anywhere. A receipt listing eight rows
            // of barcodes is not a receipt. ⚠ If the catalogue no longer holds the item (withdrawn,
            // binned) the BARCODE is printed rather than a blank: it is what the original showed
            // alongside the name and it still identifies the goods.
            var lines = new List<ReceiptDocLine>();
            foreach (var line in sale.Lines)
            {
                var meta = LineMeta.FromJson(line.DiscountsJson);
                var idOne = meta?.ItemIdOne ?? "";

                string name = idOne;
                if (!string.IsNullOrWhiteSpace(idOne))
                {
                    var item = await Services.Storage.TillStoreAccess.TryUseAsync(
                        s => s.FindByBarcodeAsync(idOne));
                    if (!string.IsNullOrWhiteSpace(item?.Name)) name = item.Name;
                }

                lines.Add(new ReceiptDocLine(
                    string.IsNullOrWhiteSpace(name) ? "Item" : name,
                    line.Qty,
                    line.UnitPricePence,
                    line.LineGrossPence,
                    // ⚠ A NEGATIVE LINE IS A RETURN. On a refund receipt every line is one, and the
                    // customer's copy must say so in words — the totals being negative is not
                    // something anyone should have to notice.
                    IsReturn: line.LineGrossPence < 0,
                    DiscountPence: line.DiscountPence,
                    DiscountName: line.DiscountPence > 0 ? "Discount" : null));
            }

            var address = string.IsNullOrWhiteSpace(store.FullAddress)
                ? new[] { store.AdLine1, store.AdLine2, store.PostCode, store.Country }
                : new[] { store.FullAddress };

            return new ReceiptDocInput
            {
                StoreName = store.StoreName,
                Phone = store.ContactNumber,
                AddressLines = address.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
                VatNumber = store.VatIN,
                // ⚠ THE ORIGINAL SALE'S TIME, not now. A reprint stamped with today's date is a
                // different claim about when the goods were bought, and returns policies run off it.
                WhenLocal = sale.OccurredAtUtc.ToLocalTime(),
                Lines = lines,
                Notes = string.IsNullOrWhiteSpace(sale.Note)
                    ? Array.Empty<string>()
                    : new[] { sale.Note },
                ExPence = sale.GrossPence - sale.VatPence,
                GrossPence = sale.GrossPence,
                Tenders = sale.Tenders
                    .Select(t => new ReceiptDocTender(TenderName(t.TenderType), t.AmountPence, t.ChangePence))
                    .ToList(),
                SaleId = sale.SaleId.ToString("N"),
                IsReprint = true,
            };
        }

        // ⚠ The payload carries a tender BYTE. A receipt reading "1" where the customer expects
        // "Card" is worse than useless, so the names are resolved from the shared constants — the
        // same mapping `ReceiptSale` uses, so an original and its reprint agree.
        private static string TenderName(byte tenderType) => tenderType switch
        {
            Plutus.SharedKernel.Tenders.Cash => "Cash",
            Plutus.SharedKernel.Tenders.Card => "Card",
            Plutus.SharedKernel.Tenders.Online => "Online",
            Plutus.SharedKernel.Tenders.Credit => "Store credit",
            Plutus.SharedKernel.Tenders.GiftCard => "Gift card",
            _ => "Payment",
        };
    }
}
