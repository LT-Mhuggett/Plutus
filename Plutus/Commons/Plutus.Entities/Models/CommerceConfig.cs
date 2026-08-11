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

    /// <summary>
    /// The till build the platform expects each surface to be running — GLOBAL single row (Id
    /// always 1), like <see cref="BillingSettings"/>.
    ///
    /// ⚠ Matt, 2026-08-11: *"Does the heartbeat from the till check for updates? All tills should do
    /// this."* It did not — the heartbeat carried no version at all. This is the reference point it
    /// now returns, and the reason it is a SETTING rather than something derived: the platform must
    /// not start nagging forty tills the moment a build is published. Somebody decides when a
    /// release becomes "the one you should be on", and that somebody is a platform admin.
    ///
    /// ⚠ **PLATFORM-GLOBAL, NOT TENANT-OWNED** — deliberately absent from the `TenantOwned` array.
    /// A till build is the operator's release decision, not a shop's; a tenant cannot pin itself to
    /// an old till, and one tenant's upgrade window is not another's problem to configure.
    ///
    /// ⚠ **BLANK MEANS "SAY NOTHING"**, and that is the safe default this ships with. An empty
    /// expected version disables the prompt entirely rather than comparing against "" — so the
    /// feature is inert until somebody deliberately turns it on, and a fresh install never greets
    /// its owner with an upgrade banner it cannot act on.
    ///
    /// ⚠ **THIS DOES NOT GATE ANYTHING.** It is advisory: a till below the expected version still
    /// sells, still takes money, still drains. Matt, 2026-08-11: *"No self update for MAUI"* — the
    /// till is an unpackaged .exe that cannot fetch its own replacement, so refusing to work would
    /// strand a shop with no route out. The `426 Upgrade Required` gate remains deferred (WP5, risk
    /// #6) and would be a separate, deliberate decision.
    /// </summary>
    public class TillReleaseSettings
    {
        public byte Id { get; set; } = 1;

        /// <summary>The MAUI till build tills should be on. Blank = no prompt.</summary>
        public string ExpectedMauiVersion { get; set; }

        /// <summary>The web till build. ⚠ Blank by default and expected to STAY blank: the web till
        /// already detects a new deploy exactly, by comparing its running bundle hash against the
        /// one the server serves. This exists so the two surfaces are configured in one place if
        /// that ever changes — not because the browser needs telling.</summary>
        public string ExpectedWebVersion { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }
}
