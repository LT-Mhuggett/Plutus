using Plutus.Entities.Models;
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

        public override Expression<Func<Item, bool>> GetExpression()
        {
            if (string.IsNullOrWhiteSpace(Search))
                return base.GetExpression();

            var term = Search.ToLower();
            return i => i.CreatedAt.Date >= MinCreatedDate.Date &&
                        i.CreatedAt.Date <= MaxCreatedDate.Date &&
                        (i.Name.ToLower().Contains(term) ||
                         i.IdOne.ToLower().Contains(term) ||
                         i.Brand.ToLower().Contains(term));
        }
    }
}
