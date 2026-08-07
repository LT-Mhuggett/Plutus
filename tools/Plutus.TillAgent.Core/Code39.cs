using System;
using System.Collections.Generic;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// Code 39 as dot columns, for the raster path. Same pattern table as the till's on-screen
    /// Barcode39.tsx and the same charset logic — uppercase, unsupported characters dropped —
    /// so screen, ESC/POS and raster all encode identically.
    /// </summary>
    public static class Code39
    {
        // 9 elements per glyph (bar,space,bar,…,bar); '1' = wide, '0' = narrow. Exactly 3 wide.
        private static readonly Dictionary<char, string> Patterns = new()
        {
            ['0'] = "000110100", ['1'] = "100100001", ['2'] = "001100001", ['3'] = "101100000",
            ['4'] = "000110001", ['5'] = "100110000", ['6'] = "001110000", ['7'] = "000100101",
            ['8'] = "100100100", ['9'] = "001100100",
            ['A'] = "100001001", ['B'] = "001001001", ['C'] = "101001000", ['D'] = "000011001",
            ['E'] = "100011000", ['F'] = "001011000", ['G'] = "000001101", ['H'] = "100001100",
            ['I'] = "001001100", ['J'] = "000011100", ['K'] = "100000011", ['L'] = "001000011",
            ['M'] = "101000010", ['N'] = "000010011", ['O'] = "100010010", ['P'] = "001010010",
            ['Q'] = "000000111", ['R'] = "100000110", ['S'] = "001000110", ['T'] = "000010110",
            ['U'] = "110000001", ['V'] = "011000001", ['W'] = "111000000", ['X'] = "010010001",
            ['Y'] = "110010000", ['Z'] = "011010000",
            ['-'] = "010000101", ['.'] = "110000100", [' '] = "011000100",
            ['*'] = "010010100", // start/stop sentinel
        };

        /// <summary>Uppercase and drop anything Code 39 can't carry (mirrors the screen version).</summary>
        public static string Encodable(string value)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in (value ?? string.Empty).ToUpperInvariant())
                if (c != '*' && Patterns.ContainsKey(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// One dot row of the barcode (true = black), fitted to <paramref name="maxDots"/>.
        /// Tries chunky modules first and narrows until the framed payload fits — a 6-char gift
        /// card gets fat, scanner-friendly bars; a 36-char sale UUID drops to the thinnest 2:1
        /// modules (the same squeeze the on-screen SVG's `fit` does). Returns null when even the
        /// thinnest encoding cannot fit, or nothing survives sanitising.
        /// </summary>
        public static bool[]? Dots(string value, int maxDots)
        {
            var text = Encodable(value);
            if (text.Length == 0) return null;
            var framed = "*" + text + "*";

            // (narrow, wide) candidates, widest first. Char cost = 3*wide + 6*narrow + narrow gap.
            foreach (var (narrow, wide) in new[] { (2, 6), (2, 5), (1, 3), (1, 2) })
            {
                var width = framed.Length * (3 * wide + 7 * narrow) - narrow; // no gap after last char
                if (width > maxDots) continue;

                var dots = new bool[width];
                var x = 0;
                foreach (var ch in framed)
                {
                    var pattern = Patterns[ch];
                    for (var i = 0; i < pattern.Length; i++)
                    {
                        var w = pattern[i] == '1' ? wide : narrow;
                        if (i % 2 == 0) for (var d = 0; d < w; d++) dots[x + d] = true; // even = bar
                        x += w;
                    }
                    x += narrow; // inter-character gap (overruns width by one gap at the end — trimmed by length)
                }
                return dots;
            }
            return null;
        }
    }
}
