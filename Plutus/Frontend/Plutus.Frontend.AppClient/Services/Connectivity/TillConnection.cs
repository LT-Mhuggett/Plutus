using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// The app's one connection check: builds the shared probe, and turns its answer into the two
    /// things a screen needs — a sentence and a colour.
    ///
    /// ⚠ Everything that DECIDES anything lives in <see cref="ConnectivityProbe"/>, shared with
    /// every other till. This class holds no rules, only wiring — which is the point. When the web
    /// till moves off <c>navigator.onLine</c> (WP16a) it gets the same verdicts because it runs the
    /// same probe, not because someone kept two implementations in step.
    /// </summary>
    internal static class TillConnectionCheck
    {
        internal const string DefaultServerUrl = "https://plutus.huggett.dscloud.me";

        // One HttpClient for the life of the app: a new one per check leaks sockets, and on a till
        // that polls this every minute for twelve hours that is a real exhaustion bug.
        private static readonly Lazy<HttpClient> Http = new(() => new HttpClient
        {
            // Above the probe's own 5s budget so the probe's timeout is what fires, not this one —
            // but far below HttpClient's 100-second default, which would look like a frozen app.
            Timeout = TimeSpan.FromSeconds(10),
        });

        /// <summary>Check now. ⚠ Never throws and never blocks longer than the probe's timeout —
        /// a login screen that hangs on a dead network is a till that cannot be signed into during
        /// an outage, which is exactly when the shop most needs to keep selling.</summary>
        public static async Task<ConnectionStatus> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                var url = Preferences.Get(nameof(ViewModels.Settings.ServerUrlSetting), DefaultServerUrl);
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
                    return Failed("No Plutus server address is set for this till.",
                        "Set the server URL in Settings.");

                var http = Http.Value;
                if (http.BaseAddress != baseUri) http.BaseAddress = baseUri;

                // verifyIdentity:false until WP4 puts a device credential on this app — there is
                // nothing to check yet, and asking would report every till as "not set up".
                return await new ConnectivityProbe(new PlutusApiClient(http), network: new MauiNetworkAvailability())
                    .CheckAsync(verifyIdentity: false, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // A base address that changes after the first request throws on assignment rather
                // than on the call. Still an answer, not a crash.
                return Failed("Can't reach Plutus.", $"{e.GetType().Name}: {e.Message}");
            }

            static ConnectionStatus Failed(string summary, string detail) =>
                new(TillConnection.NoServer, summary, detail, DateTime.UtcNow, TimeSpan.Zero, null);
        }

        /// <summary>Green online · amber reachable but not usable · red nothing there. Kept here
        /// rather than in the shared probe because a colour is a UI decision, and XAML brushes are
        /// not a concept <c>Plutus.Client.Core</c> is allowed to know about.</summary>
        public static Color ColourFor(ConnectionStatus status) => status.State switch
        {
            TillConnection.Online => Color.FromArgb("#1B873F"),
            // Amber, not red: the server is right there. The fix is in the portal, not the router.
            TillConnection.NotEnrolled or TillConnection.Rejected => Color.FromArgb("#B26A00"),
            _ => Color.FromArgb("#C1272D"),
        };
    }
}
