using System.Collections.Generic;
using System.Linq.Expressions;

namespace Plutus.Repository.Extensions
{
    public class ParameterRebinder : ExpressionVisitor
    {
        private readonly Dictionary<ParameterExpression, ParameterExpression> Map;

        public ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression> map)
        {
            Map = map ?? new Dictionary<ParameterExpression, ParameterExpression>();
        }

        public static Expression ReplaceParameters(Dictionary<ParameterExpression, ParameterExpression> map, Expression exp) => new ParameterRebinder(map).Visit(exp);

        protected override Expression VisitParameter(ParameterExpression p)
        {
            if (Map.TryGetValue(p, out var replacement))
                p = replacement;

            return base.VisitParameter(p);
        }
    }
}
