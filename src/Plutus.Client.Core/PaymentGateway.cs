using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>How the operator is going to take a card payment.</summary>
public enum CardFlow
{
    /// <summary>The operator uses their own chip &amp; pin machine and confirms approval on the
    /// till. ⚠ The DEFAULT and the fallback — it is the flow that works when nothing else does,
    /// because the only thing it depends on is a human.</summary>
    Standalone = 0,

    /// <summary>The till drives the terminal and waits for its result. ⚠ Requires a wired
    /// integration; nothing on the platform has one yet.</summary>
    Integrated = 1,
}

/// <summary>
/// What the checkout screen should say and do about cards.
/// </summary>
/// <param name="Flow">Which flow to run.</param>
/// <param name="Label">Provider name for the screen, e.g. "Worldpay". Never null.</param>
/// <param name="PendingIntegration">The tenant has chosen a real provider but no integration is
/// wired yet, so the operator still confirms manually — the checkout must SAY so, or a cashier
/// looking at "Card via Worldpay" waits for a terminal prompt that is never coming.</param>
public sealed record CardPaymentDisplay(CardFlow Flow, string Label, bool PendingIntegration);

/// <summary>
/// WP14 — payment-gateway awareness.
///
/// ⚠ THE RULE IS NOT "read <c>Provider</c>". Every provider today reports
/// <c>Integrated: false</c>, so a till that switched on the provider name alone would sit waiting
/// for a terminal that will never respond, with a customer in front of it. What decides the flow is
/// <c>Integrated</c>; the provider only decides the wording.
///
/// ⚠ In Client.Core because both tills must reach the same conclusion from the same response — this
/// mirrors the web till's `CheckoutDialog.tsx`, where the same three cases are decided inline. Two
/// tills disagreeing here means one of them hangs at the payment step.
/// </summary>
public static class PaymentGateway
{
    /// <summary>Shown when we have nothing better. ⚠ Not "Unknown": the operator does not care what
    /// the till failed to fetch, they care which machine to reach for.</summary>
    public const string StandaloneLabel = "your card terminal";

    /// <summary>
    /// Turn the server's answer into a decision. Never throws.
    ///
    /// ⚠ NULL IS A NORMAL INPUT and it means standalone, not "error". A till that has never been
    /// online, one whose poll just failed, and one belonging to a tenant that picked the standalone
    /// default are all the same thing to a cashier: take payment on the machine and confirm it.
    /// Blocking card sales because a *cosmetic* lookup failed would be a self-inflicted outage.
    /// </summary>
    public static CardPaymentDisplay Resolve(ActiveGatewayDto? gateway)
    {
        // Standalone whenever there is no answer, no provider, or the provider IS standalone.
        // ⚠ The constant comes from SharedKernel.PaymentProviderCatalogue rather than a literal
        // here — the server compares against the same one, so a rename cannot leave the two sides
        // silently disagreeing about which key means "no integration".
        if (gateway is null || string.IsNullOrWhiteSpace(gateway.Provider)
            || string.Equals(gateway.Provider.Trim(), PaymentProviderCatalogue.Standalone,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CardPaymentDisplay(CardFlow.Standalone, StandaloneLabel, PendingIntegration: false);
        }

        var label = string.IsNullOrWhiteSpace(gateway.Label) ? gateway.Provider.Trim() : gateway.Label.Trim();

        // ⚠ A chosen provider with no wired integration is STILL the standalone flow. The only
        // difference is that the screen names the provider and says the integration is pending, so
        // the cashier knows to use the terminal by hand rather than wait.
        return gateway.Integrated
            ? new CardPaymentDisplay(CardFlow.Integrated, label, PendingIntegration: false)
            : new CardPaymentDisplay(CardFlow.Standalone, label, PendingIntegration: true);
    }

    /// <summary>Fetch and resolve in one step. Never throws — a failed lookup resolves to
    /// standalone, which is exactly what the till would do anyway.</summary>
    public static async Task<CardPaymentDisplay> FetchAsync(
        PlutusApiClient api, CancellationToken ct = default)
    {
        if (api is null) throw new ArgumentNullException(nameof(api));
        try
        {
            return Resolve(await api.GetActiveGatewayAsync(ct).ConfigureAwait(false));
        }
        catch
        {
            return Resolve(null);
        }
    }
}
