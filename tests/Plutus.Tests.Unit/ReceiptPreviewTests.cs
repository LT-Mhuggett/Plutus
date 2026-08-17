using System.Linq;
using Plutus.Client.Core;
using Plutus.TillAgent.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// A receipt shown on screen instead of printed (2026-08-17).
///
/// ⚠⚠ THE PROPERTY THAT MATTERS: it renders the **same** `PrintDocument` the printer gets. Laying a
/// receipt out a second time for the screen produces a preview that can disagree with the paper — and
/// a preview that can disagree with the paper is worse than none, because somebody checks it, sees the
/// right total, and the customer's copy says something else.
/// </summary>
public class ReceiptPreviewTests
{
    private static PrintDocument Doc(int columns, params PrintOp[] ops) =>
        new() { Columns = columns, Ops = ops.ToList() };

    // ── layout ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_rule_spans_the_paper_width()
    {
        Assert.Equal(new string('-', 32), ReceiptPreview.Lines(Doc(32, PrintOp.Rule()))[0]);
        Assert.Equal(new string('-', 42), ReceiptPreview.Lines(Doc(42, PrintOp.Rule()))[0]);
    }

    [Fact]
    public void Centred_text_is_centred_for_the_paper_in_use()
    {
        var line = ReceiptPreview.Lines(Doc(20, PrintOp.Line("SHOP", PrintAlign.Centre)))[0];

        Assert.Equal("        SHOP", line);   // (20-4)/2 = 8 spaces
    }

    [Fact]
    public void Right_aligned_text_reaches_the_right_edge()
    {
        var line = ReceiptPreview.Lines(Doc(10, PrintOp.Line("£9.99", PrintAlign.Right)))[0];

        Assert.Equal(10, line.Length);
        Assert.EndsWith("£9.99", line);
    }

    /// <summary>
    /// ⚠ LEFT-ALIGNED TEXT IS NOT PADDED. Padding every line to the column width fills a
    /// copy-and-paste of the receipt with invisible trailing spaces, and somebody pastes it into a
    /// support ticket.
    /// </summary>
    [Fact]
    public void Left_aligned_text_gains_no_trailing_whitespace()
    {
        var line = ReceiptPreview.Lines(Doc(42, PrintOp.Line("2 x Batman #1")))[0];

        Assert.Equal("2 x Batman #1", line);
    }

    /// <summary>
    /// ⚠⚠ AN OVER-LONG LINE IS LEFT ALONE, NOT TRUNCATED. The printer wraps it; cutting it here would
    /// hide the one thing a preview is for — spotting that a shop's address line does not fit the paper.
    /// </summary>
    [Fact]
    public void A_line_longer_than_the_paper_is_not_truncated()
    {
        var long_ = new string('x', 60);

        Assert.Equal(long_, ReceiptPreview.Lines(Doc(42, PrintOp.Line(long_)))[0]);
    }

    /// <summary>⚠ A blank line is a blank line — receipts use them for spacing, and dropping them
    /// would compress the layout into something the paper will not match.</summary>
    [Fact]
    public void A_blank_line_survives()
    {
        var lines = ReceiptPreview.Lines(Doc(42, PrintOp.Line("A"), PrintOp.Line(""), PrintOp.Line("B")));

        Assert.Equal(3, lines.Count);
        Assert.Equal("", lines[1]);
    }

    // ── machine instructions are not content ──────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ CUT AND DRAWER RENDER NOTHING. They are instructions to a machine, not part of the receipt:
    /// a preview showing "[cut]" invites somebody to ask why the paper did not cut.
    /// </summary>
    [Fact]
    public void A_cut_and_a_drawer_kick_are_invisible()
    {
        var lines = ReceiptPreview.Lines(Doc(42,
            PrintOp.Line("TOTAL"), PrintOp.Drawer(), PrintOp.Cut()));

        Assert.Single(lines);
        Assert.Equal("TOTAL", lines[0]);
    }

    /// <summary>⚠ A barcode becomes a marker AND its content — the number underneath is the useful
    /// half, because it is the sale id somebody reads down a phone.</summary>
    [Fact]
    public void A_barcode_shows_a_marker_and_the_number_beneath_it()
    {
        var lines = ReceiptPreview.Lines(Doc(42, PrintOp.Barcode("A1B2C3D4")));

        Assert.Equal(2, lines.Count);
        Assert.Contains(ReceiptPreview.BarcodeMarker, lines[0]);
        Assert.Contains("A1B2C3D4", lines[1]);
    }

    // ── the whole thing ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Rendered from a document built by `ReceiptDocumentBuilder` itself, not a hand-made op list —
    /// so this test fails if the real builder's output ever stops being renderable.
    /// </summary>
    [Fact]
    public void A_real_receipt_renders_with_its_figures_intact()
    {
        var doc = ReceiptDocumentBuilder.Build(new ReceiptDocInput
        {
            StoreName = "Kapow Comics",
            Columns = 42,
            Lines = new[] { new ReceiptDocLine("Batman #1", 2, 660, 660) },
            Tenders = new[] { new ReceiptDocTender("Cash", 660) },
            GrossPence = 660,
            ExPence = 550,
            SaleId = "A1B2C3D4",
        });

        var text = ReceiptPreview.Text(doc);

        Assert.Contains("Kapow Comics", text);
        Assert.Contains("Batman #1", text);
        Assert.Contains("6.60", text);
        Assert.Contains("Cash", text);
    }

    [Fact]
    public void An_empty_or_null_document_is_no_lines_rather_than_a_crash()
    {
        Assert.Empty(ReceiptPreview.Lines(null!));
        Assert.Empty(ReceiptPreview.Lines(new PrintDocument()));
        Assert.Equal("", ReceiptPreview.Text(new PrintDocument()));
    }

    /// <summary>⚠ A document with no stated column width still renders — 42 is the 80mm default, and a
    /// preview that produced a zero-width rule would look like a fault.</summary>
    [Fact]
    public void A_document_with_no_column_width_falls_back_to_eighty_millimetre()
    {
        var doc = new PrintDocument { Columns = 0, Ops = { PrintOp.Rule() } };

        Assert.Equal(42, ReceiptPreview.Lines(doc)[0].Length);
    }
}
