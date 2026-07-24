using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ITransactionRepository : ICompositeRepositoryBase<Transaction, int, Guid, TransactionBody, TransactionParameters>, Plutus.Contracts.ITransactionRepository
    {
    }
}
