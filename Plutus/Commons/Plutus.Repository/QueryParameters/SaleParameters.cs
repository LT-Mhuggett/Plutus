using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Plutus.Repository.Extensions;

namespace Plutus.Repository.QueryParameters
{
    public class SaleParameters : QueryParameters<Sale, Guid>
    {
        public DateTime MinDateOfSale { get; set; } = DateTime.UnixEpoch;
        public DateTime MaxDateOfSale { get; set; } = DateTime.UnixEpoch;

        public override Expression<Func<Sale, bool>> GetExpression()
        {
            var queryExpression = base.GetExpression();
            if(MinDateOfSale != DateTime.UnixEpoch && MaxDateOfSale != DateTime.UnixEpoch)
            {
                queryExpression = queryExpression.And(s => s.DateOfSale.Date >= MinDateOfSale.Date && s.DateOfSale.Date <= MaxDateOfSale.Date);
            }

            return queryExpression;
        }
    }
}
