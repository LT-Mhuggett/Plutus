using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class AuthActionRepository : RepositoryBase<AuthActions, int>, IAuthActionRepository
    {
        public AuthActionRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {
        }
    }
}
