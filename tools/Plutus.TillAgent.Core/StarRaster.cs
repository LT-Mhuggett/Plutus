using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// FE3.1: STAR Raster Graphics mode — the ONLY language a Star TSP100/TSP143 (futurePRNT)
    /// speaks. Those printers have no text mode at all: raw ESC/POS is discarded unprinted, which
    /// is why the agent's original ESC/POS path produced nothing (or runaway feeds) on them.
    ///
    /// This renderer takes pre-packed 1-bit image rows (see the agent's ReceiptRasterizer) and
    /// frames them as a raster job. Command bytes verified against three mutually-consistent
    /// sources: Star's "STAR Graphic Mode Command Specifications" Rev 2.32 (the applicable-models
    /// list is exactly the TSP100 futurePRNT family), Star's own starcupsdrv rastertostar.c, and
    /// receiptline's stargraphic driver.
    ///
    /// The two gotchas that make or break this on hardware:
    ///  • Numeric parameters of ESC * r commands are ASCII DECIMAL DIGITS, NUL-terminated —
    ///    partial cut 13 is "13" (0x31 0x33 0x00), never a binary 0x0D. Only the row-data
    ///    commands ('b'/'k') carry a binary little-endian count.
    ///  • Every setting command is IGNORED once any row data is buffered — the whole settings
    ///    block must sit between "enter raster" and the first 'b'.
    /// </summary>
    public static class StarRaster
    {
        private const byte ESC = 0x1B;

        /// <summary>72mm printable width @ 203dpi (80mm paper) = 576 dots = 72 bytes/row.</summary>
        public const int Dots80mm = 576;
        /// <summary>51mm printable width (58mm paper) = 408 dots = 51 bytes/row.</summary>
        public const int Dots58mm = 408;

        public sealed class Options
        {
            /// <summary>Kick drawer 1 as part of the job (fires at job start, like futurePRNT).</summary>
            public bool OpenDrawer { get; set; }
            /// <summary>58mm paper (51mm print area) instead of 80mm/72mm.</summary>
            public bool Narrow58mm { get; set; }
        }

        /// <summary>
        /// A complete print job. <paramref name="rows"/> is top-to-bottom packed dot rows
        /// (MSB of byte 0 = leftmost dot); an empty array means a blank row. Blank runs become a
        /// single "move down n lines" command; trailing blanks are dropped entirely — the
        /// continuous page mode ("P0") plus end-of-document feed-and-cut means the printer feeds
        /// EXACTLY the printed height plus its own feed-to-cutter. No driver, no page sizes, no
        /// blank tail — the property the GDI/browser path could never guarantee.
        /// </summary>
        public static byte[] RenderJob(IReadOnlyList<byte[]> rows, Options options)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            options ??= new Options();
            using var ms = new MemoryStream();

            // line-mode preamble (accepted before raster mode; starcupsdrv sends the same)
            ms.Write(new byte[] { ESC, (byte)'@' });                        // initialise
            ms.Write(new byte[] { ESC, 0x1E, (byte)'A', options.Narrow58mm ? (byte)0x01 : (byte)0x00 }); // print area
            if (options.OpenDrawer)
                ms.Write(new byte[] { ESC, 0x07, 0x14, 0x14 });             // drawer pulse 200ms/200ms

            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'R' }); // initialise raster mode
            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'A' }); // enter raster mode
            WriteAsciiParam(ms, 'Q', 1);                                    // quality: normal
            WriteAsciiParam(ms, 'P', 0);                                    // page length 0 = continuous
            if (options.OpenDrawer) WriteAsciiParam(ms, 'D', 1);            // kick drawer 1 at job start
            WriteAsciiParam(ms, 'E', 13);                                   // EOT: feed to cutter + partial cut

            // image rows: coalesce blank runs, drop trailing blanks
            var lastInk = -1;
            for (var i = rows.Count - 1; i >= 0; i--)
                if (rows[i] is { Length: > 0 }) { lastInk = i; break; }

            var blankRun = 0;
            for (var i = 0; i <= lastInk; i++)
            {
                var row = rows[i];
                if (row == null || row.Length == 0) { blankRun++; continue; }
                if (blankRun > 0) { WriteAsciiParam(ms, 'Y', blankRun); blankRun = 0; }
                var len = Math.Min(row.Length, options.Narrow58mm ? Dots58mm / 8 : Dots80mm / 8);
                ms.Write(new byte[] { (byte)'b', (byte)(len & 0xFF), (byte)(len >> 8) });
                ms.Write(row, 0, len);
            }

            ms.Write(new byte[] { ESC, 0x0C, 0x04 });                       // ESC FF EOT — print, feed, cut
            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'B' }); // quit raster mode
            return ms.ToArray();
        }

        /// <summary>Drawer kick with no printing — a raster-mode job whose only payload is the
        /// drawer command (fires immediately because the image buffer is empty).</summary>
        public static byte[] DrawerOnlyJob()
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { ESC, 0x07, 0x14, 0x14 });                 // pulse 200ms/200ms
            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'R' });
            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'A' });
            WriteAsciiParam(ms, 'D', 1);
            ms.Write(new byte[] { ESC, (byte)'*', (byte)'r', (byte)'B' });
            return ms.ToArray();
        }

        /// <summary>Pack one dot row: true = black. MSB of each byte is the leftmost dot;
        /// trailing all-white bytes are trimmed (the printer treats short rows as white-padded).
        /// Returns an empty array for an all-white row.</summary>
        public static byte[] PackRow(ReadOnlySpan<bool> dots)
        {
            var bytes = new byte[(dots.Length + 7) / 8];
            var last = -1;
            for (var i = 0; i < dots.Length; i++)
            {
                if (!dots[i]) continue;
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
                last = i / 8;
            }
            if (last < 0) return Array.Empty<byte>();
            var trimmed = new byte[last + 1];
            Array.Copy(bytes, trimmed, last + 1);
            return trimmed;
        }

        /// <summary>ESC * r X &lt;ascii digits&gt; NUL — the raster parameter form. The decimal-as-ASCII
        /// encoding is the spec's single most missable detail.</summary>
        private static void WriteAsciiParam(Stream s, char command, int value)
        {
            s.WriteByte(ESC);
            s.WriteByte((byte)'*');
            s.WriteByte((byte)'r');
            s.WriteByte((byte)command);
            var digits = Encoding.ASCII.GetBytes(value.ToString());
            s.Write(digits, 0, digits.Length);
            s.WriteByte(0x00);
        }
    }
}
