using System;
using System.Collections.Generic;

namespace Plutus.SharedKernel;

/// <summary>One rule the platform actually applies, with the HMRC source it comes from.</summary>
/// <param name="Id">Stable slug — the portal anchors links on it, so don't rename one lightly.</param>
/// <param name="Title">The rule in a line.</param>
/// <param name="WhatPlutusDoes">What the CODE does. If this stops being true, the code or this
/// text is wrong — one of them must change.</param>
/// <param name="Where">Where in Plutus it happens, so the claim is checkable.</param>
/// <param name="Source">The citation, as a person would write it on a query letter.</param>
/// <param name="Url">The HMRC page.</param>
public sealed record VatRule(string Id, string Title, string WhatPlutusDoes, string Where, string Source, string Url);

/// <summary>
/// WP2c — the VAT rules Plutus applies, in one place, with citations.
///
/// WHY THIS IS CODE AND NOT A WIKI PAGE: every rule below is implemented somewhere in this repo,
/// and a rule that drifts from its implementation is worse than no rule at all — it is a document
/// that will be believed. Keeping the text next to the constants means a change to the behaviour
/// lands in the same commit as the change to the explanation, and the portal renders THIS rather
/// than a hand-written copy that nobody re-reads.
///
/// ⚠ This describes what the software does. It is not tax advice, and the accountant's decisions
/// (which retail scheme, whether a historical error is corrected on the next return or by
/// disclosure) belong to the business, not to the platform.
/// </summary>
public static class VatGuidance
{
    /// <summary>The one-line basis the VAT return, the published band contract and the portal all
    /// quote — so a till, a report and a screen can never describe the rule differently.</summary>
    public const string PricingBasis =
        "Prices are VAT-inclusive; VAT is the fraction of the gross, rounded to the nearest penny "
        + "(HMRC Notice 700 §17, VATREC12020 — the round-down concession is not available to retailers).";

    /// <summary>The output-tax basis quoted on the VAT return itself. ⚠ Notice 727/3 §4.2 states
    /// that its calculation steps HAVE THE FORCE OF LAW — this is not a preference between
    /// acceptable methods.</summary>
    public const string ReturnBasis =
        "HMRC Notice 727 §3.4.1 / Notice 727/3 §4.1 — VAT fraction applied to takings at each rate "
        + "(Point of Sale retail scheme).";

    // ── HMRC error-correction thresholds (Notice 700/45 §4) ────────────────────────────────────
    // Below the threshold AND not careless/deliberate → adjust on the next return. Otherwise
    // (or by choice) → separate disclosure on form VAT652. The threshold is the GREATER of
    // £10,000 and 1% of Box 6, capped at £50,000.

    /// <summary>The floor of the reporting threshold: £10,000 net error.</summary>
    public const long ErrorCorrectionFloorPence = 10_000_00;

    /// <summary>The ceiling: 1% of Box 6 never takes the threshold above £50,000.</summary>
    public const long ErrorCorrectionCeilingPence = 50_000_00;

    /// <summary>1% of Box 6 (VAT-exclusive turnover), as a fraction.</summary>
    public const decimal ErrorCorrectionTurnoverFraction = 0.01m;

    /// <summary>
    /// The Notice 700/45 threshold for a period whose Box 6 (net outputs) is
    /// <paramref name="boxSixPence"/>: the greater of £10,000 and 1% of Box 6, capped at £50,000.
    /// </summary>
    public static long ErrorCorrectionThresholdPence(long boxSixPence)
    {
        var onePercent = (long)Math.Round(Math.Abs(boxSixPence) * ErrorCorrectionTurnoverFraction, MidpointRounding.AwayFromZero);
        return Math.Min(Math.Max(ErrorCorrectionFloorPence, onePercent), ErrorCorrectionCeilingPence);
    }

    /// <summary>
    /// Which correction route a net error takes. ⚠ This answers only the ARITHMETIC half of the
    /// test. The other half — was the error careless or deliberate? — is a judgement the business
    /// makes, and a careless error must be disclosed on VAT652 however small it is.
    /// </summary>
    public static string ErrorCorrectionRoute(long netErrorPence, long boxSixPence) =>
        Math.Abs(netErrorPence) <= ErrorCorrectionThresholdPence(boxSixPence)
            ? "adjust-next-return"
            : "vat652";

    /// <summary>
    /// The rules, in the order a person would want to read them: how a price becomes VAT, how a
    /// return is computed, which moment governs, and how the classes differ.
    /// </summary>
    public static readonly IReadOnlyList<VatRule> Rules = new[]
    {
        new VatRule(
            "inclusive-pricing",
            "Prices include VAT; the net figure is derived from them",
            "Every price in the catalogue is the VAT-INCLUSIVE price the customer pays. The ex-VAT "
            + "figure is calculated from it and rounded to the nearest penny — never the other way "
            + "round. So the shelf price is always exactly what is charged, and rounding can only "
            + "ever move the net/VAT split by a penny, not the amount taken.",
            "Item editors (portal + till); Plutus stores Price and ExPrice as integer pence.",
            "HMRC Notice 700 §17 (calculating VAT) and VAT Notice 700 §7 (VAT-inclusive prices)",
            "https://www.gov.uk/guidance/vat-guide-notice-700"),

        new VatRule(
            "rounding",
            "VAT is rounded to the nearest penny, up or down",
            "Plutus rounds to the nearest penny in both directions, with halves going away from "
            + "zero. HMRC's concession allowing VAT to be rounded DOWN is explicitly not available "
            + "to retailers, so the platform does not offer it.",
            "VatAccounting.OutputTaxOn — MidpointRounding.AwayFromZero.",
            "HMRC VAT Trader Records Manual VATREC12020 (rounding at retailers): \"As a general "
            + "rule the concession to round down is not appropriate for retailers.\"",
            "https://www.gov.uk/hmrc-internal-manuals/vat-trader-records/vatrec12020"),

        new VatRule(
            "vat-fraction-on-takings",
            "The return applies the VAT fraction to takings — it does not add up the lines",
            "Output tax for a period is the VAT fraction (rate ÷ (100 + rate); one sixth at 20%) "
            + "applied to the total takings at each rate. It is NOT the sum of each line's VAT: "
            + "every line is rounded to the penny, and thousands of roundings do not add up to the "
            + "rounding of the total. Plutus reports both figures and the difference between them, "
            + "so the gap is always visible rather than silently absorbed.",
            "GET /api/v1/reports/vat — vatPence is the fraction, vatChargedPence is the lines' sum.",
            "HMRC Notice 727 §3.4.1 and Notice 727/3 §4.1 (Point of Sale scheme). ⚠ §4.2's "
            + "calculation steps have the force of law.",
            "https://www.gov.uk/guidance/vat-point-of-sale-retail-scheme-notice-7273"),

        new VatRule(
            "takings-by-band",
            "Takings are totalled by BAND, not by the rate a till happened to calculate",
            "A till derives each line's rate from its price pair, so one 20% band legitimately "
            + "arrives as 1993–2004 basis points across a period. Totalling by those raw numbers "
            + "would split a single rate into a dozen buckets, which is not \"the total value of "
            + "sales at each rate\" in any sense. Plutus snaps each line to the published band "
            + "within 0.25 percentage points and totals by band.",
            "VatAccounting.BandFor, tolerance 25bp.",
            "HMRC Notice 727 §3.4.1: \"Once your system has produced the total value of sales at "
            + "each rate, you calculate your output tax by applying the appropriate VAT fraction…\"",
            "https://www.gov.uk/guidance/vat-point-of-sale-retail-scheme-notice-7273"),

        new VatRule(
            "unclassified-never-merged",
            "Takings that match no band are reported separately, never absorbed into a real one",
            "If a line's price pair matches none of the published bands — legacy data with a price "
            + "and an ex-VAT price that disagree, for instance — it is reported as UNCLASSIFIED "
            + "with what the till actually charged. Plutus will not invent a rate for it or fold it "
            + "into the nearest band, because that would put a number on the return that nothing "
            + "supports.",
            "GET /api/v1/reports/vat — the 'unclassified' bucket; GET /api/v1/reports/vat-integrity lists the items.",
            "HMRC Notice 700 §19 (records) — the return must be supported by the records",
            "https://www.gov.uk/guidance/vat-guide-notice-700"),

        new VatRule(
            "tax-point",
            "The rate that applies is the one in force when the sale happened",
            "A sale is judged against the VAT rates in force at its time of supply — the moment it "
            + "was rung up — not the rates in force when it reached the server. This matters "
            + "because tills trade offline: one that reconnects a week later pushes a backlog that "
            + "must be accounted at the rates of the days it was actually trading.",
            "VatRateHistory.Assess, judged on the sale's OccurredAtUtc.",
            "HMRC Notice 700 §14 (time of supply / tax points)",
            "https://www.gov.uk/guidance/vat-guide-notice-700"),

        new VatRule(
            "offline-rate-change",
            "A till trading on a superseded rate is quarantined, not silently accepted or rewritten",
            "If a sale's price pair is explained only by a rate that was NOT in force when it "
            + "happened — a till that missed a rate change — it is held for review rather than "
            + "filed. Accepting it silently would file a wrong return; rewriting it silently would "
            + "change what the customer was recorded as paying, which is worse. A price pair that "
            + "matches no band at all is accepted and reported, because that is legacy data, not a "
            + "compliance failure in progress.",
            "SalesIngestService — 202 Accepted + a quarantine row naming both rates.",
            "HMRC Notice 700 §30 (VAT rate changes) and §14 (tax points)",
            "https://www.gov.uk/guidance/vat-guide-notice-700"),

        new VatRule(
            "zero-is-not-exempt",
            "Zero-rated and exempt both charge nothing — and are not the same thing",
            "Zero-rated is a TAXABLE supply at 0%: no VAT on the sale, and the VAT on related costs "
            + "IS recoverable. Exempt is NOT a taxable supply: no VAT on the sale, and the VAT on "
            + "costs attributable to it is NOT recoverable (partial exemption). Because both are 0% "
            + "to the customer, the difference cannot be recovered from the rate — so Plutus stores "
            + "the class on the band and refuses to guess it. Classifying zero-rated stock as exempt "
            + "costs real money in irrecoverable input tax.",
            "VatClass on every band; the editor makes you choose.",
            "HMRC Notice 700 §4 (rates) and Notice 706 (partial exemption)",
            "https://www.gov.uk/guidance/partial-exemption-vat-notice-706"),

        new VatRule(
            "books-zero-rated",
            "Books, comics, magazines and newspapers are zero-rated",
            "Printed matter of this kind is zero-rated in UK law, not exempt. It is worth stating "
            + "explicitly because a band labelled \"Exempt\" at 0% looks harmless and is not: it "
            + "blocks recovery of input tax on the stock and on the overheads attributable to it.",
            "Kapow's 14,740-item band, reclassified from Exempt to Zero on 2026-08-08.",
            "HMRC Notice 701/10 (zero-rating of books and other forms of printed matter)",
            "https://www.gov.uk/guidance/zero-rating-books-and-printed-matter-for-vat-notice-70110"),

        new VatRule(
            "vouchers",
            "Gift cards: single-purpose is taxed when SOLD, multi-purpose when SPENT",
            "A single-purpose voucher (everything it can buy carries the same VAT rate) is taxed "
            + "when the card is sold, so spending it must not declare VAT a second time. A "
            + "multi-purpose voucher (the catalogue carries mixed rates) is a payment method: "
            + "selling it declares nothing, and VAT falls due on the goods when the card is spent. "
            + "The choice is per-business and locks at the first card sale, because changing it "
            + "afterwards would retrospectively move tax between periods.",
            "GiftCardSettings; the treatment decides the activation line's band at the till.",
            "VAT treatment of vouchers from 1 January 2019",
            "https://www.gov.uk/government/publications/changes-to-the-vat-treatment-of-vouchers/vat-treatment-of-vouchers-from-1-january-2019"),

        new VatRule(
            "rate-changes-are-future-dated",
            "A rate change is scheduled, never back-dated",
            "Changing a band's rate adds a new dated entry; it never edits the existing one. The "
            + "old rate stays true for the sales rung up under it. The portal refuses a change "
            + "dated in the past, because back-dating would make settled, correctly-recorded sales "
            + "look non-compliant without changing a penny of what the customer actually paid. A "
            + "genuine historical error is corrected on the return, not in the rate table.",
            "POST /api/v1/vat/bands/{key}/rate-changes — 400 on a past date.",
            "HMRC Notice 700 §30 (change of VAT rate) and Notice 700/45 (correcting errors)",
            "https://www.gov.uk/guidance/how-to-correct-vat-errors-and-make-adjustments-or-claims-vat-notice-70045"),

        new VatRule(
            "correcting-errors",
            "Errors on a past return are corrected on the next one — up to a threshold",
            "A net error below the reporting threshold, and not careless or deliberate, is adjusted "
            + "on the next return. The threshold is the greater of £10,000 and 1% of Box 6 "
            + "(VAT-exclusive turnover), capped at £50,000. Above it — or if the error was careless "
            + "or deliberate, whatever its size — it must be disclosed separately on form VAT652. "
            + "Plutus works out which side of the arithmetic test an error falls on; whether it was "
            + "careless is a judgement for the business.",
            "GET /api/v1/reports/vat-corrections — the restatement, per period, with the route.",
            "HMRC Notice 700/45 §4 (how to correct VAT errors)",
            "https://www.gov.uk/guidance/how-to-correct-vat-errors-and-make-adjustments-or-claims-vat-notice-70045"),
    };

    /// <summary>Current UK rates, for the editor's reference — NOT the source of truth for a
    /// tenant's bands, which are whatever the portal has published for them.</summary>
    public const string RatesUrl = "https://www.gov.uk/vat-rates";
}
