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
                .ToArray();

            var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayActionSheet(
                    "Which receipt?", "Cancel".Translate(), null, labels));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

            var index = Array.IndexOf(labels, picked);
            if (index < 0 || index >= recent.Count) return;

            await ReprintAsync(recent[index].SaleId);
        }

        /// <summary>Print a copy of one known sale.</summary>
        public static async Task ReprintAsync(Guid saleId)
        {
            var stored = await Services.Storage.TillStoreAccess.TryUseAsync(
                s => s.FindLocalSaleAsync(saleId));

            if (stored is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This till doesn't have that sale — it may have been rung up on another till.",
                    "OK".Translate());
                return;
            }

            var input = await InputForAsync(stored);
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

        // ⚠ One client for the app's lifetime — a new one per reprint leaks sockets into TIME_WAIT.
        private static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient();

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
