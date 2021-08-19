using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class RoleRepository : RepositoryBase<Role, string>, IRoleRepository
    {
        public RoleRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
