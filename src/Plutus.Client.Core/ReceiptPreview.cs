using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.TillAgent.Core;

namespace Plutus.Client.Core;

/// <summary>
/// A receipt as TEXT, for showing on screen instead of printing it.
///
/// ⚠⚠ IT RENDERS THE SAME `PrintDocument` THE PRINTER GETS, and that is the entire point. The
/// alternative — laying the receipt out a second time for the screen — produces a preview that can
/// disagree with the paper, and a preview that can disagree with the paper is worse than no preview:
/// somebody checks it, sees the right total, and the customer's copy says something else.
///
/// So the pipeline is `ReceiptDocInput` → `ReceiptDocumentBuilder.Build` → **one** `PrintDocument`,
/// then either the printer renders it or this does.
///
/// ⚠ IT IS NOT A PIXEL PREVIEW. A thermal printer's bold, double-height and barcode have no honest
/// text equivalent, so this shows the *content and layout* — the columns line up, the rules span the
/// paper width, centring is centred. What it will not tell you is whether the shop name came out
/// double-height. Anything claiming to be more faithful than that would be lying about a device it
/// cannot see.
///
/// ⚠ RECEIPTS ARE IMMUNE TO THEMING (till-design C1), and this must stay so. Printing from a dark
/// scheme once put near-white ink on paper; a preview tinted by the portal's theme would re-introduce
/// exactly that confusion on screen.
/// </summary>
public static class ReceiptPreview
{
    /// <summary>⚠ What a barcode becomes on screen. The number underneath is the useful part — it is
    /// the sale id somebody reads down a phone — so it is shown rather than a row of bars.</summary>
    public const string BarcodeMarker = "[ barcode ]";

    /// <summary>
    /// The receipt as lines of text, in print order.
    ///
    /// ⚠ `Cut` and `Drawer` produce NOTHING. They are instructions to a machine, not content: a
    /// preview showing "[cut]" invites somebody to ask why the paper did not cut.
    /// </summary>
    public static IReadOnlyList<string> Lines(PrintDocument doc)
    {
        if (doc is null) return Array.Empty<string>();

        var columns = doc.Columns > 0 ? doc.Columns : 42;
        var lines = new List<string>();

        foreach (var op in doc.Ops ?? new List<PrintOp>())
        {
            switch (op.Kind)
            {
                case PrintOpKind.Text:
                    lines.Add(Align(op.Text ?? string.Empty, op.Align, columns));
                    break;

                case PrintOpKind.Rule:
                    lines.Add(new string('-', columns));
                    break;

                case PrintOpKind.Barcode:
                    // ⚠ Centred like the printer centres it, and the CONTENT is kept beneath.
                    lines.Add(Align(BarcodeMarker, PrintAlign.Centre, columns));
                    if (!string.IsNullOrWhiteSpace(op.Text))
                        lines.Add(Align(op.Text!, PrintAlign.Centre, columns));
                    break;

                // ⚠ Machine instructions, deliberately invisible — see the method header.
                case PrintOpKind.Cut:
                case PrintOpKind.Drawer:
                default:
                    break;
            }
        }

        return lines;
    }

    /// <summary>The whole receipt as one block, ready for a monospaced control.</summary>
    public static string Text(PrintDocument doc) => string.Join(Environment.NewLine, Lines(doc));

    /// <summary>
    /// ⚠ TRAILING WHITESPACE IS NOT ADDED. Left-aligned text is returned as-is rather than padded to
    /// the column width — padding every line makes a copy-and-paste of the receipt full of invisible
    /// spaces, and somebody pastes it into a support ticket.
    ///
    /// ⚠ A line LONGER than the paper is left alone, not truncated. The printer wraps it; silently
    /// cutting it here would hide the one thing a preview is for — spotting that a shop's address line
    /// does not fit.
    /// </summary>
    private static string Align(string text, PrintAlign align, int columns)
    {
        if (text.Length >= columns || align == PrintAlign.Left) return text;

        var slack = columns - text.Length;

        return align switch
        {
            PrintAlign.Right => new string(' ', slack) + text,
            PrintAlign.Centre => new string(' ', slack / 2) + text,
            _ => text,
        };
    }
}
