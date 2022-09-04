using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface ITransactionRepository : ICompositeRepositoryBase<Transaction, int, Guid>
    {
    }
}
