namespace Plutus.SharedKernel;

/// <summary>
/// The tender, channel and adjustment values as they appear ON THE WIRE, named once so a till does
/// not hand-code them.
///
/// ⚠ CONSTANTS, NOT ENUMS, AND THAT IS DELIBERATE. The obvious move — declaring
/// <c>TenderType</c>/<c>SaleChannel</c> enums here — was tried first and reverted: those type names
/// already exist in <c>Plutus.Entities.Models.SalesV2</c>, and **69 backend files import both that
/// namespace and this one**, so every use became `CS0104: ambiguous reference`. Renaming the
/// backend's copy instead would change the CLR type of mapped EF properties and move the model
/// snapshot — a `PendingModelChangesWarning` on a live database, which is the failure that took
/// the test backend down on 2026-08-09.
///
/// The client never needed the type anyway: <c>IngestTender.TenderType</c> is a <c>byte</c> on the
/// wire. What it needed was the VALUES and the NAME MAPPING, which is what this provides —
/// pinned to the backend enum and the web till by <c>TenderTypeParityTests</c>.
///
/// ⚠ THESE VALUES ARE THE WIRE. They sit on byte columns and every payment-split report groups by
/// them. Only ever append: renumbering does not break a build, it silently re-labels history.
/// </summary>
public static class Tenders
{
    public const byte Cash = 0;
    public const byte Card = 1;
    /// <summary>Taken online — webstore orders ingest as this.</summary>
    public const byte Online = 2;
    /// <summary>Store credit drawn from a customer's account.</summary>
    public const byte Credit = 3;
    /// <summary>FE7. Redeemed against a gift card's balance. ⚠ Deliberately NOT <see cref="Credit"/>:
    /// different liability, different reconciliation, and the payment-split report groups by this
    /// value — lumping them together misstates both.</summary>
    public const byte GiftCard = 4;

    /// <summary>
    /// Name → wire byte, shared so the tills cannot disagree about which byte a payment method is.
    ///
    /// ⚠ THIS IS A C2 TWIN AND THE ORDER OF THE TESTS IS LOAD-BEARING. It mirrors the web till's
    /// `tenderTypeFor` (`api.ts`), where **gift card is checked BEFORE credit** — "Gift card"
    /// contains neither "cash" nor "online", so a naive chain drops it into the store-credit bucket
    /// because nothing else matches before the fallback. That one line is the difference between a
    /// gift-card redemption reconciling against the right liability and quietly inflating store
    /// credit.
    ///
    /// ⚠ <see cref="Card"/> is the FALLBACK, matching the web till: an unrecognised method name on
    /// a busy counter should complete the sale as a card payment rather than refuse it. A misfiled
    /// tender is a reporting correction; a refused sale is a customer walking out.
    /// </summary>
    /// <summary>
    /// Wire name → byte, STRICTLY: unrecognised names are refused rather than guessed.
    ///
    /// ⚠⚠ USE THIS, NOT <see cref="FromMethodName"/>, WHEN READING A SERVER PAYLOAD. That one falls
    /// back to <see cref="Card"/> on purpose, because a cashier typing an unrecognised method name
    /// should still be able to complete a sale. Applied to a wire value the same leniency is a
    /// liability: a tender name this build does not know would be counted as CARD, and on the refund
    /// path (finding Y) that inflates the card's refundable capacity by somebody else's money.
    ///
    /// ⚠ Matches the `TenderType` enum's own names, which is what `GET /api/v1/sales/{saleId}`
    /// serialises — `"Cash"`, `"Card"`, `"Online"`, `"Credit"`, `"GiftCard"`.
    /// </summary>
    public static bool TryFromWireName(string? wireName, out byte tenderType)
    {
        tenderType = 0;
        if (string.IsNullOrWhiteSpace(wireName)) return false;

        switch (wireName.Trim().ToLowerInvariant())
        {
            case "cash": tenderType = Cash; return true;
            case "card": tenderType = Card; return true;
            case "online": tenderType = Online; return true;
            case "credit": tenderType = Credit; return true;
            case "giftcard": tenderType = GiftCard; return true;
            default: return false;
        }
    }

    public static byte FromMethodName(string? methodName)
    {
        var n = methodName?.ToLowerInvariant() ?? string.Empty;
        if (n.Contains("cash")) return Cash;
        if (n.Contains("online")) return Online;
        // ⚠ BEFORE credit — see the remarks. Do not reorder.
        if (n.Contains("gift")) return GiftCard;
        if (n.Contains("credit")) return Credit;
        return Card;
    }
}

/// <summary>How a sale reached the platform. ⚠ Wire values; see <see cref="Tenders"/>.</summary>
public static class SaleChannels
{
    /// <summary>A physical till — the MAUI app or the browser till at a counter.</summary>
    public const byte Till = 0;
    /// <summary>The browser till used away from a counter.</summary>
    public const byte WebPos = 1;
    /// <summary>An order that came in from the webstore connector.</summary>
    public const byte WebStore = 2;
}

/// <summary>What a sale adjustment is. ⚠ Wire values; see <see cref="Tenders"/>.</summary>
public static class Adjustments
{
    public const byte Refund = 0;
    public const byte Void = 1;
}
