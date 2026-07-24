using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using System;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class TaxParameters : CompositeQueryParameters<Tax, int, Guid>
    {
    }
}
