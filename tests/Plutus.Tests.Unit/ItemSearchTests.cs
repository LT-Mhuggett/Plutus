using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Item search tokenising — the rule that decides what a scan bar finds.
///
/// ⚠ It existed in THREE places before this: the server's <c>ItemParameters.Tokenise</c>, the web
/// till's <c>offline.ts searchTokens</c>, and (about to be) MAUI's offline catalogue search. Both
/// existing copies carried a "keep in sync" comment, which admits the problem rather than fixing
/// it. The failure they produce is quiet and awful: **two tills in the same shop return different
/// results for the same query**, and everyone reads that as the stock being wrong.
///
/// These vectors are the contract. If the TypeScript ever changes, they are what should fail —
/// see till-design C2 for the honest note that nothing executes the TypeScript half yet.
/// </summary>
public class ItemSearchTests
{
    private static string[] Words(string term) => ItemSearch.Tokenise(term, matchAllWords: true).ToArray();
    private static string[] Phrase(string term) => ItemSearch.Tokenise(term, matchAllWords: false).ToArray();

    // ── phrase mode (the server default) ──

    [Fact]
    public void Phrase_mode_is_the_whole_input_as_one_token()
    {
        Assert.Equal(new[] { "batman year one" }, Phrase("Batman Year One"));
    }

    [Fact]
    public void Phrase_mode_strips_quotes_rather_than_honouring_them()
    {
        // Quotes mean "literal phrase" — which is what phrase mode already does, so they are noise.
        Assert.Equal(new[] { "batman year one" }, Phrase("\"Batman Year One\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void Nothing_typed_produces_no_tokens(string? term)
    {
        // ⚠ Zero tokens must not mean "everything matches". Matches() requires at least one token
        // for exactly this reason — see the last test.
        Assert.Empty(Phrase(term));
        Assert.Empty(Words(term));
    }

    // ── word mode (the device preference that makes search useful) ──

    [Fact]
    public void Word_mode_splits_on_whitespace()
    {
        Assert.Equal(new[] { "batman", "one" }, Words("batman one"));
    }

    [Fact]
    public void THE_headline_case_batman_one_finds_Batman_Year_One()
    {
        // The example in the handover, and the whole reason word mode exists.
        Assert.True(ItemSearch.Matches("batman one", matchAllWords: true,
            name: "Batman Year One", idOne: "9781401207526", brand: "DC"));

        // ⚠ …and phrase mode does NOT, which is correct: the two modes are a device preference and
        // are SUPPOSED to differ. What must never differ is two tills with the same setting.
        Assert.False(ItemSearch.Matches("batman one", matchAllWords: false,
            name: "Batman Year One", idOne: "9781401207526", brand: "DC"));
    }

    [Fact]
    public void Word_mode_NARROWS_every_token_must_hit()
    {
        // If tokens were OR'd, "batman one" would return every item containing "one" — which is
        // most of a comic shop.
        Assert.False(ItemSearch.Matches("batman one", matchAllWords: true,
            name: "Superman Year One", idOne: "1", brand: "DC"));
    }

    [Fact]
    public void A_quoted_segment_is_one_literal_phrase()
    {
        Assert.Equal(new[] { "year one", "batman" }, Words("\"year one\" batman"));
    }

    [Fact]
    public void An_UNCLOSED_quote_runs_to_the_end_rather_than_returning_nothing()
    {
        // ⚠ Deliberate. Someone half-way through typing `"year one` should get sensible results,
        // not an empty list — search runs on every keystroke.
        Assert.Equal(new[] { "batman", "year one" }, Words("batman \"year one"));
    }

    [Fact]
    public void Case_and_extra_whitespace_do_not_matter()
    {
        Assert.Equal(new[] { "batman", "one" }, Words("  BATMAN   One  "));
    }

    // ── what is searched ──

    [Fact]
    public void Name_barcode_and_brand_are_all_searched()
    {
        Assert.True(ItemSearch.Matches("9781", true, "Batman", "9781401207526", "DC"));
        Assert.True(ItemSearch.Matches("dc", true, "Batman", "9781401207526", "DC"));
        // ⚠ Three fields, no more. Adding a fourth here without adding it to the server would change
        // what a till finds, and nothing anywhere would say so.
        Assert.False(ItemSearch.Matches("hardback", true, "Batman", "9781401207526", "DC"));
    }

    [Fact]
    public void Null_fields_are_survivable()
    {
        // Legacy rows have nulls. A search that throws on one is a scan bar that dies mid-shift.
        Assert.False(ItemSearch.Matches("batman", true, null, null, null));
        Assert.True(ItemSearch.Matches("batman", true, "Batman", null, null));
    }

    [Fact]
    public void An_EMPTY_search_matches_NOTHING_rather_than_everything()
    {
        // ⚠ The dangerous default. With zero tokens, "every token matches" is vacuously true — so
        // an empty box would return the entire catalogue. Matches() requires at least one token.
        Assert.False(ItemSearch.Matches("", true, "Batman", "9781401207526", "DC"));
        Assert.False(ItemSearch.Matches("   ", false, "Batman", "9781401207526", "DC"));
    }
}
