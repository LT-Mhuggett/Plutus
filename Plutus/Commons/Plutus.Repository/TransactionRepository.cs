using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class TransactionRepository : CompositeRepositoryBase<Transaction, int, Guid>, ITransactionRepository
    {
        public TransactionRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
