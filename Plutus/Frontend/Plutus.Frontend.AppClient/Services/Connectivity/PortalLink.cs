using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// Where this till's management portal lives.
    ///
    /// ⚠⚠ C2 TWIN of the web till's `portalUrl()` in `sibling.ts`, and derived the same way: the
    /// portal is the API host with an `admin.` prefix. The web till reads `window.location`; this till
    /// has no location, so it reads the server URL it was configured with — which is the same host.
    ///
    /// ⚠ IT NEVER GUESSES FROM AN IP OR `localhost`. `sibling.ts` refuses both, because
    /// `admin.192.168.1.20` resolves to nothing and `admin.localhost` to nothing useful — a dead link
    /// is worse than an absent button, since the operator concludes the portal is broken rather than
    /// that this till was never told where it is.
    /// </summary>
    public static class PortalLink
    {
        /// <summary>
        /// The portal's base URL, or null when it cannot be worked out.
        ///
        /// ⚠ NEVER THROWS. It is called from an `async void` handler on the app bar.
        /// </summary>
        public static async Task<string?> ResolveAsync(CancellationToken ct = default)
        {
            try
            {
                var configured = new ViewModels.Settings().ServerUrlSetting;
                if (string.IsNullOrWhiteSpace(configured)) return null;

                if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var api)) return null;

                var host = api.Host;
                if (string.IsNullOrWhiteSpace(host)) return null;

                // ⚠ An IP or a loopback name cannot carry a subdomain — see the class remarks.
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || System.Net.IPAddress.TryParse(host, out _))
                    return null;

                // ⚠ Already the portal? Then it IS the portal — don't build `admin.admin.…`.
                var portalHost = host.StartsWith("admin.", StringComparison.OrdinalIgnoreCase)
                    ? host
                    : $"admin.{host}";

                var builder = new UriBuilder(api.Scheme, portalHost)
                {
                    Port = api.IsDefaultPort ? -1 : api.Port,
                };

                await Task.CompletedTask.ConfigureAwait(false);
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("PortalLink.Resolve", ex);
                return null;
            }
        }
    }
}
