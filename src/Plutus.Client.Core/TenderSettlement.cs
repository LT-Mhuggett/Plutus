using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Plutus.Client.Core;

/// <summary>
/// One pay method, as the arithmetic needs it. <see cref="IsChangeable"/> is true for cash.
/// </summary>
public sealed record TenderMethodRef(int Id, bool IsChangeable);

/// <summary>
/// The amount boxes, read.
///
/// ⚠ <see cref="Valid"/> false means ONE box was not a number — see <see cref="TenderSettlement.ParseAmounts"/>.
/// </summary>
public sealed record ParsedAmounts(bool Valid, IReadOnlyDictionary<int, long> PerMethod, long Paid)
{
    public static readonly ParsedAmounts Invalid =
        new(false, new Dictionary<int, long>(), 0);
}

/// <summary>What a set of amounts does to a basket.</summary>
public sealed record Settlement(
    /// <summary>Absolute value of what the basket owes — a refund is a positive Owed with Refunding true.</summary>
    long Owed,
    bool Refunding,
    long Paid,
    /// <summary>Still to take. Never negative — an excess is <see cref="Overpay"/>, not a negative remainder.</summary>
    long Remaining,
    /// <summary>Taken above what is owed. Always 0 on a refund.</summary>
    long Overpay,
    /// <summary>⚠ A refund handed back MORE than it owes. Not change — an error.</summary>
    bool OverRefund,
    /// <summary>Sum of the tenders that can physically give change back (cash).</summary>
    long ChangeablePaid,
    /// <summary>Whether the overpay can actually be handed back from the drawer.</summary>
    bool ChangeOk,
    /// <summary>Change per pay method, summing EXACTLY to <see cref="Overpay"/>.</summary>
    IReadOnlyDictionary<int, long> ChangeByPayId);

/// <summary>
/// ONE-SCREEN tendering arithmetic — what is paid, what remains, what change is due, and whether the
/// sale may complete. §5c item 2, 2026-08-19.
///
/// ⚠⚠ **C2 TWIN of the web till's `till/tendering.ts`, and this is the .NET half arriving second.**
/// That file has carried the one-screen rules and 40 vitest vectors; MAUI had only
/// <see cref="TenderLoop"/>, which asks for one tender at a time and cannot answer "what does this
/// whole screen add up to". Matt, 2026-08-19: *"I need the functionality and look and feel to be the
/// same across both tills. So if a user swaps between the two, it doesnt matter and they would
/// understand how to use it."* A MAUI checkout that looks like the web till's needs the web till's
/// arithmetic, and re-deriving it in a viewmodel is exactly how two tills come to disagree by a penny.
///
/// ⚠⚠ **PORTED EXPRESSION BY EXPRESSION, NOT REINTERPRETED.** Every rule below is the TypeScript one,
/// and `TenderSettlementTests` runs the SAME VECTORS as `tendering.test.ts` — including the ones that
/// look like edge cases and are not: change apportionment summing to the penny, a refund never giving
/// change, and one bad box invalidating the whole set.
///
/// ⚠ <see cref="TenderLoop"/> STAYS. It owns a different question — the sequential prompt flow, its
/// caps, refusals and surcharge — and this class owns the settlement. They are not two copies of one
/// rule: the loop asks "may this ONE tender be taken", this asks "does the WHOLE screen balance".
/// </summary>
public static class TenderSettlement
{
    /// <summary>
    /// Read one amount box: pounds text in, pence out, null when it is not money.
    ///
    /// ⚠⚠ C2 TWIN of `money.ts parsePence`, and DELIBERATELY AS STRICT. It accepts an optional `£`,
    /// digits, and at most two decimals — nothing else. No negatives (a negative tender is not a
    /// refund, it is a typo), no thousands separators, no bare `1.`.
    ///
    /// ⚠ Parsed as `decimal`, where the TypeScript uses `parseFloat` and `Math.round`. With at most
    /// two decimal places the two cannot disagree: binary error is ~1e-13, nowhere near the half that
    /// would change a rounding decision, and doubles represent exact halves exactly. Decimal is used
    /// here because it is what money is in .NET, not because the answers differ.
    /// </summary>
    public static long? ParsePence(string? input)
    {
        var cleaned = (input ?? string.Empty).Trim();
        if (cleaned.StartsWith("£", StringComparison.Ordinal)) cleaned = cleaned.Substring(1);

        if (!Regex.IsMatch(cleaned, @"^\d+(\.\d{1,2})?$")) return null;

        var pounds = decimal.Parse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return (long)Math.Round(pounds * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Read the per-method amount boxes.
    ///
    /// ⚠ ONE BAD BOX INVALIDATES THE WHOLE SET, deliberately. A basket that is 90% parseable is not
    /// 90% payable — completing on a partial read would take a different sum from the one on screen.
    /// ⚠ Blank is SKIPPED, not zero: an untouched row is not a £0.00 tender.
    /// ⚠ Zero is DROPPED rather than refused, matching the web till: the gate that stops the sale is
    /// `Remaining == 0`, so a £0 row simply contributes nothing.
    /// </summary>
    public static ParsedAmounts ParseAmounts(IReadOnlyDictionary<int, string> amounts)
    {
        var perMethod = new Dictionary<int, long>();

        foreach (var (id, raw) in amounts ?? new Dictionary<int, string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var pence = ParsePence(raw);
            if (pence is null) return ParsedAmounts.Invalid;
            if (pence > 0) perMethod[id] = pence.Value;
        }

        return new ParsedAmounts(true, perMethod, perMethod.Values.Sum());
    }

    /// <summary>
    /// Assess a set of amounts against a basket.
    ///
    /// ⚠ <paramref name="totalPence"/> NEGATIVE MEANS REFUND, matching the web till and the wire.
    /// ⚠ NO CHANGE ON A REFUND. You hand back exactly what is owed, so an excess is a mistake rather
    /// than change — the same rule <see cref="TenderLoop"/> applies.
    /// </summary>
    public static Settlement Assess(
        long totalPence,
        ParsedAmounts parsed,
        IReadOnlyList<TenderMethodRef> methods)
    {
        var refunding = totalPence < 0;
        var owed = Math.Abs(totalPence);

        var paid = parsed.Valid ? parsed.Paid : 0;
        var remaining = Math.Max(0, owed - paid);
        var overpay = refunding ? 0 : Math.Max(0, paid - owed);
        var overRefund = refunding && paid > owed;

        var changeablePaid = parsed.Valid
            ? parsed.PerMethod
                .Where(kv => methods?.FirstOrDefault(m => m.Id == kv.Key)?.IsChangeable == true)
                .Sum(kv => kv.Value)
            : 0;

        var changeOk = overpay == 0 || overpay <= changeablePaid;

        return new Settlement(
            owed, refunding, paid, remaining, overpay, overRefund, changeablePaid, changeOk,
            ApportionChange(parsed, methods, overpay, changeablePaid));
    }

    /// <summary>
    /// Split the change across the methods that can give it.
    ///
    /// ⚠⚠ THE LAST CHANGEABLE METHOD ABSORBS THE ROUNDING REMAINDER, and that is load-bearing:
    /// Σchange must equal the overpay TO THE PENNY, because the v1 pipeline enforces
    /// `net tender == gross` and rejects the sale otherwise. Rounding each share independently loses
    /// or invents a penny on three-way splits, and the sale is refused at ingest with nothing on
    /// screen explaining why.
    ///
    /// ⚠⚠ `MidpointRounding.AwayFromZero` IS NOT OPTIONAL. .NET's default is banker's rounding and
    /// JavaScript's `Math.round` is half-up; every share here is positive, so away-from-zero is the
    /// one that agrees with the web till. This is the documented JS-vs-.NET midpoint trap that
    /// `VatLineMathTests` also exists to pin.
    ///
    /// ⚠ ORDERED BY PAY METHOD ID, because "the last one" has to mean the same row on both tills —
    /// a dictionary's enumeration order is not a promise, and the remainder lands on whoever is last.
    /// </summary>
    public static IReadOnlyDictionary<int, long> ApportionChange(
        ParsedAmounts parsed,
        IReadOnlyList<TenderMethodRef> methods,
        long overpay,
        long changeablePaid)
    {
        var changeByPayId = new Dictionary<int, long>();
        if (!parsed.Valid || overpay <= 0 || changeablePaid <= 0) return changeByPayId;

        var changeable = parsed.PerMethod
            .Where(kv => methods?.FirstOrDefault(m => m.Id == kv.Key)?.IsChangeable == true)
            .OrderBy(kv => kv.Key)
            .ToList();

        var allocated = 0L;
        for (var i = 0; i < changeable.Count; i++)
        {
            var change = i == changeable.Count - 1
                ? overpay - allocated
                : (long)Math.Round((decimal)overpay * changeable[i].Value / changeablePaid,
                                   MidpointRounding.AwayFromZero);

            allocated += change;
            changeByPayId[changeable[i].Key] = change;
        }

        return changeByPayId;
    }

    /// <summary>
    /// What the "rest" button should put in one row.
    ///
    /// ⚠ IT IGNORES WHAT THIS ROW ALREADY HOLDS. Using the bare remainder made "rest" toggle between
    /// 0.00 and the full amount whenever the row was already filled (reported 2026-08-07 on the web
    /// till), and left an overpaid row untouched. <paramref name="own"/> is subtracted out so the row
    /// is recomputed from the others.
    ///
    /// ⚠ <paramref name="capTo"/> is what the method can actually give — a customer's credit balance,
    /// a gift card's remaining value. Matt, 2026-08-19: *"the 'Rest' button needs to only ever put the
    /// max credit they have at the time in. There is No point putting the full number in."*
    /// </summary>
    public static long RestFor(long owed, long paid, long own, long? capTo = null)
    {
        var needed = Math.Max(0, owed - (paid - own));
        return capTo is null ? needed : Math.Min(needed, capTo.Value);
    }

    /// <summary>
    /// Why the sale cannot complete yet — or null when it can.
    ///
    /// ⚠ IT RETURNS A SENTENCE. A greyed-out Complete button with no reason is the same failure as
    /// *"Something went wrong"*: the screen has decided something and not said what. Matt reported
    /// exactly that against the MAUI till on 2026-08-11.
    ///
    /// ⚠ THE ORDER OF THE TESTS IS THE MESSAGE THE OPERATOR GETS, and it runs from the most common
    /// cause to the rarest, so the sentence is usually the useful one: still to pay, then handing back
    /// too much, then change that cannot be given.
    /// </summary>
    public static string? Refusal(Settlement s, ParsedAmounts parsed, Func<long, string> gbp)
    {
        if (!parsed.Valid) return "One of those amounts isn't a number.";
        if (s.Remaining > 0) return $"{gbp(s.Remaining)} still to pay.";
        if (s.OverRefund) return $"That's more than the refund owes — hand back exactly {gbp(s.Owed)}.";
        if (!s.ChangeOk) return $"{gbp(s.Overpay)} over, and only cash can give change back.";
        return null;
    }
}
