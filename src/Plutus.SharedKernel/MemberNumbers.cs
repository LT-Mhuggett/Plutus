using System;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// FE2 membership numbers: a short, human-typeable, scannable customer id.
///
/// Shape: <c>NNNNNNC</c> — a 6-digit per-tenant sequence plus one check character, e.g.
/// <c>000482K</c>. On a card the barcode payload carries a <see cref="Prefix"/> ("C…") so the
/// till's scan handler can tell a member card from a product EAN or a receipt's sale id.
///
/// Why this shape: short enough to read down a phone line, entirely within the Code 39 charset
/// (so the existing hand-rolled renderer prints it and cheap scanners read it — unlike a 36-char
/// UUID), and the check character catches mis-keys and mis-scans before they become a wrong
/// customer.
///
/// The check character comes from <see cref="Crockford32.Alphabet"/> — the codebase's existing
/// human-keyable set, which drops I/L/O/U so a card can be read aloud without "was that a one or
/// an I?". (The classic Code 39 mod-43 set is worse still: it includes space, '$', '%', '+', '.'
/// and '/'.) Input is folded through <see cref="Crockford32.Normalise"/>, so someone typing O for
/// 0 or I for 1 still lands on the right customer. Weights (7,3,1 repeating) mean transposing two
/// adjacent digits changes the sum, which a plain digit-sum would miss.
///
/// ⚠⚠ **WHY THIS LIVES IN SharedKernel, AND WHY THE ALLOCATOR DOES NOT.** The *format* is a rule —
/// "does this scan belong to a member, and which one" — and a till has to answer it offline, at the
/// scanner, before any server is involved. MAUI may not reference `Plutus.Customers` (it is a
/// backend module), so leaving it there meant the MAUI till re-deriving the check character in a
/// second implementation: a C2 twin whose failure mode is a card that validates on one till and is
/// rejected on the next. C1 register, `till-design.md`.
///
/// ⚠ **The ALLOCATOR is deliberately NOT here.** Handing out the *next* number needs a database and
/// a tenant-wide counter, and it is server-only for a reason a till cannot work around: two offline
/// tills would mint the same number (binding default 20/21). It stays in
/// `Plutus.Customers/MemberNoAllocator.cs`. The split is the point — the *rule* travels, the
/// *sequence* does not.
/// </summary>
public static class MemberNumbers
{
    /// <summary>Barcode-payload prefix that marks a scan as a member card.</summary>
    public const string Prefix = "C";
    public const int SequenceDigits = 6;

    private static readonly int[] Weights = { 7, 3, 1 };

    /// <summary>The check character for a digit string (its canonical form appends this).</summary>
    public static char CheckChar(string digits)
    {
        if (string.IsNullOrEmpty(digits)) throw new ArgumentException("digits required", nameof(digits));
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            if (!char.IsDigit(digits[i])) throw new ArgumentException("digits only", nameof(digits));
            sum += (digits[i] - '0') * Weights[i % Weights.Length];
        }
        return Crockford32.Alphabet[sum % Crockford32.Alphabet.Length];
    }

    /// <summary>
    /// Sequence number → member number, e.g. 482 → "000482K".
    ///
    /// ⚠⚠ **THE CEILING IS <see cref="SequenceDigits"/> DIGITS — 1,000,000 members per tenant — AND
    /// PAST IT A NUMBER FORMATS BUT CANNOT BE READ BACK.** Found by the round-trip test on
    /// 2026-08-13; the comment here previously claimed *"past 999,999 the number simply grows a digit
    /// rather than wrapping into a collision"*, which is true and beside the point:
    /// <see cref="TryCanonicalise"/> keys off length (it must — see its comment) and accepts only
    /// <see cref="SequenceDigits"/>+1, so sequence 1,000,000 formats as "10000007" and then
    /// canonicalises to **null**. The card prints, scans, and resolves to nobody.
    ///
    /// ⚠ **The fix, if a tenant ever approaches it, is to widen <see cref="SequenceDigits"/>** — both
    /// halves read that one constant, so they widen in step and every existing shorter number keeps
    /// working (they are zero-padded, so "000482K" is unchanged at 7 digits). It is NOT to loosen the
    /// parser: accepting arbitrary lengths would make a bare **EAN-8** product barcode canonicalise
    /// as a member number roughly 3% of the time, which is a far worse failure at a counter than a
    /// ceiling nobody has reached.
    /// </summary>
    public static string Format(long sequence)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var digits = sequence.ToString(new string('0', SequenceDigits));
        return digits + CheckChar(digits);
    }

    /// <summary>What goes in the barcode on a printed card.</summary>
    public static string BarcodePayload(string memberNo) => Prefix + memberNo;

    /// <summary>
    /// Turn anything a human or scanner might supply into the canonical member number, or null
    /// when the input is not one. Accepts: the bare number ("000482K"), the barcode payload
    /// ("C000482K"), the sequence without its check character ("000482", "482"), and any of those
    /// in lower case or with spaces/hyphens. A 7+ character candidate whose check character does
    /// not verify is REJECTED (null) — that is the whole point of having one.
    /// </summary>
    public static string? TryCanonicalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var stripped = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        // Crockford folding (O→0, I/L→1, U→V) so a hand-typed card number still resolves.
        var s = Crockford32.Normalise(stripped);
        if (s.Length > 1 && s.StartsWith(Prefix, StringComparison.Ordinal)) s = s[Prefix.Length..];
        if (s.Length == 0) return null;

        // LENGTH first, not "all digits": a check character is often itself a digit (e.g.
        // sequence 1 → "0000011"), so a full number can be all digits and must not be mistaken
        // for a bare sequence.
        if (s.Length == SequenceDigits + 1)
        {
            var body = s[..^1];
            if (!body.All(char.IsDigit)) return null;
            return CheckChar(body) == s[^1] ? s : null;
        }

        // shorter, all digits → a sequence without its check character ("482" → "000482" + check)
        if (s.Length <= SequenceDigits && s.All(char.IsDigit))
        {
            var digits = s.PadLeft(SequenceDigits, '0');
            return digits + CheckChar(digits);
        }

        return null;
    }

    /// <summary>True when <paramref name="memberNo"/> is a well-formed number with a valid
    /// check character.</summary>
    public static bool IsValid(string? memberNo) =>
        !string.IsNullOrWhiteSpace(memberNo) && TryCanonicalise(memberNo) == memberNo.Trim().ToUpperInvariant()
        && memberNo.Trim().Length == SequenceDigits + 1;

    /// <summary>
    /// Does this scanned/typed code look like a MEMBER CARD rather than a product barcode or a
    /// receipt's sale id? True only for the prefixed payload with a valid check character.
    ///
    /// ⚠ **Deliberately stricter than <see cref="TryCanonicalise"/>**, and the difference matters at
    /// a scanner. `TryCanonicalise` accepts a bare "482" because a human reading a card down the
    /// phone will say "four eight two" — but a *scan* of a six-digit product barcode must not be
    /// hijacked into a customer lookup. So the routing test requires the <see cref="Prefix"/>;
    /// typing the short form still works because that path goes through search, not the scanner.
    /// Step 27's scan handler: prefixed and valid → attach the customer; prefixed and INVALID → say
    /// the card did not scan cleanly, rather than searching for an item that cannot exist.
    ///
    /// ⚠ **The length and prefix tests are REDUNDANT, and kept deliberately.** Mutation-checked
    /// 2026-08-13: removing either survives the suite, because <see cref="TryCanonicalise"/> already
    /// rejects everything they reject (only a "C"-prefixed 8-character code can canonicalise at
    /// all). They are belt-and-braces for the loosening <see cref="Format"/>'s comment warns
    /// against — if the parser ever accepts longer inputs, THIS is the guard that stops a bare
    /// EAN-8 becoming a member lookup, and it should not have to be re-derived then. Dropping the
    /// check-character verification, by contrast, is caught immediately.
    /// </summary>
    public static bool LooksLikeMemberScan(string? scanned)
    {
        if (string.IsNullOrWhiteSpace(scanned)) return false;
        var stripped = new string(scanned.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        var s = Crockford32.Normalise(stripped);
        return s.Length == Prefix.Length + SequenceDigits + 1
            && s.StartsWith(Prefix, StringComparison.Ordinal)
            && TryCanonicalise(s) != null;
    }
}
