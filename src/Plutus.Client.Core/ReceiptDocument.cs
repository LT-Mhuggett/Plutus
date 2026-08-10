using System.Globalization;
using Plutus.TillAgent.Core;

namespace Plutus.Client.Core;

/// <summary>One sold or returned line, as the paper describes it.</summary>
public sealed record ReceiptDocLine(
    string Name,
    int Quantity,
    long UnitPence,
    long TotalPence,
    bool IsReturn = false,
    string? DiscountName = null,
    long DiscountPence = 0,
    string? Note = null);

/// <summary>One payment line.</summary>
public sealed record ReceiptDocTender(string Name, long AmountPence, long ChangePence = 0);

/// <summary>
/// Everything the paper says, already resolved. ⚠ Deliberately dumb: no lookups, no clock, no
/// culture guessing. Whatever built this has already decided what the customer sees, so the same
/// input always produces the same receipt — which is what makes it testable and what makes two
/// tills agree.
/// </summary>
public sealed record ReceiptDocInput
{
    public IReadOnlyList<string> HeaderLines { get; init; } = new[] { "Thank you for shopping with us" };
    public string? StoreName { get; init; }
    public string? Phone { get; init; }
    public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();
    public string? VatNumber { get; init; }
    public DateTime WhenLocal { get; init; }
    public string? OperatorName { get; init; }

    public IReadOnlyList<ReceiptDocLine> Lines { get; init; } = Array.Empty<ReceiptDocLine>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public long ExPence { get; init; }
    public long GrossPence { get; init; }
    public IReadOnlyList<ReceiptDocTender> Tenders { get; init; } = Array.Empty<ReceiptDocTender>();

    public IReadOnlyList<string> FooterLines { get; init; } = Array.Empty<string>();

    /// <summary>⚠ The platform sale id. This is what a reprint or a receipt-led refund looks the
    /// sale up by; an empty one makes the paper unsearchable for ever.</summary>
    public string SaleId { get; init; } = "";
    public bool ShowBarcode { get; init; } = true;

    /// <summary>Taken while the line was down. Says so on the paper, because the customer's copy is
    /// the only evidence a sale happened until the outbox drains.</summary>
    public bool Queued { get; init; }

    /// <summary>
    /// This paper is a COPY of a receipt already issued.
    ///
    /// ⚠ IT MUST SAY SO, and this is a money rule rather than a courtesy. A reprint that is
    /// indistinguishable from the original is a second receipt for one sale — and this till's own
    /// refund flow accepts a sale found by the barcode on a receipt. Two identical papers for one
    /// purchase is the shape of a double refund, which has already happened here once from a
    /// different cause. The customer keeps a receipt; the shop must be able to see which one it is.
    /// </summary>
    public bool IsReprint { get; init; }

    public int Columns { get; init; } = 42;
    public bool OpenDrawer { get; init; }
}

/// <summary>
/// Renders a completed sale into the agent's print-op document.
///
/// ⚠ THIS IS THE SECOND IMPLEMENTATION OF THIS LAYOUT and that is a deliberate, recorded cost —
/// see `till-design.md` C2. The first is the web till's `receiptDoc.ts`, which cannot be shared
/// because it is TypeScript running in a browser. The two are kept line-for-line identical on
/// purpose, and `ReceiptDocumentTests` pins the shape (order of blocks, two-column padding,
/// negative-total REFUND banner) so a change to one that is not made to the other fails a test
/// rather than printing two different receipts in two shops.
///
/// ⚠ The layout lives in the TILL, never in the agent. The agent receives a rendered document, so
/// a receipt template change ships with the till and cannot leave the agent printing last month's
/// header — the reasoning is written out in `PrintOps.cs`.
/// </summary>
public static class ReceiptDocumentBuilder
{
    /// <summary>
    /// "Item name .......... £4.99". ⚠ Truncates the NAME, never the money — a receipt that has
    /// dropped a digit off a total is worse than one with a shortened product name, and the printer
    /// cannot do two columns itself. Mirrors `EscPos.TwoColumn` and the web till's `twoColumn`.
    /// </summary>
    public static string TwoColumn(string? left, string? right, int columns)
        => EscPos.TwoColumn(left ?? "", right ?? "", columns);

    /// <summary>
    /// ⚠ INVARIANT £, not the machine's culture. A till in a shop whose Windows is set to
    /// de-DE would otherwise print "4,99 €" on a UK VAT receipt. The web till pins en-GB for the
    /// same reason.
    /// </summary>
    public static string Gbp(long pence)
    {
        var pounds = pence / 100m;
        return pence < 0
            ? "-£" + (-pounds).ToString("0.00", CultureInfo.InvariantCulture)
            : "£" + pounds.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static PrintDocument Build(ReceiptDocInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var columns = input.Columns > 0 ? input.Columns : 42;
        var doc = new PrintDocument { Columns = columns, OpenDrawer = input.OpenDrawer };
        var ops = doc.Ops;

        // ── header, in the order the on-screen receipt uses ──
        foreach (var line in input.HeaderLines.Count > 0 ? input.HeaderLines : new[] { "Thank you for shopping with us" })
            ops.Add(PrintOp.Line(line, PrintAlign.Centre));

        if (!string.IsNullOrWhiteSpace(input.StoreName))
            ops.Add(PrintOp.Line(input.StoreName, PrintAlign.Centre, bold: true, large: true));
        if (!string.IsNullOrWhiteSpace(input.Phone))
            ops.Add(PrintOp.Line(input.Phone, PrintAlign.Centre));
        foreach (var line in input.AddressLines)
            if (!string.IsNullOrWhiteSpace(line)) ops.Add(PrintOp.Line(line, PrintAlign.Centre));
        if (!string.IsNullOrWhiteSpace(input.VatNumber))
            ops.Add(PrintOp.Line($"VAT No: {input.VatNumber}", PrintAlign.Centre));

        ops.Add(PrintOp.Line(input.WhenLocal.ToString("dd/MM/yyyy, HH:mm:ss", CultureInfo.InvariantCulture), PrintAlign.Centre));
        if (!string.IsNullOrWhiteSpace(input.OperatorName))
            ops.Add(PrintOp.Line($"Served by {input.OperatorName}", PrintAlign.Centre));

        // ⚠ A refund must be obvious at a glance on both copies. The totals are negative, but
        // reading a receipt should not depend on spotting a minus sign.
        if (input.GrossPence < 0)
            ops.Add(PrintOp.Line("** REFUND **", PrintAlign.Centre, bold: true));

        // ⚠ ABOVE THE RULE, WITH THE REFUND BANNER, not buried in the footer. Both answer the same
        // question — "what am I holding?" — and a reprint marker nobody reads is a reprint marker
        // that does not exist. See ReceiptDocInput.IsReprint for why it is a money rule.
        if (input.IsReprint)
            ops.Add(PrintOp.Line("** REPRINT — not a new sale **", PrintAlign.Centre, bold: true));

        ops.Add(PrintOp.Rule());

        // ── lines ──
        foreach (var line in input.Lines)
        {
            ops.Add(PrintOp.Line((line.IsReturn ? "RETURN — " : "") + line.Name));
            ops.Add(PrintOp.Line(TwoColumn(
                $"  {line.Quantity} x {Gbp(line.UnitPence)}", Gbp(line.TotalPence), columns)));

            if (!string.IsNullOrWhiteSpace(line.DiscountName))
                ops.Add(PrintOp.Line(TwoColumn(
                    $"  {line.DiscountName}", $"-{Gbp(line.DiscountPence)}", columns)));

            if (!string.IsNullOrWhiteSpace(line.Note))
                ops.Add(PrintOp.Line($"  {line.Note}"));
        }

        // ── totals ──
        ops.Add(PrintOp.Rule());
        ops.Add(PrintOp.Line(TwoColumn("Subtotal (ex tax)", Gbp(input.ExPence), columns)));
        ops.Add(PrintOp.Line(TwoColumn("Tax", Gbp(input.GrossPence - input.ExPence), columns)));
        ops.Add(PrintOp.Line(TwoColumn("TOTAL", Gbp(input.GrossPence), columns), bold: true));
        ops.Add(PrintOp.Rule());

        foreach (var tender in input.Tenders)
            ops.Add(PrintOp.Line(TwoColumn(tender.Name, Gbp(tender.AmountPence), columns)));

        var change = input.Tenders.Sum(t => t.ChangePence);
        if (change > 0)
            ops.Add(PrintOp.Line(TwoColumn("Change", Gbp(change), columns)));

        if (input.Notes.Count > 0)
        {
            ops.Add(PrintOp.Rule());
            foreach (var note in input.Notes)
                ops.Add(PrintOp.Line(note));
        }

        ops.Add(PrintOp.Rule());
        foreach (var line in input.FooterLines)
            if (!string.IsNullOrWhiteSpace(line)) ops.Add(PrintOp.Line(line, PrintAlign.Centre));

        if (input.Queued)
            ops.Add(PrintOp.Line("* taken offline — will sync automatically *", PrintAlign.Centre));

        // ⚠ The barcode is the sale id, and it is the only way a customer's receipt ever finds its
        // sale again. Code 39 has no lower case, hence the upper-casing.
        if (input.ShowBarcode && !string.IsNullOrWhiteSpace(input.SaleId))
            ops.Add(PrintOp.Barcode(input.SaleId.ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(input.SaleId))
            ops.Add(PrintOp.Line(input.SaleId, PrintAlign.Centre));

        ops.Add(PrintOp.Cut());
        return doc;
    }
}
