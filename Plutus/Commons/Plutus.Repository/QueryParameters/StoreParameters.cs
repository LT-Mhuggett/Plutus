using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class StoreParameters : QueryParameters<Store, int>
    {
        public Guid BusinessId { get; set; } = default;

        public override Expression<Func<Store, bool>> GetExpression()
        {
            var expression = base.GetExpression();
            if (BusinessId != default)
            {
                expression = expression.And(s => s.BusinessId == BusinessId);
            }

            return expression;
        }
    }
}
