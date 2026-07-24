using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ICheckoutItemChangeRepository : IRepositoryBase<CheckoutItemChange, int, CheckoutItemChangeBody, CheckoutItemChangeParameters>, Plutus.Contracts.ICheckoutItemChangeRepository
    {
    }
}
