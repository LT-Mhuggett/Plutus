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

        /// <summary>Check now. ⚠ Never throws and never blocks longer than the probe's timeout —
        /// a login screen that hangs on a dead network is a till that cannot be signed into during
        /// an outage, which is exactly when the shop most needs to keep selling.</summary>
        public static async Task<ConnectionStatus> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                var url = Preferences.Get(nameof(ViewModels.Settings.ServerUrlSetting), DefaultServerUrl);

                // ⚠ Never mutate BaseAddress — see PlutusHttp. This used to, and it is what broke
                // enrolment: the first check sent a request, and every call after it threw.
                var http = PlutusHttp.TryFor(url);
                if (http is null)
                    return Failed("No Plutus server address is set for this till.",
                        "Set the server address on the Plutus tab.");

                // verifyIdentity:false because the login screen only needs to know whether the
                // PLATFORM is reachable. Whether this device is enrolled belongs on the Plutus tab,
                // where there is something the operator can do about it.
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

        /// <summary>
        /// The colour of the CONNECTION, and only the connection.
        ///
        /// ⚠ <see cref="TillConnection.NotEnrolled"/> is GREEN. It used to be amber, which put an
        /// amber dot next to the words "Connected to Plutus" — a panel headed *Connection*
        /// contradicting itself. The connection genuinely is fine; not being enrolled is a
        /// different fact, with its own section and its own next step. Colouring one indicator by
        /// two unrelated states is how a screen stops being trusted.
        ///
        /// Amber is reserved for the case that IS a connection fault with a non-network fix:
        /// reachable, and this till refused.
        /// </summary>
        public static Color ColourFor(ConnectionStatus status) => status.State switch
        {
            TillConnection.Online or TillConnection.NotEnrolled => Color.FromArgb("#1B873F"),
            // Amber: reachable, and cannot serve this till. A deploy fixes it — not a cable.
            TillConnection.ServerTooOld => Color.FromArgb("#B26A00"),
            // Amber, not red: the server is right there. The fix is in the portal, not the router.
            TillConnection.Rejected => Color.FromArgb("#B26A00"),
            _ => Color.FromArgb("#C1272D"),
        };
    }
}
