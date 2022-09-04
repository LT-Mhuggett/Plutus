using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using Plutus.Frontend.ClientUI.Core.AppSettings;

namespace Plutus.Frontend.ClientUI.Services.Authentication
{
    public class AuthService : IAuthService
    {
        private readonly IPublicClientApplication authenticationClient;
        IConfiguration Configuration { get; set; }

        public AuthService(IConfiguration configuration)
        {
            Configuration = configuration;
            var azureAdB2CSettings = Configuration.GetRequiredSection("AppSettings:AzureAdB2CSettings").Get<AzureAdB2CSettings>();
            authenticationClient = PublicClientApplicationBuilder.Create(azureAdB2CSettings.ClientId)
                                   .WithB2CAuthority($"{azureAdB2CSettings.Instance}/tfp/{azureAdB2CSettings.Domain}/{azureAdB2CSettings.SignUpSignInPolicyId}")
                                   .WithRedirectUri($"msal{azureAdB2CSettings.ClientId}://auth")
                                   .Build();
        }

        public async Task<(AuthenticationResult authResult, IAccount account)> LoginAsync(CancellationToken cancellationToken)
        {
            var azureAdB2CSettings = Configuration.GetRequiredSection("AppSettings").Get<AppSettings>().AzureAdB2CSettings;
            AuthenticationResult result;
            try
            {
                result = await authenticationClient
                         .AcquireTokenInteractive(azureAdB2CSettings.Scopes)
                         .WithPrompt(Prompt.NoPrompt)
#if ANDROID
                         .WithParentActivityOrWindow(Platform.CurrentActivity)
#endif
                         .ExecuteAsync(cancellationToken);

                return (result, await authenticationClient.GetAccountAsync(result.UniqueId));
            }
            catch (MsalClientException)
            {
                return default;
            }
        }

        public async Task LogoutAsync(string userOid)
        {
            var azureAdB2CSettings = Configuration.GetRequiredSection("AppSettings").Get<AppSettings>().AzureAdB2CSettings;

            var account = (await authenticationClient.GetAccountsAsync()).FirstOrDefault(a => a.HomeAccountId.ObjectId == userOid);
            if(account != default)
            {
                await authenticationClient.RemoveAsync(account).ConfigureAwait(false);
            }
        }
    }
}
