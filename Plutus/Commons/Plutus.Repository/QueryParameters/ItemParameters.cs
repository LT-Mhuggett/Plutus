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

        /// <summary>
        /// FE5.4 the Bin. Default (false) hides binned items EVERYWHERE this filter is used — till
        /// scan/search, both inventory lists, the webstore feed — which is the whole point of a bin
        /// that isn't a delete. Set true to see the Bin itself (gated on inventory.bulk).
        /// </summary>
        public bool Binned { get; set; }

        public override Expression<Func<Item, bool>> GetExpression()
        {
            // NB: unlike Search/CatId, the bin filter must apply even with no other criteria —
            // "no filters" must still mean "no binned items".
            Expression<Func<Item, bool>> expr = i => i.CreatedAt.Date >= MinCreatedDate.Date &&
                                                     i.CreatedAt.Date <= MaxCreatedDate.Date;
            expr = Binned
                ? expr.And(i => i.BinnedAtUtc != null)
                : expr.And(i => i.BinnedAtUtc == null);

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

        /// <summary>
        /// ⚠ MOVED to <see cref="Plutus.SharedKernel.ItemSearch.Tokenise"/> (2026-08-08).
        ///
        /// It used to live here with a "mirrored client-side in offline.ts searchTokens — keep in
        /// sync" comment, which is a note admitting the problem rather than fixing it. Once MAUI
        /// searched a cached catalogue there would have been a third copy, and the failure they
        /// produce is quiet: two tills in one shop returning different results for the same query,
        /// which everybody reads as the stock being wrong.
        /// </summary>
        private static IEnumerable<string> Tokenise(string search, bool matchAllWords) =>
            Plutus.SharedKernel.ItemSearch.Tokenise(search, matchAllWords);
    }
}
