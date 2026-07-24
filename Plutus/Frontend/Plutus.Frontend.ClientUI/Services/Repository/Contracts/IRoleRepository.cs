using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IRoleRepository : IRepositoryBase<Role, int, RoleBody, RoleParameters>, Plutus.Contracts.IRoleRepository
    {
    }
}
