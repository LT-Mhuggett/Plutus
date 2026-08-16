using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// The receipt layout a store prints with — set in the PORTAL, obeyed by every till.
///
/// ⚠ Every field is optional. An untouched template is not an empty receipt: the store's real
/// details fill the gaps — see <see cref="ReceiptTemplateRules.Merge"/>.
/// </summary>
public sealed record ReceiptTemplate(
    string? StoreName = null,
    IReadOnlyList<string>? AddressLines = null,
    string? Phone = null,
    string? VatNumber = null,
    IReadOnlyList<string>? HeaderLines = null,
    IReadOnlyList<string>? FooterLines = null,
    bool ShowVatNumber = true,
    bool ShowOperator = true,
    bool ShowBarcode = true);

/// <summary>The store's own details, as the platform holds them — the fallback source.</summary>
public sealed record ReceiptStoreDetails(
    string? Name = null,
    string? AdLine1 = null,
    string? AdLine2 = null,
    string? City = null,
    string? PostCode = null,
    string? ContactNumber = null,
    string? VatNumber = null);

/// <summary>
/// How a saved template and a store's real details combine into the receipt that actually prints.
///
/// ⚠⚠ IT IS SHARED BECAUSE THE ALTERNATIVE IS TWO RECEIPTS. The web till merges these in `api.ts`
/// (`mergeTemplate`), and MAUI was about to grow its own — so the same store would print its address
/// on one till and not on the other, from the same portal settings. Both renderers on the web till
/// already read one cache for exactly this reason; this is that discipline across the two tills.
///
/// ⚠ THE RULE THAT MATTERS: **the store's details fill any field the template leaves BLANK.** A shop
/// that has never touched its template still prints its own name, address, phone and VAT number.
/// Before the web till did this it printed none of them while the portal's preview pretended
/// otherwise — a receipt that is legally required to identify the trader, silently missing it.
///
/// ⚠ TOGGLES COME FROM THE SAVED TEMPLATE ONLY, never from the store record. "Show the VAT number"
/// is a layout decision somebody made in the portal; a store simply HAVING a VAT number is not the
/// same as choosing to print it.
/// </summary>
public static class ReceiptTemplateRules
{
    /// <summary>
    /// The EFFECTIVE template — what the paper is actually built from.
    ///
    /// ⚠ Blank means blank: whitespace counts as unset, so a field somebody cleared in the portal
    /// falls back rather than printing a space and looking like a rendering fault.
    ///
    /// ⚠ An address the template sets WINS ENTIRELY — it is not merged line by line with the store's.
    /// Interleaving two addresses produces one that belongs to nobody.
    /// </summary>
    public static ReceiptTemplate Merge(ReceiptTemplate? saved, ReceiptStoreDetails? store)
    {
        var t = saved ?? new ReceiptTemplate();
        if (store is null) return t;

        var storeAddress = new[] { store.AdLine1, store.AdLine2, store.City, store.PostCode }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.Trim())
            .ToList();

        return t with
        {
            StoreName = Prefer(t.StoreName, store.Name),
            Phone = Prefer(t.Phone, store.ContactNumber),
            VatNumber = Prefer(t.VatNumber, store.VatNumber),

            // ⚠ The template's address wins WHOLE or not at all.
            AddressLines = HasLines(t.AddressLines) ? Clean(t.AddressLines!) : storeAddress,

            // ⚠ Header and footer have NO store fallback — there is nothing on a store record that
            // could stand in for "Thank you for shopping with us" or a returns policy. Absent means
            // the caller's own default, not an invented sentence.
        };
    }

    /// <summary>Should the VAT number print? ⚠ BOTH the toggle and a number are required — a
    /// "VAT No:" label with nothing after it is worse than omitting the line.</summary>
    public static bool PrintsVatNumber(ReceiptTemplate? t) =>
        t is { ShowVatNumber: true } && !string.IsNullOrWhiteSpace(t.VatNumber);

    /// <summary>The header lines, or the caller's default when the template sets none.</summary>
    public static IReadOnlyList<string> HeaderOr(ReceiptTemplate? t, IReadOnlyList<string> fallback) =>
        HasLines(t?.HeaderLines) ? Clean(t!.HeaderLines!) : fallback;

    /// <summary>The footer lines, or nothing. ⚠ Unlike the header there is no sensible default — an
    /// invented returns policy is a promise the shop did not make.</summary>
    public static IReadOnlyList<string> FooterOr(ReceiptTemplate? t) =>
        HasLines(t?.FooterLines) ? Clean(t!.FooterLines!) : Array.Empty<string>();

    private static string? Prefer(string? fromTemplate, string? fromStore) =>
        !string.IsNullOrWhiteSpace(fromTemplate) ? fromTemplate!.Trim()
        : !string.IsNullOrWhiteSpace(fromStore) ? fromStore!.Trim()
        : null;

    private static bool HasLines(IReadOnlyList<string>? lines) =>
        lines is { Count: > 0 } && lines.Any(l => !string.IsNullOrWhiteSpace(l));

    /// <summary>⚠ Blank lines are DROPPED. An empty line in the middle of a receipt reads as a
    /// printer fault, and a portal field somebody cleared should vanish rather than leave a gap.</summary>
    private static IReadOnlyList<string> Clean(IReadOnlyList<string> lines) =>
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
}
