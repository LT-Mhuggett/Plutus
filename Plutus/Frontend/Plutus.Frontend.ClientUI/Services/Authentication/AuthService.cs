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
            var builder = PublicClientApplicationBuilder.Create(azureAdB2CSettings.ClientId)
                                   .WithB2CAuthority($"{azureAdB2CSettings.Instance}/tfp/{azureAdB2CSettings.Domain}/{azureAdB2CSettings.SignUpSignInPolicyId}");
#if WINDOWS
            // Windows desktop uses the system-browser flow, which only supports a loopback redirect.
            // NOTE: "http://localhost" must also be registered as a redirect URI on the Azure AD B2C
            // app registration (as a Mobile & desktop / public-client redirect) for this to complete.
            builder = builder.WithRedirectUri("http://localhost");
#else
            builder = builder.WithRedirectUri($"msal{azureAdB2CSettings.ClientId}://auth");
#endif
            authenticationClient = builder.Build();
        }

        public async Task<(AuthenticationResult authResult, IAccount account)> LoginAsync(CancellationToken cancellationToken)
        {
            var azureAdB2CSettings = Configuration.GetRequiredSection("AppSettings").Get<AppSettings>().AzureAdB2CSettings;
            AuthenticationResult result;
            try
            {
                try
                {
                    // Reuse a cached account silently if one is available (no browser).
                    var accounts = await authenticationClient.GetAccountsAsync();
                    result = await authenticationClient
                             .AcquireTokenSilent(azureAdB2CSettings.Scopes, accounts.FirstOrDefault())
                             .ExecuteAsync(cancellationToken);
                }
                catch (MsalUiRequiredException)
                {
                    // No usable cached token -> show the interactive sign-in UI.
                    // NOTE: previously hard-coded Prompt.NoPrompt, which suppresses the sign-in
                    // form and only works when a B2C session already exists (why login "did
                    // nothing" / the browser opened but couldn't be completed on a fresh session).
                    result = await authenticationClient
                             .AcquireTokenInteractive(azureAdB2CSettings.Scopes)
                             .WithPrompt(Prompt.SelectAccount)
#if ANDROID
                             .WithParentActivityOrWindow(Platform.CurrentActivity)
#elif WINDOWS
                             .WithParentActivityOrWindow(GetWindowHandle())
#endif
                             .ExecuteAsync(cancellationToken);
                }

                return (result, await authenticationClient.GetAccountAsync(result.UniqueId));
            }
            catch (MsalClientException ex)
            {
                // User cancellation etc. Logged (not swallowed silently) so genuine
                // config failures are diagnosable; still returns default so the UI stays put.
                System.Diagnostics.Debug.WriteLine($"[AuthService] MsalClientException: {ex.ErrorCode} - {ex.Message}");
                return default;
            }
        }

#if WINDOWS
        // .NET MAUI Windows requires an explicit parent window handle for MSAL interactive auth.
        private static IntPtr GetWindowHandle()
        {
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
            var nativeWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            return nativeWindow is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        }
#endif

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
