using System;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// The signed-in operator's platform token, for as long as the app is running
    /// (cutover step 19, binding default 11).
    ///
    /// ⚠ IN MEMORY ONLY, AND THAT IS THE DESIGN. This is a bearer token with no server-side
    /// denylist — revoking a user does not invalidate one already issued — so a copy written to
    /// disk would outlive every revocation the platform is capable of, and survive a reboot into
    /// the next shift. The offline story is the synced roster and its PBKDF2 hashes, which CAN be
    /// withdrawn on the next sync; a cached token cannot.
    ///
    /// ⚠ It is the OPERATOR's token, never the till's. The device credential authenticates the
    /// machine and is what sales are posted with; this authenticates the person, and the two answer
    /// different questions.
    /// </summary>
    internal static class OperatorSession
    {
        private static OperatorSessionDto _session;
        private static readonly object Lock = new();

        internal static void Set(OperatorSessionDto session)
        {
            lock (Lock) _session = session;
        }

        internal static void Clear()
        {
            lock (Lock) _session = null;
        }

        /// <summary>The live token, or null when there isn't one — including when it has expired.
        /// ⚠ Expiry is checked on READ rather than by a timer: a token that lapsed while the app sat
        /// idle overnight must not be handed to the next call simply because nothing woke up.</summary>
        internal static string Token
        {
            get
            {
                lock (Lock)
                {
                    if (_session is null) return null;
                    if (_session.ExpiresAt <= DateTime.UtcNow) { _session = null; return null; }
                    return _session.Token;
                }
            }
        }

        internal static Guid? EmployeeId
        {
            get { lock (Lock) return _session?.EmployeeId; }
        }
    }
}
