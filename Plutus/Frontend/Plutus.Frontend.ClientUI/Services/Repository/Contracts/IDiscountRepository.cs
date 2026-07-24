using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IDiscountRepository : IRepositoryBase<Discount, int, DiscountBody, DiscountParameters>
    {
    }
}
