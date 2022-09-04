using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.QueryParameters
{
    public class StockParameters : TriCompositeQueryParameters<Stock, string, Guid, int>
    {
    }
}
