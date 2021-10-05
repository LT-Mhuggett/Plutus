using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class StoreParameters : QueryParameters<Store, string>
    {
        public string BussinessId { get; set; } = default;

        public override Expression<Func<Store, bool>> GetExpression()
        {
            var expression = base.GetExpression();
            if (!String.IsNullOrEmpty(BussinessId))
            {
                expression = expression.And(s => s.BussinessId == BussinessId);
            }

            return expression;
        }
    }
}
