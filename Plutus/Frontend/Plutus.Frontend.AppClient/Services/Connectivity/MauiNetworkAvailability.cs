using Microsoft.Maui.Networking;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// MAUI's answer to "is there a network?", behind <see cref="INetworkAvailability"/>.
    ///
    /// ⚠ This is the ONLY platform-specific part of the connection check, and it is deliberately
    /// this thin. Everything that decides what a state MEANS — the two-step order, the wording, the
    /// difference between "no server" and "revoked" — lives in
    /// <see cref="Plutus.Client.Core.ConnectivityProbe"/>, shared with every other till. A second
    /// copy of that reasoning per platform is a second copy that drifts.
    ///
    /// ⚠ <see cref="NetworkAccess.ConstrainedInternet"/> counts as HAVING a network. It means a
    /// captive portal — the shop's wifi has the "click here to accept" page in the way. The device
    /// genuinely is connected; the ping will fail and report "no server", which is the honest answer
    /// and points at the right fix. Treating it as no-network would blame the cable.
    /// </summary>
    internal sealed class MauiNetworkAvailability : INetworkAvailability
    {
        public bool HasNetwork =>
            Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess is
                NetworkAccess.Internet or NetworkAccess.ConstrainedInternet;
    }
}
