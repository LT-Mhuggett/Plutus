using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class SaleRepository : RepositoryBase<Sale, Guid>, ISaleRepository
    {
        public SaleRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
