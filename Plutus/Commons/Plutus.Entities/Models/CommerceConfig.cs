using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// 16.4 billing-provider selection — GLOBAL single row (Id always 1): the operator's
    /// platform-wide choice of Stripe/Paddle/Chargebee/manual plus its config (secrets in
    /// ConfigJson are write-only via the API, redacted on read). "manual" = no automation.
    /// </summary>
    public class BillingSettings
    {
        public byte Id { get; set; } = 1;
        public string Provider { get; set; } = "manual";
        public string ConfigJson { get; set; }
        public bool Enabled { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }

    /// <summary>
    /// 17.2 per-tenant payment gateway — TENANT-OWNED (each client picks their own; managed from
    /// the client portal under portal.company.manage). One row per tenant; absent row ⇒
    /// "standalone" (external chip &amp; pin, cashier confirms — today's flow). The till reads only
    /// the provider key/label, never the config.
    /// </summary>
    public class PaymentGatewaySettings
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Provider { get; set; } = "standalone";
        public string ConfigJson { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }

        /// <summary>
        /// Card surcharge, percentage half, in BASIS POINTS of the basket's sale value (169 = 1.69%).
        /// Zero = no surcharge, and zero is the default because surcharging CONSUMER cards has been
        /// banned in the UK since 13 January 2018 (Consumer Rights (Payment Surcharges) Regulations
        /// 2012) — the setting exists for B2B tenants and other jurisdictions, and the portal's
        /// helper text carries the warning.
        ///
        /// ⚠ Percent + flat TOGETHER, because that is the shape of every acquirer's own fee
        /// (1.69% + 20p is how the merchant is charged), and lawful surcharging is capped at cost
        /// recovery — a tenant passing through their real cost needs both halves.
        /// </summary>
        public int SurchargeBp { get; set; }

        /// <summary>Card surcharge, flat half, in integer pence. See <see cref="SurchargeBp"/>.</summary>
        public long SurchargeFlatPence { get; set; }
    }
}
