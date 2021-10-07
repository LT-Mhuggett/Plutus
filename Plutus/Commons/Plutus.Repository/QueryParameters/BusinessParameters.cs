using NSwag.Annotations;
using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.QueryParameters
{
    public class BusinessParameters : QueryParameters<Business, string>
    {
        public string EmployeeObjectId { get; set; }


        [OpenApiIgnore]
        public bool ValidEmployeeObjectId => Guid.TryParse(EmployeeObjectId, out _);
    }
}
