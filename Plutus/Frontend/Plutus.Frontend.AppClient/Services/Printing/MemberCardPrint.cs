using System;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Plutus.TillAgent.Core;

namespace Plutus.Frontend.AppClient.Services.Printing
{
    /// <summary>
    /// Print a customer their membership card, from the till (WP-L1c, §5d).
    ///
    /// ⚠⚠ MATT, 2026-08-18: *"Need to be able to print the card from the till."* — and then
    /// *"Build for both"*, so this is MAUI's half and the web till has its own.
    ///
    /// ⚠⚠ **THE TWO TILLS PRINT DIFFERENT PHYSICAL THINGS, AND THAT IS THE HONEST ANSWER.** The
    /// portal's card is **CR80** (85.6 × 54 mm) rendered in HTML and sent to an ordinary printer with
    /// `window.print()`, which the web till can do identically because it is a browser. **MAUI's
    /// printer is the thermal receipt printer on the counter** — there is no page printer behind it —
    /// so it prints a **scannable membership slip** instead.
    ///
    /// ⚠ That is parity in FUNCTIONALITY, not in implementation (Matt, 2026-08-17): the customer
    /// leaves with something carrying their number that a scanner reads. Pretending a thermal printer
    /// can produce a plastic card would be the lie. ⚠ **If a shop wants CR80 cards, they are printed
    /// from the portal or the web till onto card stock** — that path already exists and needs no
    /// hardware this estate does not have.
    ///
    /// ⚠ SAME SYMBOLOGY, SAME PAYLOAD. Code 39 with the `C` prefix, exactly as the portal's card and
    /// exactly what `MemberNumbers.LooksLikeMemberScan` expects — so a slip printed here scans as a
    /// MEMBER on any till, not as a product.
    /// </summary>
    internal static class MemberCardPrint
    {
        /// <summary>
        /// Build the slip. ⚠ Separated from the printing so it can be reasoned about (and, unlike the
        /// print itself, exercised) without hardware.
        /// </summary>
        /// <param name="name">Who it belongs to. A card with no name on it is a card nobody can hand back.</param>
        /// <param name="memberNo">Their membership number — the human-readable half.</param>
        /// <param name="tier">Their tier, if any.</param>
        /// <param name="renewalDay">When it lapses, if it does.</param>
        /// <param name="shopName">
        /// The shop, when the caller knows it — so the customer can tell whose card it is.
        ///
        /// ⚠ OPTIONAL, AND NOT LOOKED UP IN HERE. `ReceiptBranding` resolves a shop's name as part of
        /// building a whole receipt (`ApplyAsync` takes a `ReceiptDocInput` the caller has already
        /// filled in); there is no cheap "what is this shop called" accessor, and inventing a second
        /// path to a shop's name is exactly how two surfaces come to disagree about it. Blank is
        /// honest: the card came out of their till.
        /// </param>
        public static PrintDocument Build(
            string name, string memberNo, string tier, string renewalDay, string shopName = null)
        {
            var doc = new PrintDocument();

            if (!string.IsNullOrWhiteSpace(shopName))
                doc.Ops.Add(PrintOp.Line(shopName.Trim(), PrintAlign.Centre, bold: true, large: true));

            doc.Ops.Add(PrintOp.Line("Membership card", PrintAlign.Centre));
            doc.Ops.Add(PrintOp.Rule());

            doc.Ops.Add(PrintOp.Line(
                string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim(),
                PrintAlign.Centre, bold: true));

            if (!string.IsNullOrWhiteSpace(tier))
                doc.Ops.Add(PrintOp.Line(tier.Trim(), PrintAlign.Centre));

            doc.Ops.Add(PrintOp.Line());

            // ⚠⚠ THE BARCODE IS THE POINT OF THE SLIP. `BarcodePayload` prefixes the number with "C",
            // which is what makes a scan resolve to a MEMBER — `LooksLikeMemberScan` requires that
            // prefix AND a valid check character, so a bare number would scan as a product code.
            //
            // ⚠ Taller than the receipt default (80 → 100 dots): this one is meant to be scanned
            // repeatedly, from a wallet, for years.
            doc.Ops.Add(PrintOp.Barcode(MemberNumbers.BarcodePayload(memberNo), height: 100));

            // ⚠ THE NUMBER IN PRINT AS WELL AS IN BARS. Thermal paper fades and creases; a customer
            // whose barcode has stopped scanning can still read their number out, and an operator can
            // type it. The barcode itself is printed without its text line for the same reason the
            // portal's card is — the human copy sits here where it survives being folded.
            doc.Ops.Add(PrintOp.Line(memberNo, PrintAlign.Centre, bold: true));

            if (!string.IsNullOrWhiteSpace(renewalDay))
                doc.Ops.Add(PrintOp.Line($"valid to {renewalDay}", PrintAlign.Centre));

            doc.Ops.Add(PrintOp.Line());
            doc.Ops.Add(PrintOp.Line("Show this when you shop", PrintAlign.Centre));
            doc.Ops.Add(PrintOp.Cut());

            // ⚠ NO DRAWER KICK. This is not a sale — kicking the drawer to print a card would open
            // the till in front of a customer for no reason, and every drawer opening is a moment
            // somebody has to account for.
            return doc;
        }

        /// <summary>
        /// Build it and send it. ⚠ Returns false rather than throwing: a card that did not print is a
        /// disappointment, and it must never be able to take the till down with it.
        /// </summary>
        public static async Task<bool> PrintAsync(
            string name, string memberNo, string tier, string renewalDay)
        {
            if (string.IsNullOrWhiteSpace(memberNo)) return false;

            try
            {
                return await TillAgentPrinting.PrintDocumentAsync(Build(name, memberNo, tier, renewalDay))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("MemberCardPrint.PrintAsync", ex);
                return false;
            }
        }
    }
}
