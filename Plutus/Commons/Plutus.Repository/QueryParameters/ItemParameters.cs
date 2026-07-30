using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class ItemParameters : CompositeQueryParameters<Item, string, Guid>
    {
        /// <summary>
        /// Optional case-insensitive search across item name, barcode/id and brand.
        /// Added 2026-07-23 for the webapp till/inventory search (additive — when absent
        /// the previous unfiltered behaviour is unchanged).
        /// </summary>
        public string Search { get; set; }

        /// <summary>
        /// When true, <see cref="Search"/> is split into tokens and EVERY token must match
        /// (each independently against name/barcode/brand) — so "batman one" finds
        /// "Batman Year One". FE8.1: a "quoted segment" is one token matched as the literal
        /// phrase, so `"year one" batman` = phrase *year one* AND word *batman*. Added
        /// 2026-07-30, driven by a till device setting; default false keeps the original
        /// whole-phrase behaviour for existing callers (quotes are then just stripped).
        /// </summary>
        public bool MatchAllWords { get; set; }

        /// <summary>FE5.0: optional category filter. The portal/till category dropdowns used
        /// to filter client-side within the fetched page (usually showing nothing) — this
        /// moves it server-side. Composes with <see cref="Search"/>.</summary>
        public Guid? CatId { get; set; }

        public override Expression<Func<Item, bool>> GetExpression()
        {
            if (string.IsNullOrWhiteSpace(Search) && CatId == null)
                return base.GetExpression();

            Expression<Func<Item, bool>> expr = i => i.CreatedAt.Date >= MinCreatedDate.Date &&
                                                     i.CreatedAt.Date <= MaxCreatedDate.Date;
            if (CatId is Guid cat)
                expr = expr.And(i => i.CatId == cat);

            foreach (var token in Tokenise(Search, MatchAllWords))
            {
                var term = token; // per-iteration capture — each And gets its own token
                expr = expr.And(i => i.Name.ToLower().Contains(term) ||
                                     i.IdOne.ToLower().Contains(term) ||
                                     i.Brand.ToLower().Contains(term));
            }
            return expr;
        }

        /// <summary>Lower-cased match tokens. Word mode: quoted segments are literal-phrase
        /// tokens (an unclosed quote runs to end-of-input), the rest splits on whitespace.
        /// Phrase mode: the whole input (quotes stripped) is one token. Mirrored client-side
        /// in the till's offline search (offline.ts searchTokens) — keep in sync.</summary>
        private static IEnumerable<string> Tokenise(string search, bool matchAllWords)
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
                    foreach (var word in parts[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                        yield return word;
                }
            }
        }
    }
}
