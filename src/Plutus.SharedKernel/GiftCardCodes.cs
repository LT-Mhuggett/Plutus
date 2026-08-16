using System;
using System.Linq;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// FE7 gift-card codes: <c>XXXXXXXXXXXXC</c> — 12 random Crockford32 characters plus one check
    /// character, printed in groups of four ("K7QP-2M9W-XT4R-8") and as a Code 39 barcode carrying a
    /// <see cref="Prefix"/> ("G…") so the till's scan handler can tell a voucher from a product EAN,
    /// a member card ("C…") or a receipt's sale id.
    ///
    /// Why RANDOM rather than sequential (member numbers are sequential): a gift card is a bearer
    /// instrument. A guessable code is a licence to print money — someone who buys card 000042 could
    /// try 000043 and spend a stranger's balance. 12 Crockford characters is 60 bits, so guessing is
    /// hopeless even for an attacker who owns a card.
    ///
    /// The alphabet drops I/L/O/U (<see cref="Crockford32.Alphabet"/>) so a code can be read down the
    /// phone, and input is folded through <see cref="Crockford32.Normalise"/> so someone typing O for
    /// 0 still lands on the right card. The check character (weights 7,3,1 repeating over the
    /// alphabet's index values) catches mis-keys and mis-scans BEFORE they become a lookup against
    /// someone else's card — and catches transpositions, which a plain sum would miss.
    /// </summary>
    public static class GiftCardCodes
    {
        /// <summary>Barcode-payload prefix that marks a scan as a gift card.</summary>
        public const string Prefix = "G";
        public const int BodyLength = 12;
        public const int TotalLength = BodyLength + 1;

        private static readonly int[] Weights = { 7, 3, 1 };

        /// <summary>The check character for a code body (its canonical form appends this).</summary>
        public static char CheckChar(string body)
        {
            if (string.IsNullOrEmpty(body)) throw new ArgumentException("body required", nameof(body));
            var sum = 0;
            for (var i = 0; i < body.Length; i++)
            {
                var index = Crockford32.Alphabet.IndexOf(body[i]);
                if (index < 0) throw new ArgumentException($"'{body[i]}' is not a Crockford32 character.", nameof(body));
                sum += index * Weights[i % Weights.Length];
            }
            return Crockford32.Alphabet[sum % Crockford32.Alphabet.Length];
        }

        /// <summary>A fresh random code, check character included.</summary>
        public static string New()
        {
            var body = Crockford32.NewCode(BodyLength);
            return body + CheckChar(body);
        }

        /// <summary>What goes in the barcode on a printed voucher.</summary>
        public static string BarcodePayload(string code) => Prefix + code;

        /// <summary>Grouped for printing / reading aloud: "K7QP-2M9W-XT4R-8".</summary>
        public static string Pretty(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var s = code.Trim().ToUpperInvariant();
            return string.Join("-", Enumerable.Range(0, (s.Length + 3) / 4)
                .Select(i => s.Substring(i * 4, Math.Min(4, s.Length - i * 4))));
        }

        /// <summary>
        /// Turn anything a human or a scanner might supply into the canonical code, or null when the
        /// input is not one. Accepts the bare code, the barcode payload ("G…"), the pretty form with
        /// hyphens or spaces, and lower case. A code whose check character does not verify is
        /// REJECTED — that is the entire point of having one.
        /// </summary>
        /// <remarks>⚠ NULLABILITY IS EXPLICIT NOW. This class moved into SharedKernel on 2026-08-16
        /// so MAUI could route a scan without a server round trip, and SharedKernel enables nullable
        /// reference types where `Plutus.Customers` did not — so "not a card" is `null` in the
        /// signature as well as in the contract. No behaviour changed.</remarks>
        public static string? TryCanonicalise(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var stripped = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            var s = Crockford32.Normalise(stripped);
            // Only strip the prefix at full payload length: a bare code may legitimately START with
            // 'G' (it's in the alphabet), and stripping that would corrupt one card into another.
            if (s.Length == TotalLength + Prefix.Length && s.StartsWith(Prefix, StringComparison.Ordinal))
                s = s[Prefix.Length..];
            if (s.Length != TotalLength) return null;
            if (!s.All(c => Crockford32.Alphabet.IndexOf(c) >= 0)) return null;
            return CheckChar(s[..BodyLength]) == s[^1] ? s : null;
        }

        /// <summary>True when the input could be a gift-card scan — used by the till to route a
        /// scan without a server round trip. Deliberately checks the check character too, so a
        /// mis-scanned product barcode starting with G is not treated as a card.</summary>
        public static bool LooksLikeCard(string? input) => TryCanonicalise(input) != null;
    }
}
