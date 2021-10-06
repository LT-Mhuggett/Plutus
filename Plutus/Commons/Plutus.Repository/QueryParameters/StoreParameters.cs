using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class StoreParameters : QueryParameters<Store, string>
    {
        public string BusinessId { get; set; } = default;

        public override Expression<Func<Store, bool>> GetExpression()
        {
            var expression = base.GetExpression();
            if (!String.IsNullOrEmpty(BusinessId))
            {
                expression = expression.And(s => s.BusinessId == BusinessId);
            }

            return expression;
        }
    }
}
