using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Plutus.TillAgent.Core;

namespace Plutus.TillAgent
{
    /// <summary>
    /// FE3.1: renders a <see cref="PrintDocument"/> to 1-bit raster rows for the Star raster path
    /// (TSP100 family — no text mode, so the agent must draw the receipt as an image).
    ///
    /// Layout mirrors the ESC/POS renderer's semantics: monospace text sized so exactly
    /// <c>Columns</c> characters span the printable width (so the till's TwoColumn-padded lines
    /// align), bold/underline/double-size via font styles, rules as dashes, Code 39 barcodes from
    /// the shared generator. Anti-aliasing is OFF — thermal printing is 1-bit, and thresholded AA
    /// speckles.
    /// </summary>
    public static class ReceiptRasterizer
    {
        public static List<byte[]> Rasterize(PrintDocument doc, out bool narrow58mm, out bool wantsDrawer)
        {
            using var bmp = RasterizeToBitmap(doc, out narrow58mm, out wantsDrawer);
            return ToRows(bmp);
        }

        /// <summary>FE3.3: the rendered receipt as a bitmap — shared by the Star-raster packer and
        /// the GDI print path (which hands this to the vendor driver as a normal print job).</summary>
        public static Bitmap RasterizeToBitmap(PrintDocument doc, out bool narrow58mm, out bool wantsDrawer)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            narrow58mm = doc.Columns == 32;
            wantsDrawer = doc.OpenDrawer;
            var widthDots = narrow58mm ? StarRaster.Dots58mm : StarRaster.Dots80mm;
            var columns = doc.Columns > 0 ? doc.Columns : 42;

            using var probe = new Bitmap(1, 1);
            using var pg = Graphics.FromImage(probe);
            using var baseFont = FitFont(pg, (float)widthDots / columns);
            using var boldFont = new Font(baseFont, FontStyle.Bold);
            using var largeFont = new Font(baseFont.FontFamily, baseFont.Size * 2, FontStyle.Bold);
            var lineH = (int)Math.Ceiling(baseFont.GetHeight(pg)) + 2;
            var largeH = (int)Math.Ceiling(largeFont.GetHeight(pg)) + 2;

            // generous height estimate; cropped to last ink afterwards
            var estimate = 32;
            foreach (var op in doc.Ops ?? new List<PrintOp>())
                estimate += op.Kind switch
                {
                    PrintOpKind.Barcode => Math.Max(8, op.Height) + lineH,
                    PrintOpKind.Text when op.Large => largeH,
                    PrintOpKind.Cut => 0,
                    PrintOpKind.Drawer => 0,
                    _ => lineH,
                };

            var bmp = new Bitmap(widthDots, Math.Max(estimate, 64), PixelFormat.Format32bppRgb);
            var contentY = 0;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                var y = 0;
                var format = StringFormat.GenericTypographic;

                foreach (var op in doc.Ops ?? new List<PrintOp>())
                {
                    switch (op.Kind)
                    {
                        case PrintOpKind.Text:
                        {
                            var style = FontStyle.Regular;
                            if (op.Bold || op.Large) style |= FontStyle.Bold;
                            if (op.Underline) style |= FontStyle.Underline;
                            using var font = new Font(baseFont.FontFamily, op.Large ? baseFont.Size * 2 : baseFont.Size, style);
                            var text = op.Text ?? string.Empty;
                            var w = text.Length == 0 ? 0 : g.MeasureString(text, font, int.MaxValue, format).Width;
                            var x = op.Align switch
                            {
                                PrintAlign.Centre => Math.Max(0, (widthDots - w) / 2),
                                PrintAlign.Right => Math.Max(0, widthDots - w),
                                _ => 0f,
                            };
                            if (text.Length > 0) g.DrawString(text, font, Brushes.Black, x, y, format);
                            y += op.Large ? largeH : lineH;
                            break;
                        }
                        case PrintOpKind.Rule:
                            g.DrawString(new string('-', Math.Max(1, columns)), baseFont, Brushes.Black, 0, y, format);
                            y += lineH;
                            break;
                        case PrintOpKind.Barcode:
                        {
                            var dots = Code39.Dots(op.Text ?? string.Empty, widthDots);
                            if (dots != null)
                            {
                                var height = Math.Clamp(op.Height, 8, 255);
                                var x0 = (widthDots - dots.Length) / 2;
                                for (var i = 0; i < dots.Length; i++)
                                    if (dots[i]) g.FillRectangle(Brushes.Black, x0 + i, y, 1, height);
                                y += height + 6;
                            }
                            break;
                        }
                        case PrintOpKind.Cut:
                            // the raster job's end-of-document already feeds to the cutter and cuts
                            break;
                        case PrintOpKind.Drawer:
                            wantsDrawer = true;
                            break;
                    }
                }
                contentY = y;
            }

            // Crop to the printed height (+ a small margin): trailing white in the bitmap becomes
            // fed blank paper on both the raster and GDI paths.
            var printedHeight = Math.Clamp(contentY + 4, 8, bmp.Height);
            if (printedHeight < bmp.Height)
            {
                var cropped = bmp.Clone(new Rectangle(0, 0, widthDots, printedHeight), bmp.PixelFormat);
                bmp.Dispose();
                return cropped;
            }
            return bmp;
        }

        /// <summary>Largest monospace size whose character cell fits the per-column width, so a
        /// Columns-character line exactly spans the paper like it does on the ESC/POS path.</summary>
        private static Font FitFont(Graphics g, float cellWidth)
        {
            foreach (var family in new[] { "Consolas", "Courier New" })
            {
                try
                {
                    for (var size = 26f; size >= 8f; size -= 0.5f)
                    {
                        var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
                        var w = g.MeasureString("0000000000", font, int.MaxValue, StringFormat.GenericTypographic).Width / 10f;
                        if (w <= cellWidth) return font;
                        font.Dispose();
                    }
                }
                catch (ArgumentException) { /* family missing — try the next */ }
            }
            return new Font(FontFamily.GenericMonospace, 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>Threshold to packed rows (dark pixel = dot), trimmed of trailing blank rows.</summary>
        private static List<byte[]> ToRows(Bitmap bmp)
        {
            var rows = new List<byte[]>(bmp.Height);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var buffer = new byte[stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (var yy = 0; yy < bmp.Height; yy++)
                {
                    var lineStart = yy * stride;
                    var dots = new bool[bmp.Width];
                    for (var xx = 0; xx < bmp.Width; xx++)
                        dots[xx] = buffer[lineStart + xx * 4 + 1] < 128; // green channel — we only draw black/white
                    rows.Add(StarRaster.PackRow(dots));
                }
            }
            finally { bmp.UnlockBits(data); }

            while (rows.Count > 0 && rows[^1].Length == 0) rows.RemoveAt(rows.Count - 1);
            return rows;
        }
    }
}
