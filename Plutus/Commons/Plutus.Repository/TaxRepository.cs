using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class TaxRepository : CompositeRepositoryBase<Tax, int, Guid>, ITaxRepository
    {
        public TaxRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
