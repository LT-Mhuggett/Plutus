using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class BusinessRepository : RepositoryBase<Business, Guid>, IBusinessRepository
    {
        public BusinessRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {
        }
    }
}
