using System;
using System.Collections.Generic;

namespace Plutus.SharedKernel;

/// <summary>
/// How a till turns what someone typed into the scan bar into match tokens.
///
/// ⚠ WHY THIS IS IN SHAREDKERNEL. The same rule existed in three places: the server's
/// <c>ItemParameters.Tokenise</c>, the web till's <c>offline.ts searchTokens</c>, and — once MAUI
/// searched a cached catalogue — it would have needed a fourth. Both existing copies carry a
/// "keep in sync" comment, which is a comment admitting the problem rather than fixing it.
///
/// The failure it produces is quiet and horrible: two tills in the same shop return DIFFERENT
/// results for the same query. Someone searches "batman one", finds nothing on one counter and the
/// item on the next, and concludes the stock is wrong.
///
/// ⚠ The two modes are a per-device PREFERENCE, not a platform decision. Word mode is what makes
/// "batman one" find *Batman Year One*; phrase mode (the server default) matches the whole string.
/// A till in word mode and a till in phrase mode are *supposed* to differ — that is a setting. What
/// must not differ is two tills with the SAME setting.
/// </summary>
public static class ItemSearch
{
    /// <summary>
    /// Lower-cased match tokens.
    ///
    /// <b>Word mode</b> — quoted segments are literal phrases (⚠ an unclosed quote runs to
    /// end-of-input, deliberately: someone half-way through typing <c>"year one</c> should get
    /// sensible results rather than none). Everything outside quotes splits on whitespace.
    ///
    /// <b>Phrase mode</b> — the whole input, quotes stripped, as one token.
    /// </summary>
    public static IEnumerable<string> Tokenise(string? search, bool matchAllWords)
    {
        if (string.IsNullOrWhiteSpace(search)) yield break;
        var lower = search.ToLower();

        if (!matchAllWords)
        {
            var phrase = lower.Replace("\"", "").Trim();
            if (phrase.Length > 0) yield return phrase;
            yield break;
        }

        var parts = lower.Split('"'); // odd indexes = inside quotes
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                var phrase = parts[i].Trim();
                if (phrase.Length > 0) yield return phrase;
            }
            else
            {
                foreach (var word in parts[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    yield return word;
            }
        }
    }

    /// <summary>
    /// Does one item match? ⚠ EVERY token must hit — that is what makes word mode a narrowing
    /// search rather than a widening one, and it is why "batman one" finds *Batman Year One* and
    /// not every item containing "one".
    ///
    /// The three searched fields are name, barcode and brand, in that order and no others: adding a
    /// fourth here without adding it to the server changes what a till finds and nothing would say so.
    /// </summary>
    public static bool Matches(IEnumerable<string> tokens, string? name, string? idOne, string? brand)
    {
        var n = name?.ToLower() ?? string.Empty;
        var i = idOne?.ToLower() ?? string.Empty;
        var b = brand?.ToLower() ?? string.Empty;

        foreach (var token in tokens)
            if (!n.Contains(token) && !i.Contains(token) && !b.Contains(token))
                return false;
        return true;
    }

    /// <summary>Tokenise and match in one go — the shape a client calls per item.</summary>
    public static bool Matches(string? search, bool matchAllWords, string? name, string? idOne, string? brand)
    {
        var tokens = new List<string>(Tokenise(search, matchAllWords));
        return tokens.Count != 0 && Matches(tokens, name, idOne, brand);
    }
}
