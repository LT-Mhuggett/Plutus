using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// Presents the SIGNED-IN OPERATOR's platform token instead of the till's device token.
    ///
    /// ⚠ WHY THIS HAD TO EXIST. `OperatorSession` has held a perfectly good operator token since
    /// cutover step 19 — and it had exactly ONE reference in the whole solution, the `Set(...)` that
    /// stores it. Nothing ever read it. `PlutusApiClient.AuthoriseAsync` attaches whatever
    /// `IDeviceTokenProvider` it was built with, and every client in the app was built with the
    /// DEVICE provider, so the operator's identity was minted at sign-in and thrown away.
    ///
    /// ⚠ THE CONSEQUENCE IS INVISIBLE AND SPECIFIC. `perm:*` policies resolve from RBAC **by the
    /// token's userId**; a device token has no userId, so every operator-permission endpoint answers
    /// 403 to this app. `GET /api/v1/sales/{saleId}` — gated
    /// `portal.financials.view,pos.reports.view,pos.refund` — is one of them, and
    /// `ReturnLookup.TryServerAsync` swallows the failure by design and falls back to this till's own
    /// record. So refunding goods bought at ANOTHER branch has silently answered "we have no record
    /// of that sale" since the day it shipped, on a platform holding the sale all along.
    ///
    /// ⚠ THE DEVICE TOKEN IS STILL RIGHT FOR MOST THINGS, and this must not replace it. Sales
    /// ingest, the heartbeat, the catalogue feed and enrolment are the TILL speaking as itself —
    /// they have to keep working overnight with nobody signed in, and the server derives the till id
    /// from that token rather than trusting a body. Only calls that ask "may this PERSON do this"
    /// belong here.
    ///
    /// ⚠ NULL IS AN ANSWER: nobody is signed in, or the session lapsed. `OperatorSession.Token`
    /// checks expiry on read, so a token that died while the app sat idle overnight is not handed
    /// out simply because nothing woke up. Callers must treat null as "ask the local record
    /// instead", never as an error.
    /// </summary>
    internal sealed class OperatorTokenProvider : IDeviceTokenProvider
    {
        internal static readonly OperatorTokenProvider Instance = new();

        private OperatorTokenProvider() { }

        public Task<string> GetAccessTokenAsync(CancellationToken ct = default) =>
            Task.FromResult(OperatorSession.Token);

        /// <summary>
        /// ⚠ A NO-OP, and deliberately so. `Invalidate` exists for the DEVICE provider, which can
        /// re-mint from the stored client secret after a 401. There is no equivalent here: an
        /// operator token comes from someone typing a password, and this app cannot and must not
        /// produce another one on its own. Dropping the session instead would silently sign the
        /// operator out mid-action because one call happened to 401.
        ///
        /// The session lapses on its own — `OperatorSession.Token` checks expiry on every read — and
        /// the honest response to a 401 here is the caller's fallback path, not a re-authentication
        /// nobody asked for.
        /// </summary>
        public void Invalidate() { }
    }
}
