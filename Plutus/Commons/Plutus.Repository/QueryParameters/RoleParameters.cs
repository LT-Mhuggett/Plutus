using Plutus.Entities.Models;

namespace Plutus.Repository.QueryParameters
{
    public class RoleParameters : QueryParameters<Role, int>
    {
        public string BusinessId { get; set; } = default;
    }
}
