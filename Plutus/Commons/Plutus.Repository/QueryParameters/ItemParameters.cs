using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
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
        /// When true, <see cref="Search"/> is split on whitespace and EVERY word must match
        /// (each independently against name/barcode/brand) — so "batman one" finds
        /// "Batman Year One". Added 2026-07-30, driven by a till device setting; default
        /// false keeps the original whole-phrase behaviour for existing callers.
        /// </summary>
        public bool MatchAllWords { get; set; }

        public override Expression<Func<Item, bool>> GetExpression()
        {
            if (string.IsNullOrWhiteSpace(Search))
                return base.GetExpression();

            var words = MatchAllWords
                ? Search.ToLower().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                : new[] { Search.ToLower() };

            Expression<Func<Item, bool>> expr = i => i.CreatedAt.Date >= MinCreatedDate.Date &&
                                                     i.CreatedAt.Date <= MaxCreatedDate.Date;
            foreach (var word in words)
            {
                var term = word; // per-iteration capture — each And gets its own word
                expr = expr.And(i => i.Name.ToLower().Contains(term) ||
                                     i.IdOne.ToLower().Contains(term) ||
                                     i.Brand.ToLower().Contains(term));
            }
            return expr;
        }
    }
}
