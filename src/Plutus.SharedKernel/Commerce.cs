using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>A selectable provider whose channel-less config the operator (billing) or tenant
/// (payment gateway) fills in. Reuses <see cref="ProviderField"/> (secret fields are write-only).</summary>
public sealed record CommerceProviderInfo(string Key, string Label, string Blurb, IReadOnlyList<ProviderField> Fields);

/// <summary>
/// 16.4 billing-provider catalogue — code-defined, one platform-wide selection made by the
/// operator. "manual" = no automation (you invoice tenants yourself); the other three become
/// active when their concrete <c>IBillingProvider</c> adapter is wired (webhook-driven dunning:
/// payment failed → grace → PastDue/Suspended, per D16 tills keep selling). Config metadata only —
/// no SDKs in core.
/// </summary>
public static class BillingProviderCatalogue
{
    private static ProviderField Text(string n, string l) => new(n, l, false, true);
    private static ProviderField Secret(string n, string l) => new(n, l, true, true);

    public static readonly IReadOnlyList<CommerceProviderInfo> All = new List<CommerceProviderInfo>
    {
        new("manual", "Manual (no automation)", "You invoice tenants yourself; no provider webhooks, no automated dunning.", Array.Empty<ProviderField>()),
        new("stripe", "Stripe Billing", "Subscriptions + dunning via Stripe; webhooks drive grace → suspended.", new[] { Secret("secretKey", "Secret key (sk_…)"), Secret("webhookSecret", "Webhook signing secret (whsec_…)") }),
        new("paddle", "Paddle", "Merchant-of-record (Paddle handles VAT/invoicing); webhooks drive dunning.", new[] { Text("sellerId", "Seller/vendor id"), Secret("apiKey", "API key"), Secret("webhookSecret", "Webhook secret") }),
        new("chargebee", "Chargebee", "Subscription management; webhooks drive dunning.", new[] { Text("site", "Site name"), Secret("apiKey", "API key"), Secret("webhookSecret", "Webhook secret") }),
    };

    public static CommerceProviderInfo? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 17.2 payment-gateway catalogue — chosen PER TENANT (clients differ), managed from the client
/// portal. "standalone" is the default and a first-class option: payment is taken on an external
/// chip &amp; pin terminal with no integration, and the cashier confirms approval on the till —
/// exactly today's flow, now explicit. Integrated gateways become live when their adapter is
/// wired; until then the till shows the selection but the flow stays standalone (selling never
/// blocks on a missing integration).
/// </summary>
public static class PaymentProviderCatalogue
{
    public const string Standalone = "standalone";

    private static ProviderField Text(string n, string l) => new(n, l, false, true);
    private static ProviderField Secret(string n, string l) => new(n, l, true, true);

    public static readonly IReadOnlyList<CommerceProviderInfo> All = new List<CommerceProviderInfo>
    {
        new(Standalone, "Standalone terminal (no integration)", "Take payment on your own chip & pin machine, then confirm approval on the till. The default.", Array.Empty<ProviderField>()),
        new("stripe-terminal", "Stripe Terminal", "Integrated card-present via Stripe readers.", new[] { Secret("secretKey", "Secret key (sk_…)"), Text("locationId", "Terminal location id") }),
        new("sumup", "SumUp", "Integrated card-present via SumUp readers.", new[] { Secret("apiKey", "API key"), Text("merchantCode", "Merchant code") }),
        new("square", "Square", "Integrated card-present via Square terminals.", new[] { Secret("accessToken", "Access token"), Text("locationId", "Location id") }),
        new("adyen", "Adyen", "Integrated card-present via Adyen terminals.", new[] { Secret("apiKey", "API key"), Text("merchantAccount", "Merchant account") }),
        new("worldpay", "Worldpay", "Integrated payments via Worldpay.", new[] { Secret("serviceKey", "Service key"), Text("merchantId", "Merchant id") }),
    };

    public static CommerceProviderInfo? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
