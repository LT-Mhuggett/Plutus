using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class TillRepository : RepositoryBase<Till, Guid>, ITillRepository
    {
        public TillRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
