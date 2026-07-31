using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// FE3: turns a <see cref="PrintDocument"/> into ESC/POS bytes.
    ///
    /// The command vocabulary is the one the native tills already use
    /// (Plutus.Frontend.*/Platforms/Windows/Services/POS/EscPosComands.cs) — same escapes, same
    /// "'0' not '\0' for OFF" quirk, which those files record as a real Windows 10 problem where a
    /// Unicode null was mishandled. Kept here as BYTES rather than strings so the encoding is
    /// explicit and testable.
    ///
    /// Encoding is CP437 (the near-universal receipt-printer default): the printer interprets bytes
    /// through its own code page, so UTF-8 would print mojibake for £ and any accented character.
    /// £ is 0x9C in CP437 — getting that wrong makes every price line unreadable, so it is mapped
    /// explicitly rather than trusted to a platform encoding table.
    /// </summary>
    public static class EscPos
    {
        private const byte ESC = 0x1B;
        private const byte GS = 0x1D;
        private const byte LF = 0x0A;

        public static byte[] Render(PrintDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            using var ms = new MemoryStream();

            Write(ms, ESC, (byte)'@');                      // initialise
            foreach (var op in doc.Ops ?? new List<PrintOp>()) RenderOp(ms, op, doc.Columns);

            if (doc.OpenDrawer) Write(ms, DrawerKick());
            return ms.ToArray();
        }

        private static void RenderOp(Stream s, PrintOp op, int columns)
        {
            switch (op.Kind)
            {
                case PrintOpKind.Text:
                    SetAlign(s, op.Align);
                    if (op.Bold) Write(s, ESC, (byte)'E', 1);
                    if (op.Underline) Write(s, ESC, (byte)'-', 1);
                    if (op.Large) Write(s, GS, (byte)'!', 0x11);      // double W + H
                    Write(s, Encode(op.Text ?? string.Empty));
                    Write(s, LF);
                    // Reset every attribute we set — a printer keeps modes until told otherwise, so a
                    // missed reset bleeds bold/double-height down the rest of the receipt.
                    if (op.Large) Write(s, GS, (byte)'!', (byte)'0');
                    if (op.Underline) Write(s, ESC, (byte)'-', (byte)'0');
                    if (op.Bold) Write(s, ESC, (byte)'E', (byte)'0');
                    if (op.Align != PrintAlign.Left) SetAlign(s, PrintAlign.Left);
                    break;

                case PrintOpKind.Rule:
                    Write(s, Encode(new string('-', Math.Max(1, columns))));
                    Write(s, LF);
                    break;

                case PrintOpKind.Barcode:
                    RenderBarcode(s, op);
                    break;

                case PrintOpKind.Cut:
                    Write(s, LF, LF, LF, LF);                          // feed clear of the cutter
                    Write(s, GS, (byte)'V', 66, 0);                    // partial cut, feed 0
                    break;

                case PrintOpKind.Drawer:
                    Write(s, DrawerKick());
                    break;
            }
        }

        /// <summary>
        /// Code 39, matching the till's on-screen <c>Barcode39</c> and the member/gift-card payloads.
        /// Code 39 is the format the shop's existing scanners already read.
        /// </summary>
        private static void RenderBarcode(Stream s, PrintOp op)
        {
            var content = (op.Text ?? string.Empty).ToUpperInvariant();
            if (content.Length == 0) return;

            SetAlign(s, PrintAlign.Centre);
            Write(s, GS, (byte)'h', (byte)Math.Clamp(op.Height, 1, 255));   // height
            Write(s, GS, (byte)'w', 2);                                      // module width
            Write(s, GS, (byte)'H', 0);                                      // no HRI text (we print it ourselves)
            // GS k m n d1..dn — m=69 is CODE39 in the "length-prefixed" family, which avoids the
            // NUL-terminated variant's trouble with odd content.
            var data = Encode(content);
            Write(s, GS, (byte)'k', 69, (byte)Math.Min(data.Length, 255));
            Write(s, data);
            Write(s, LF);
            SetAlign(s, PrintAlign.Left);
        }

        /// <summary>ESC p 0 t1 t2 — the standard drawer-kick pulse on pin 2.</summary>
        public static byte[] DrawerKick() => new byte[] { ESC, (byte)'p', 0, 25, 250 };

        private static void SetAlign(Stream s, PrintAlign align) =>
            Write(s, ESC, (byte)'a', align switch
            {
                PrintAlign.Centre => (byte)1,
                PrintAlign.Right => (byte)2,
                _ => (byte)'0',   // '0' not 0 — the native tills' documented Windows 10 NUL quirk
            });

        /// <summary>
        /// CP437 bytes. .NET Core dropped the built-in code pages, and registering a provider just
        /// for this is more moving parts than the job needs — the receipt character set is small and
        /// fixed, so map it directly. Anything unmappable becomes '?' rather than a wrong glyph.
        /// </summary>
        public static byte[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                bytes[i] = c switch
                {
                    '£' => 0x9C,
                    '€' => 0xEE,   // CP437 has no €; 0xEE is 'ε' on most printers — close enough to spot
                    '–' or '—' => (byte)'-',
                    '‘' or '’' => (byte)'\'',
                    '“' or '”' => (byte)'"',
                    '…' => (byte)'.',
                    '×' => (byte)'x',
                    '✓' => (byte)'*',
                    '°' => 0xF8,
                    'é' => 0x82, 'è' => 0x8A, 'ê' => 0x88, 'ë' => 0x89,
                    'á' => 0xA0, 'à' => 0x85, 'â' => 0x83, 'ä' => 0x84,
                    'ó' => 0xA2, 'ò' => 0x95, 'ô' => 0x93, 'ö' => 0x94,
                    'ú' => 0xA3, 'ù' => 0x97, 'û' => 0x96, 'ü' => 0x81,
                    'í' => 0xA1, 'ì' => 0x8D, 'î' => 0x8C, 'ï' => 0x8B,
                    'ñ' => 0xA4, 'Ñ' => 0xA5, 'ç' => 0x87, 'Ç' => 0x80,
                    _ when c < 0x80 => (byte)c,
                    _ => (byte)'?',
                };
            }
            return bytes;
        }

        /// <summary>Left/right justified within the paper width — "Total" ......... "£12.34".
        /// The printer's own alignment can't do two-column, so the till builds these as one line.</summary>
        public static string TwoColumn(string left, string right, int columns)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            var gap = columns - left.Length - right.Length;
            if (gap < 1)
            {
                // truncate the LEFT (an item name) — never the money
                var room = Math.Max(0, columns - right.Length - 1);
                left = left.Length > room ? left[..room] : left;
                gap = Math.Max(1, columns - left.Length - right.Length);
            }
            return left + new string(' ', gap) + right;
        }

        private static void Write(Stream s, params byte[] bytes) => s.Write(bytes, 0, bytes.Length);
    }
}
