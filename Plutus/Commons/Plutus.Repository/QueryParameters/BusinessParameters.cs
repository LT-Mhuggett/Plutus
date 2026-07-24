using NSwag.Annotations;
using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.QueryParameters
{
    public class BusinessParameters : QueryParameters<Business, Guid>
    {
        public Guid EmployeeObjectId { get; set; } = default;
        public bool WithRoles { get; set; } = false;
    }
}
