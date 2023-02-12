using Plutus.Entities.Models;
using System;
using System.Linq.Expressions;

namespace Plutus.Repository.QueryParameters
{
    public class TillParameters : QueryParameters<Till, Guid>
    {
        public bool IncludeStore { get; set; } = false;
        public bool IncludeBusiness { get; set; } = false;
    }
}
