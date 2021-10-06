using Plutus.Entities.Models;
using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Plutus.Repository.QueryParameters
{
    public class BusinessParameters : QueryParameters<Business, string>
    {
        public bool LoadStores { get; set; } = false;

        public override Expression<Func<Business, bool>> GetExpression()
        {
            var expression = base.GetExpression();
            if (LoadStores)
            {
                //expression = expression
            }
            return expression;
        }
    }
}
