using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class TillRepository : RepositoryBase<Till, string>, ITillRepository
    {
        public TillRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
