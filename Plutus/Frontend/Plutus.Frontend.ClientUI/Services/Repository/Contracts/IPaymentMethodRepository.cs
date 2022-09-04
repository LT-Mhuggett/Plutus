using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IPaymentMethodRepository : IRepositoryBase<PaymentMethod, int, PaymentMethodBody, PaymentMethodsParameters>, Plutus.Contracts.IPaymentMethodRepository
    {
        Task<IQueryable<PaymentMethod>> GetAllQueryable();
    }
}
