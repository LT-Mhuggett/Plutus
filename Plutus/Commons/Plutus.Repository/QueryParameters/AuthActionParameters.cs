using NSwag.Annotations;
using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.QueryParameters
{
    public class AuthActionsParameters : QueryParameters<AuthActions, int>
    {
        public string EmployeeObjectId { get; set; } = default;
        public string BusinessId { get; set; } = default;

        [OpenApiIgnore]
        public bool ValidEmployeeObjectId => Guid.TryParse(EmployeeObjectId, out _);
    }
}
