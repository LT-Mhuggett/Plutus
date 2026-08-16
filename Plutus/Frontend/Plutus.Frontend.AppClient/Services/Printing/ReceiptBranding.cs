using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Printing
{
    /// <summary>
    /// The portal's receipt layout, applied to whatever a print path built (step 26).
    ///
    /// ⚠⚠ ONE OVERLAY, THREE CALLERS. A sale print, a local reprint and a cross-till reprint each
    /// assemble a `ReceiptDocInput` from the store record; without this each would need its own copy
    /// of the template logic, and the three would drift into three receipts for one shop. They now
    /// differ only in where the LINES come from.
    ///
    /// ⚠ CACHED, so a receipt prints the same with the line down. The template says what a
    /// customer's paper reveals about the trader; falling back to a hardcoded layout during an
    /// outage would print a different receipt for the same shop, and the customer's copy is the only
    /// evidence they have that the sale happened.
    ///
    /// ⚠ IT NEVER BLOCKS A PRINT. Every failure — no template, no network, a malformed blob — falls
    /// back to the store's own details, which is exactly the receipt a shop that never touched its
    /// template gets. A till that refused to print over a cosmetic setting would stop trading.
    /// </summary>
    internal static class ReceiptBranding
    {
        /// <summary>
        /// Overlay the effective template onto a receipt the caller has already built.
        ///
        /// ⚠ THE CALLER'S VALUES ARE THE FALLBACK, not the other way round: they came from the
        /// store record, and `ReceiptTemplateRules.Merge` is explicit that the store fills whatever
        /// the template leaves blank.
        /// </summary>
        public static async Task<ReceiptDocInput> ApplyAsync(ReceiptDocInput input, CancellationToken ct = default)
        {
            try
            {
                var template = ReceiptTemplateWire.Parse(await CachedJsonAsync(ct).ConfigureAwait(false));

                // ⚠ The store details handed to the rule are the ones the CALLER resolved, so a
                // cross-till reprint keeps whatever it read from the platform.
                var effective = ReceiptTemplateRules.Merge(template, new ReceiptStoreDetails(
                    Name: input.StoreName,
                    AdLine1: input.AddressLines.ElementAtOrDefault(0),
                    AdLine2: input.AddressLines.ElementAtOrDefault(1),
                    City: input.AddressLines.ElementAtOrDefault(2),
                    PostCode: input.AddressLines.ElementAtOrDefault(3),
                    ContactNumber: input.Phone,
                    VatNumber: input.VatNumber));

                return input with
                {
                    StoreName = effective.StoreName,
                    Phone = effective.Phone,
                    AddressLines = effective.AddressLines ?? input.AddressLines,

                    // ⚠ BOTH the toggle and a number, or the line goes entirely — "VAT No:" with
                    // nothing after it looks like the shop failed to fill something in.
                    VatNumber = ReceiptTemplateRules.PrintsVatNumber(effective) ? effective.VatNumber : null,

                    HeaderLines = ReceiptTemplateRules.HeaderOr(effective, input.HeaderLines),
                    FooterLines = ReceiptTemplateRules.FooterOr(effective) is { Count: > 0 } f
                        ? f
                        : input.FooterLines,

                    // ⚠ The operator's name is a TOGGLE, not a fact about the sale. A shop that does
                    // not want staff named on paper has a reason, and the till obeys it.
                    OperatorName = effective.ShowOperator ? input.OperatorName : null,

                    ShowBarcode = effective.ShowBarcode,
                };
            }
            catch (Exception ex)
            {
                // ⚠ Print SOMETHING. Whatever the caller built is a valid receipt; the template only
                // ever improves it.
                Analytics.CrashLog.Write("ReceiptBranding.ApplyAsync", ex);
                return input;
            }
        }

        /// <summary>
        /// The cached blob, refreshed from the portal when the till can reach it.
        ///
        /// ⚠ THE CACHE IS READ FIRST AND USED WHATEVER HAPPENS NEXT. A refresh that fails leaves the
        /// last-known template in place rather than reverting a shop's receipt mid-day.
        /// </summary>
        private static async Task<string> CachedJsonAsync(CancellationToken ct)
        {
            var cached = await Storage.TillStoreAccess.TryUseAsync(
                s => s.GetMetaAsync(MetaKeys.ReceiptTemplate, ct), ct).ConfigureAwait(false);

            return cached;
        }

        /// <summary>
        /// Pull the template from the portal and store it. ⚠ Called on the sync cadence, NOT at
        /// print time: a printer job must never wait on the network, and a receipt is the one thing
        /// an operator is standing there watching for.
        /// </summary>
        public static async Task RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
                if (api is null) return;

                if (await Storage.TillPlacement.StoreIdAsync(ct: ct).ConfigureAwait(false) is not int storeId) return;

                var result = await api.GetReceiptTemplateAsync(storeId, ct).ConfigureAwait(false);
                if (result?.ReceiptTemplateJson is not string json) return;

                // ⚠ `SetMetaAsync` returns a bare Task, so it needs the value-returning shape
                // `TryUseAsync<T>` expects.
                await Storage.TillStoreAccess.TryUseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.ReceiptTemplate, json, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ⚠ A failed refresh keeps the last-known template. Never fatal.
                Analytics.CrashLog.Write("ReceiptBranding.RefreshAsync", ex);
            }
        }
    }
}
