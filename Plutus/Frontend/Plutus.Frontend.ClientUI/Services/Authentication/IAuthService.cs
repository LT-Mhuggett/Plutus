using Microsoft.Identity.Client;

namespace Plutus.Frontend.ClientUI.Services.Authentication
{
    public interface IAuthService
    {
        Task<(AuthenticationResult authResult, IAccount account)> LoginAsync(CancellationToken cancellationToken);
        Task LogoutAsync(string userOid);
    }
}
