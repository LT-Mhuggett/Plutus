using Plutus.Entities.Models;

namespace Plutus.Contracts
{
    public interface IStockRepository : ITriCompositeRepositoryBase<Stock, string, string, string>
    {
    }
}
