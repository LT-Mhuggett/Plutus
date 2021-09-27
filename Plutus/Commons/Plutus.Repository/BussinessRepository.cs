using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class BussinessRepository : RepositoryBase<Bussiness, string>, IBussinessRepository
    {
        public BussinessRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {
        }
    }
}
