using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface ISavedTransactionRepository : IRepositoryBase<SavedTransaction, Guid>
    {
    }
}
