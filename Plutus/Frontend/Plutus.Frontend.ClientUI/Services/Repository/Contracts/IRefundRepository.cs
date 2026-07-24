using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IRefundRepository : IRepositoryBase<Refund, int, RefundBody, RefundParameters>, Plutus.Contracts.IRefundRepository
    {
    }
}
