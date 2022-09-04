using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IAuthActionRepository : IRepositoryBase<AuthActions, int, AuthActionBody, AuthActionsParameters>, Plutus.Contracts.IAuthActionRepository
    {
    }
}
