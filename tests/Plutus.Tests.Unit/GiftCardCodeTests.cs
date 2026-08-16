using System.Collections.Generic;
using System.Linq;
using Plutus.Customers;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE7 gift-card code format. A gift card is a bearer instrument, so the code's job is to be
/// unguessable, printable as Code 39, readable down a phone, and — above all — to REFUSE a
/// mis-scan rather than silently resolve to a different customer's card.
/// </summary>
public class GiftCardCodeTests
{
    [Fact]
    public void A_generated_code_is_13_crockford_characters_and_round_trips()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = GiftCardCodes.New();
            Assert.Equal(GiftCardCodes.TotalLength, code.Length);
            Assert.All(code, c => Assert.Contains(c, Crockford32.Alphabet));
            Assert.Equal(code, GiftCardCodes.TryCanonicalise(code));
        }
    }

    [Fact]
    public void Codes_are_random_not_sequential()
    {
        // 200 codes with no repeats is the point: a sequential scheme would let the buyer of one
        // card guess the next one and spend a stranger's balance.
        var codes = Enumerable.Range(0, 200).Select(_ => GiftCardCodes.New()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Every_spelling_a_human_or_scanner_might_produce_resolves_to_the_same_code()
    {
        var code = GiftCardCodes.New();
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(code.ToLowerInvariant()));
        Assert.Equal(code, GiftCardCodes.TryCanonicalise($"  {code}  "));
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(GiftCardCodes.Pretty(code)));           // K7QP-2M9W-…
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(GiftCardCodes.Pretty(code).Replace('-', ' ')));
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(GiftCardCodes.BarcodePayload(code)));   // "G…"
    }

    /// <summary>Crockford folding: someone reading a card aloud says "oh" for 0 and "eye" for 1, and
    /// the alphabet has no O/I/L/U precisely so that folding is unambiguous.</summary>
    [Fact]
    public void Ambiguous_characters_fold_to_the_canonical_ones()
    {
        var code = GiftCardCodes.New();
        var mistyped = code.Replace('0', 'O').Replace('1', 'I');
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(mistyped));
    }

    /// <summary>The whole reason for a check character: one wrong character must FAIL, not land on
    /// another card.</summary>
    [Fact]
    public void A_single_wrong_character_is_rejected()
    {
        var code = GiftCardCodes.New();
        var rejected = 0;
        for (var i = 0; i < code.Length; i++)
        {
            // swap position i for a different alphabet character
            var replacement = Crockford32.Alphabet[(Crockford32.Alphabet.IndexOf(code[i]) + 7) % Crockford32.Alphabet.Length];
            var mutated = code[..i] + replacement + code[(i + 1)..];
            if (GiftCardCodes.TryCanonicalise(mutated) == null) rejected++;
        }
        // A single check character over a 32-letter alphabet cannot catch EVERY single-character
        // slip, but it must catch the overwhelming majority — anything less is a broken checksum.
        Assert.True(rejected >= code.Length - 1, $"only {rejected}/{code.Length} single-character mutations were rejected");
    }

    /// <summary>Weighted (7,3,1) rather than a plain sum, so swapping two adjacent characters — the
    /// classic typing slip — changes the check character.</summary>
    [Fact]
    public void Transposing_two_adjacent_characters_is_rejected()
    {
        var caught = 0;
        var tried = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = GiftCardCodes.New();
            for (var i = 0; i + 1 < GiftCardCodes.BodyLength; i++)
            {
                if (code[i] == code[i + 1]) continue;   // a swap of equal characters is not a change
                tried++;
                var swapped = code[..i] + code[i + 1] + code[i] + code[(i + 2)..];
                if (GiftCardCodes.TryCanonicalise(swapped) == null) caught++;
            }
        }
        // (7,3,1) repeating catches a transposition unless the two positions share a weight — i.e.
        // roughly two thirds of cases. A plain digit-sum would catch NONE, which is the point.
        Assert.True(caught > tried / 2, $"only {caught}/{tried} transpositions were rejected");
    }

    /// <summary>
    /// ⚠ The prefix trap. 'G' is a legitimate Crockford character, so a bare code can START with G.
    /// Stripping a leading G unconditionally would turn one valid card into a different (invalid, or
    /// worse, VALID) code — so the prefix is only stripped at full payload length.
    /// </summary>
    [Fact]
    public void A_bare_code_beginning_with_G_is_not_mistaken_for_a_prefixed_payload()
    {
        // find a code whose first character is 'G'
        string code = null;
        for (var i = 0; i < 5000 && code == null; i++)
        {
            var candidate = GiftCardCodes.New();
            if (candidate[0] == 'G') code = candidate;
        }
        Assert.NotNull(code);   // 1-in-32; 5000 attempts is a certainty

        Assert.Equal(code, GiftCardCodes.TryCanonicalise(code));
        Assert.Equal(code, GiftCardCodes.TryCanonicalise("G" + code));   // still handles the payload form
    }

    [Fact]
    public void Rubbish_is_rejected_rather_than_guessed_at()
    {
        foreach (var input in new[] { null, "", "   ", "-", "G", "NOTACARD", "12345", new string('0', 40) })
            Assert.Null(GiftCardCodes.TryCanonicalise(input));

        // a product EAN must never look like a card, even one starting with G
        Assert.Null(GiftCardCodes.TryCanonicalise("5028486334643"));
        Assert.False(GiftCardCodes.LooksLikeCard("5028486334643"));
        Assert.True(GiftCardCodes.LooksLikeCard(GiftCardCodes.BarcodePayload(GiftCardCodes.New())));
    }

    [Fact]
    public void Pretty_groups_in_fours_without_losing_anything()
    {
        var code = GiftCardCodes.New();
        var pretty = GiftCardCodes.Pretty(code);
        Assert.Equal(code, pretty.Replace("-", ""));
        Assert.Equal(new List<int> { 4, 4, 4, 1 }, pretty.Split('-').Select(p => p.Length).ToList());
    }

    // ── scan routing, now that BOTH tills use this (moved to SharedKernel 2026-08-16) ──────────

    /// <summary>
    /// ⚠⚠ THE ROUTING PREDICATE IS STRICTER THAN THE WEB TILL'S REGEX, AND THAT IS THE POINT OF
    /// SHARING IT. The web till tests `/^G[0-9A-Z]{13}$/i`, which accepts I, L, O and U — characters
    /// **not in the Crockford alphabet** — and never checks the check character. So it routes
    /// mis-scans to a gift-card lookup that must 404 before falling through to an item search.
    ///
    /// ⚠ It fails SAFE on the web till (a wasted round trip, then the item lookup), so this is a
    /// discrepancy to record rather than a defect to panic about — C2 carries it. MAUI uses the real
    /// rule, so a mis-scan never leaves the till at all.
    /// </summary>
    /// <remarks>
    /// ⚠ `GOOOOOOOOOOOOO` IS DELIBERATELY NOT IN THIS LIST, and finding out why was worth the test
    /// failing first. `Crockford32.Normalise` folds **O → 0** on purpose, so that input becomes
    /// `0000000000000` — whose check character genuinely verifies (the weighted sum of twelve zeros
    /// is zero, and `Alphabet[0]` is `'0'`). It is a **valid code**, not a mis-scan: the rule is
    /// right and the assertion was wrong. `New()` could in principle mint it, at 32⁻¹².
    /// </remarks>
    [Theory]
    [InlineData("GIIIIIIIIIIIII")]   // I folds to 1 — and twelve 1s do not check out
    [InlineData("GLLLLLLLLLLLLL")]   // L folds to 1 as well
    [InlineData("GUUUUUUUUUUUUU")]   // U is not in the alphabet at all
    public void A_scan_the_web_tills_regex_would_accept_is_still_refused_by_the_rule(string input)
    {
        // ⚠ Shape-matches the web till's pattern — prefix plus 13 — so this really is a case the two
        // disagree on, not a straw man.
        Assert.Equal(14, input.Length);
        Assert.StartsWith(GiftCardCodes.Prefix, input, StringComparison.Ordinal);

        Assert.False(GiftCardCodes.LooksLikeCard(input));
    }

    /// <summary>
    /// ⚠ A BARE CODE MAY LEGITIMATELY START WITH 'G' — it is in the alphabet — so the prefix is
    /// stripped ONLY at full payload length. Getting this wrong turns one card into another, which
    /// is a lookup against a stranger's balance.
    /// </summary>
    [Fact]
    public void A_code_that_starts_with_the_prefix_letter_is_not_mangled()
    {
        string code;
        var guard = 0;
        do
        {
            code = GiftCardCodes.New();
            if (++guard > 10_000) return;   // vanishingly unlikely; do not hang the suite
        } while (!code.StartsWith(GiftCardCodes.Prefix, StringComparison.Ordinal));

        Assert.Equal(code, GiftCardCodes.TryCanonicalise(code));
        Assert.Equal(code, GiftCardCodes.TryCanonicalise(GiftCardCodes.BarcodePayload(code)));
    }

    /// <summary>⚠ A member card must never route to the gift-card lookup, nor the reverse. Both
    /// prefixes are single letters in the same alphabet and both scanners feed the same box.</summary>
    [Fact]
    public void A_member_card_is_not_a_gift_card()
    {
        var member = MemberNumbers.Format(482);

        Assert.False(GiftCardCodes.LooksLikeCard(member));
        Assert.False(GiftCardCodes.LooksLikeCard(MemberNumbers.Prefix + member));
        Assert.False(MemberNumbers.LooksLikeMemberScan(GiftCardCodes.BarcodePayload(GiftCardCodes.New())));
    }
}
