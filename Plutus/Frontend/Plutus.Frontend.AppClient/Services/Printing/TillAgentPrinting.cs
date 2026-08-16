using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Database.Models;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Models;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Printing
{
    /// <summary>
    /// The MAUI till's receipt printer — the SAME one the web till uses.
    ///
    /// ⚠ THE WHOLE POINT IS THAT THERE IS NOW ONE HARDWARE ROUTE. Matt, 2026-08-10: *"I still
    /// cannot see a printer, it says wifi is turned off, but I do not understand what this means?
    /// The webtill can see the receipt printer fine."* Both halves of that had the same cause — the
    /// two tills were on different routes:
    ///
    ///   • The WEB till POSTs a rendered document to the Plutus Till Agent, a tray app on the till
    ///     PC, which prints through the ORDINARY WINDOWS PRINT QUEUE. Any printer this PC has a
    ///     driver for is one it can use. Hence "sees the receipt printer fine".
    ///   • The MAUI till asked Windows for a <c>PointOfService</c> device. That is a specialist
    ///     driver profile almost no receipt printer ships, so the list was empty — and the picker
    ///     Windows shows for it is the generic device chrome, which fills an empty list with a
    ///     complaint about Bluetooth and Wi-Fi Direct radios. ⚠ "Wireless is turned off" was never
    ///     about the printer. It is the picker answering a question nobody asked, which is exactly
    ///     why the screen was impossible to act on.
    ///
    /// So this class is not a better picker. It is the till stopping having a second route.
    ///
    /// ⚠ GRACEFUL DEGRADATION, the rule the web till states in `hardware.ts` and the reason nothing
    /// here throws: no agent, wrong code, printer off, agent wedged — every one returns false and
    /// the caller falls back to OPOS, or to nothing. A shop must be able to trade with a broken
    /// printer, and no receipt is worth losing a sale over.
    /// </summary>
    internal static class TillAgentPrinting
    {
        // ⚠ ONE HttpClient for the life of the app. A new one per sale leaks sockets into
        // TIME_WAIT, and on a till that rings hundreds of sales a day that eventually exhausts the
        // ephemeral port range — which presents as the printer "randomly" stopping working.
        private static readonly HttpClient Http = new HttpClient();

        private static TillAgentClient Client => new TillAgentClient(Http);

        private static string Token => App.GetViewModel().TillAgentTokenSetting ?? string.Empty;

        /// <summary>Has anyone paired this till with the agent? ⚠ Cheap and synchronous — safe to
        /// ask on the checkout path before spending 3 seconds on a probe.</summary>
        public static bool Paired => !string.IsNullOrWhiteSpace(Token);

        /// <summary>Is an agent here, and is it well? Null means no usable agent.</summary>
        public static Task<TillAgentStatus> StatusAsync() => Client.StatusAsync();

        /// <summary>The agent's own test strip — paper, alignment, bold, the £ sign, a barcode.</summary>
        public static Task<bool> TestPrintAsync() => Client.TestPrintAsync(Token);

        /// <summary>Kick the drawer on its own (a no-sale).</summary>
        public static Task<bool> OpenDrawerAsync() => Client.OpenDrawerAsync(Token);

        /// <summary>
        /// Print a committed sale, and kick the drawer with the same job when it is a cash sale.
        ///
        /// ⚠ THE DRAWER RIDES WITH THE RECEIPT. One round trip, and the drawer opens as the paper
        /// starts moving — which is what an operator expects and what the OPOS path did. Two calls
        /// would open it a second or so after the receipt, which reads as a fault.
        ///
        /// Returns false if nothing was printed, so the caller can fall back.
        /// </summary>
        public static async Task<bool> TryPrintSaleAsync(
            ReceiptSale sale, IEnumerable<IBasketRecord> basket, StoreModel store, bool openDrawer,
            TillAgentStatus status)
        {
            // ⚠ THE STATUS IS PASSED IN, NOT PROBED HERE, and that is a correctness point rather
            // than a saved round trip. The checkout has to decide BEFORE it dispatches anything
            // whether the agent or OPOS is opening the drawer. Deciding inside this method would
            // leave the caller racing its own fallback and the drawer could be kicked twice — or,
            // on a printer that treats a second kick as a fault, not at all.
            if (sale is null || status is null || !Paired) return false;

            // ⚠ The portal's receipt layout is applied here, from the SAME overlay the two reprint
            // paths use — one shop, one receipt, whichever door the paper came out of.
            var branded = await ReceiptBranding.ApplyAsync(
                InputFor(sale, basket, store, status.Columns, openDrawer && status.DrawerSupported));

            var doc = ReceiptDocumentBuilder.Build(branded);

            return await Client.PrintAsync(doc, Token);
        }

        /// <summary>The status this till would print through, or null when there is no usable agent.
        /// ⚠ Costs nothing on a till nobody has paired — which is every till until someone does.</summary>
        public static Task<TillAgentStatus> ResolveAsync() => Paired ? StatusAsync() : Task.FromResult<TillAgentStatus>(null);

        /// <summary>
        /// Turn the till's own types into the shared receipt description.
        ///
        /// ⚠ THE MONEY IS THE COMMITTED PAYLOAD'S, never a re-sum of the basket. The basket supplies
        /// the LINES — what was bought — and `sale` supplies every TOTAL. Re-adding the basket for
        /// the totals would give the customer's paper a second opinion about the sale, and a penny
        /// of disagreement on a receipt is the one piece of evidence a chargeback turns on.
        /// </summary>
        internal static ReceiptDocInput InputFor(
            ReceiptSale sale, IEnumerable<IBasketRecord> basket, StoreModel store,
            int columns, bool openDrawer)
        {
            // ⚠ A missing store must not lose the receipt. `EnsureStoreAsync` has five paths that
            // deliberately leave `Store` null rather than block sign-in, and the OPOS printer used
            // to dereference it six times — a NullReferenceException AFTER the sale was committed,
            // which cleared nothing and showed nothing, so the operator rang the sale again.
            store ??= new StoreModel();

            var lines = new List<ReceiptDocLine>();
            foreach (var record in basket ?? Enumerable.Empty<IBasketRecord>())
            {
                if (record is BasketReturnItem ret)
                {
                    // ⚠ `Math.Abs` on the unit price, and IsReturn carries the meaning. The paper
                    // says "RETURN —" in words; a bare minus sign in a price column is not something
                    // a customer, or a manager reading the shop's copy months later, should have to
                    // spot. The TOTAL still goes out negative, which is what reconciles.
                    lines.Add(new ReceiptDocLine(
                        ret.Name, ret.Quantity,
                        Pence.FromDecimal(Math.Abs(ret.Price)),
                        -Pence.FromDecimal(Math.Abs(ret.Price) * ret.Quantity),
                        IsReturn: true));
                }
                else if (record is BasketItem item)
                {
                    lines.Add(new ReceiptDocLine(
                        item.Name, item.Quantity,
                        Pence.FromDecimal(item.Price),
                        Pence.FromDecimal(item.Price * item.Quantity)));
                }
                // ⚠ Notes are NOT lines. They already arrive on `sale.Notes` from the committed
                // payload; printing them twice would put a customer's note in the middle of the
                // priced lines, where it reads like an item costing nothing.
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
                WhenLocal = sale.WhenLocal,
                OperatorName = App.GetViewModel().SignedInOperator?.DisplayName,
                Lines = lines,
                Notes = sale.Notes?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                ExPence = sale.ExPence,
                GrossPence = sale.GrossPence,
                Tenders = sale.Tenders
                    .Select(t => new ReceiptDocTender(t.Name, t.AmountPence, t.ChangePence))
                    .ToList(),
                // ⚠ "N" — 32 hex characters, no dashes. Code 39 cannot encode a dash-free GUID any
                // other way round, and this string is what a reprint or a receipt-led refund looks
                // the sale up by. The web till upper-cases the same value.
                SaleId = sale.SaleId.ToString("N"),
                Columns = columns,
                OpenDrawer = openDrawer,
            };
        }
    }
}
