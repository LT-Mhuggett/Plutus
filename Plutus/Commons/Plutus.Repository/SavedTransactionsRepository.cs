using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class SavedTransactionsRepository : RepositoryBase<SavedTransaction, string>, ISavedTransactionRepository
    {
        public SavedTransactionsRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
