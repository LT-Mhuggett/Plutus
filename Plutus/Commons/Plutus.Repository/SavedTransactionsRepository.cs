using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class SavedTransactionsRepository : RepositoryBase<SavedTransaction, Guid>, ISavedTransactionRepository
    {
        public SavedTransactionsRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
