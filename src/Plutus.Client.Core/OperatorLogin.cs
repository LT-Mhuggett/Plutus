using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

// ─────────────────────────────────────────────────────────────────────────────
// WP8 — signing an operator in at a till, with or without a network.
//
// ⚠ ALL OF IT LIVES HERE, not in the MAUI project, because it is a set of RULES: who may sign in,
// how long a cached credential is trusted, and what a stale one may still do. The web till, the
// MAUI till and any future macOS/Linux till have to answer those identically — a second copy is a
// till that lets someone refund when another would not.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Where a till keeps its operator roster. Implemented per platform (a file, a table);
/// the RULES are in this file and are not the implementer's business.</summary>
public interface IOperatorStore
{
    /// <summary>The cached roster, or empty. Never throws — a corrupt cache is "no operators",
    /// which is a state the login screen already handles.</summary>
    Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>Why a sign-in was refused. ⚠ The UI shows different words for each; "wrong password"
/// for all of them is what sent someone hunting for a typo that did not exist.</summary>
public enum LoginFailure
{
    None = 0,
    /// <summary>No roster at all — this till has never synced its staff.</summary>
    NoOperators,
    /// <summary>No operator matches that email or id.</summary>
    UnknownOperator,
    /// <summary>Found them; the password does not match.</summary>
    WrongPassword,
    /// <summary>Found them; they have no web login. Not a typo, an account that was never given one.</summary>
    NoCredential,
    /// <summary>The roster is older than the sell horizon — this till must reconnect.</summary>
    CredentialsTooStale,

    /// <summary>
    /// ⚠⚠ STEP 28 — this account has never signed in on THIS till, and the till cannot reach the
    /// platform to check the password. It is not a wrong password and it is not an unknown account:
    /// it is "connect once, then this works offline for ever".
    ///
    /// ⚠ IT MUST NOT COLLAPSE INTO `WrongPassword`. An operator who typed correctly and is told
    /// they got their password wrong will try three more times and then phone somebody.
    /// </summary>
    NeedsOnlineFirstSignIn,
}

/// <summary>
/// Where this till keeps the device-local verifiers minted by an online sign-in (step 28).
///
/// ⚠ SEPARATE FROM <see cref="IOperatorStore"/> ON PURPOSE. The roster is a CACHE — it is replaced
/// wholesale on every sync, and anything living in it would be destroyed by a routine refresh. A
/// verifier is earned by an online sign-in and must outlive every roster pull.
/// </summary>
public interface IDeviceVerifierStore
{
    /// <summary>This till's verifier for that operator, or null. ⚠ Never throws — a corrupt or
    /// unreadable store is "no verifier", which routes to "connect once".</summary>
    Task<DeviceVerifier.Record?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Which operators this device can verify offline **right now** — step 28's server half.
    ///
    /// ⚠⚠ THIS IS WHAT LETS THE SERVER STOP SHIPPING PASSWORD HASHES WITHOUT A CUTOVER DATE. The
    /// roster carries `CredentialHashBase64` — an operator's PLATFORM password hash, which works on
    /// the web till and the portal — for every member of staff, to every till, for ever. Removing it
    /// is the change that actually empties a stolen till, and the ordering could not be reversed: stop
    /// sending hashes before a till is minting verifiers and every operator who has not signed in
    /// since is locked out, during exactly the outage that made them need the till.
    ///
    /// ⚠ So the till TELLS the server what it already holds, and the server omits only those. No date,
    /// no switch, no estate-wide moment: each (till, operator) pair stops shipping a hash the sync
    /// after that operator first signs in online on that till.
    ///
    /// ⚠⚠ EVERY FAILURE DIRECTION IS SAFE. An empty answer — a corrupt store, a wiped PC, an older
    /// build that does not send it — means the server ships hashes as it always did, and offline
    /// sign-in keeps working. The list can only ever REMOVE the fallback for accounts that provably
    /// no longer need it.
    ///
    /// ⚠ ONLY VERIFIERS THIS BUILD CAN ACTUALLY READ (`DeviceVerifier.CanVerify`). A record stored by
    /// a future algorithm is not a verifier this till can use, and claiming it would surrender the
    /// hash for an account that then cannot sign in offline at all.
    /// </summary>
    Task<IReadOnlyList<Guid>> UsableVerifierUserIdsAsync(CancellationToken ct = default);

    /// <summary>Store a freshly minted verifier. ⚠ Overwrites — a password change online must
    /// replace what this till holds, or the old password keeps working offline for ever.</summary>
    Task SaveAsync(DeviceVerifier.Record record, CancellationToken ct = default);

    /// <summary>Forget one. ⚠ The only lever a till has over a credential it already holds: after
    /// this, that account needs a connection again.</summary>
    Task ForgetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// A signed-in operator, and what they may do.
///
/// ⚠ <see cref="Trust"/> is not decoration. Past the money-out horizon this is
/// <see cref="OfflineTrust.SellOnly"/> and the gate withdraws everything outside the sell floor —
/// so the same person, same password, has fewer powers on a till that has been offline a fortnight.
/// </summary>
public sealed record SignedInOperator(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<PermissionGrant> Grants,
    OfflineTrust Trust,
    TimeSpan RosterAge,
    bool Warn,
    string Message)
{
    /// <summary>
    /// May this operator do <paramref name="code"/> right now, for <paramref name="amountPence"/>?
    ///
    /// ⚠ Evaluates the time window against the TILL's clock at the moment of the action — which is
    /// why the raw windows are shipped rather than a pre-computed answer. And ⚠ it applies the
    /// staleness tier: a permission the operator genuinely holds is still refused when the roster
    /// is too old to be trusted with it.
    /// </summary>
    public bool Can(string code, long? amountPence = null, DateTime? nowLocal = null)
    {
        if (Trust == OfflineTrust.Refused) return false;
        if (Trust == OfflineTrust.SellOnly && !OfflineCredentials.SurvivesStaleness(code)) return false;
        return PermissionResolution.Can(Grants, code, nowLocal ?? DateTime.Now, amountPence);
    }
}

/// <summary>The result of trying to sign in.</summary>
public sealed record LoginResult(SignedInOperator? Operator, LoginFailure Failure, string Message)
{
    public bool Succeeded => Operator != null;

    public static LoginResult Failed(LoginFailure failure, string message) => new(null, failure, message);
}

/// <summary>
/// Verifies an operator against the cached roster, offline.
///
/// ⚠ The password never leaves the machine. Verification is PBKDF2 against the synced hash using
/// <see cref="Pbkdf2"/> — byte-identical to the server's, which is what lets the same credential
/// work on the web till and here.
/// </summary>
public sealed class OperatorLogin
{
    private readonly IOperatorStore _store;
    private readonly OfflineCredentialPolicy _policy;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// ⚠ STEP 28. Null keeps the pre-step-28 behaviour exactly — verify against the platform hash the
    /// roster shipped. That is what every caller that has not been wired up yet still gets, and it is
    /// why this landed without a flag day.
    /// </summary>
    private readonly IDeviceVerifierStore? _verifiers;

    /// <summary>
    /// Proves a password against the PLATFORM, or null when this till cannot ask.
    ///
    /// ⚠⚠ RETURNING null MEANS "COULD NOT ASK", NOT "NO". Offline, a dead server and a timeout must
    /// all route to *connect once*; only an actual answer of `false` is a wrong password. Conflating
    /// them tells somebody with a perfectly good password that it is wrong, every time their
    /// broadband hiccups.
    /// </summary>
    private readonly Func<string, string, CancellationToken, Task<bool?>>? _verifyOnline;

    public OperatorLogin(
        IOperatorStore store,
        OfflineCredentialPolicy? policy = null,
        Func<DateTime>? utcNow = null,
        IDeviceVerifierStore? verifiers = null,
        Func<string, string, CancellationToken, Task<bool?>>? verifyOnline = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? OfflineCredentialPolicy.Default;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _verifiers = verifiers;
        _verifyOnline = verifyOnline;
    }

    /// <param name="emailOrId">What was typed. Matched on email (case-insensitive) or user id, the
    /// same two things the legacy login accepted — staff type whichever they were given.</param>
    public async Task<LoginResult> SignInAsync(string emailOrId, string password, CancellationToken ct = default)
    {
        var roster = await _store.LoadAsync(ct);
        if (roster is null || roster.Operators.Length == 0)
            return LoginResult.Failed(LoginFailure.NoOperators,
                "This till has no staff on it yet. Connect it to Plutus and sync, or ask a manager.");

        var who = (emailOrId ?? string.Empty).Trim();
        var op = roster.Operators.FirstOrDefault(o =>
            string.Equals(o.Email, who, StringComparison.OrdinalIgnoreCase) ||
            o.UserId.ToString().Equals(who, StringComparison.OrdinalIgnoreCase));

        if (op is null)
            return LoginResult.Failed(LoginFailure.UnknownOperator, "No account on this till matches that.");

        // ⚠⚠ "NO SHIPPED HASH" STOPPED MEANING "NO PASSWORD" AT STEP 28. This guard used to refuse
        // outright, and it has to move: once a till holds a device verifier — or can ask the
        // platform — an operator whose hash the roster no longer carries signs in perfectly well.
        // Leaving it here would refuse every sign-in on the day the server stops shipping hashes.
        //
        // ⚠ THE HONEST `NoCredential` CASE SURVIVES, in `ProvePasswordAsync`'s fall-through: a
        // pre-step-28 login (no verifier store, no online check) with no shipped hash is still an
        // account that was never given a web login, and still says so in those words.
        if (_verifiers is null && _verifyOnline is null
            && (string.IsNullOrEmpty(op.CredentialHashBase64) || string.IsNullOrEmpty(op.CredentialSaltBase64)))
            return LoginResult.Failed(LoginFailure.NoCredential,
                $"{op.DisplayName} doesn't have a Plutus password yet — an administrator sets one in the portal.");

        // ── how the password is proved ────────────────────────────────────────────────────────
        //
        // ⚠⚠ STEP 28. Before this, the ONLY check was the platform hash the roster shipped, which
        // is why `OfflineCredentials`' header calls a stolen till "a bag of platform passwords".
        // Now: this till's OWN verifier if it has one, the PLATFORM if it can reach it, and a
        // refusal that says "connect once" if it has neither.
        //
        // ⚠ THE ORDER IS DELIBERATE — LOCAL FIRST. A till with a verifier must not need the network
        // to sign somebody in; that is the whole point of having one, and asking the server first
        // would make every sign-in wait on a timeout during an outage.
        var proved = await ProvePasswordAsync(op, password ?? string.Empty, ct);

        if (proved == PasswordProof.NeedsOnline)
            return LoginResult.Failed(LoginFailure.NeedsOnlineFirstSignIn,
                $"{op.DisplayName} hasn't signed in on this till yet. Connect it to Plutus once and "
                + "sign in — after that it works offline.");

        if (proved == PasswordProof.Corrupt)
            return LoginResult.Failed(LoginFailure.NoCredential,
                "This till's copy of that account is damaged. Sync it again from the Plutus tab.");

        if (proved == PasswordProof.Wrong)
            return LoginResult.Failed(LoginFailure.WrongPassword, "Wrong password.");

        // ⚠ Staleness is judged AFTER the password, deliberately. Telling someone their till is out
        // of date before they have proved who they are leaks the state of the estate to anyone who
        // walks up to it; and the message they need first is the one about their password.
        var assessment = OfflineCredentials.Assess(roster.AsOfUtc, _utcNow(), _policy);
        if (assessment.Trust == OfflineTrust.Refused)
            return LoginResult.Failed(LoginFailure.CredentialsTooStale, assessment.Message);

        var grants = op.Grants
            .Select(g => new PermissionGrant(
                g.Code, g.MaxPence, g.ValidFromUtc, g.ValidToUtc,
                g.DaysOfWeekMask, g.WindowStartLocal, g.WindowEndLocal))
            .ToList();

        return new LoginResult(
            new SignedInOperator(op.UserId, op.DisplayName, grants, assessment.Trust,
                assessment.Age, assessment.ShouldWarn, assessment.Message),
            LoginFailure.None, assessment.Message);
    }

    /// <summary>
    /// A second operator authorises ONE action the signed-in one cannot do.
    ///
    /// ⚠ This is a money path. Three things it must never become:
    /// <list type="bullet">
    /// <item>a way to <b>escalate yourself</b> — the same person authorising their own action is
    /// refused outright, however senior they are;</item>
    /// <item>a way to <b>bypass the ceiling</b> — the supervisor's own limit applies, so a £20
    /// supervisor cannot wave through a £200 refund;</item>
    /// <item>a way to <b>bypass staleness</b> — a roster too old to be trusted with refunds is too
    /// old to authorise one. Otherwise every stale-till restriction has a trivial workaround, which
    /// makes it decoration.</item>
    /// </list>
    ///
    /// ⚠ It grants exactly ONE action. There is no override "mode": the returned <see cref="Override"/>
    /// is a record of a single decision, which is what gets attached to that sale and nothing else.
    /// </summary>
    /// <param name="requestedBy">The operator who hit the wall — recorded so the audit names both.</param>
    public async Task<OverrideResult> AuthoriseOverrideAsync(
        SignedInOperator requestedBy,
        string emailOrId,
        string password,
        string permission,
        long? amountPence = null,
        DateTime? nowLocal = null,
        CancellationToken ct = default)
    {
        if (requestedBy is null) throw new ArgumentNullException(nameof(requestedBy));

        var signIn = await SignInAsync(emailOrId, password, ct);
        if (!signIn.Succeeded)
            return new OverrideResult(null, OverrideFailure.NotAuthenticated, signIn.Message);

        var authoriser = signIn.Operator!;

        // ⚠ Self-authorisation is not an override. Checked BEFORE the permission test so the
        // message says the real reason rather than "you don't have permission" to someone who does.
        if (authoriser.UserId == requestedBy.UserId)
            return new OverrideResult(null, OverrideFailure.SamePerson,
                "An override has to be authorised by someone else.");

        // The supervisor's OWN Can() — which applies their ceiling, their time window and the
        // staleness tier. Nothing about being an override relaxes any of it.
        if (!authoriser.Can(permission, amountPence, nowLocal))
        {
            // Distinguish "not allowed at all" from "not for this much": one is the wrong person,
            // the other is the wrong person for THIS, and they lead to different next steps.
            var atAll = authoriser.Can(permission, null, nowLocal);
            return atAll
                ? new OverrideResult(null, OverrideFailure.OverTheirCeiling,
                    $"{authoriser.DisplayName} can't authorise an amount this large.")
                : new OverrideResult(null, OverrideFailure.NotPermitted,
                    $"{authoriser.DisplayName} doesn't have permission to authorise that.");
        }

        return new OverrideResult(
            new Override(requestedBy.UserId, authoriser.UserId, authoriser.DisplayName,
                permission, amountPence, _utcNow()),
            OverrideFailure.None,
            $"Authorised by {authoriser.DisplayName}.");
    }

    /// <summary>What proving the password concluded.</summary>
    private enum PasswordProof
    {
        Ok,
        Wrong,
        /// <summary>⚠ Nothing on this till can check it, and the platform could not be asked.</summary>
        NeedsOnline,

        /// <summary>⚠⚠ A DAMAGED CACHE ENTRY IS NOT A NEW ACCOUNT. Pre-step-28 this answered
        /// `NoCredential` with "this till's copy of that account is damaged — sync it again", and
        /// that message is MORE actionable than "connect once": it says the data is bad rather
        /// than that the person is new here. Collapsing the two would have lost that, so it did
        /// not.</summary>
        Corrupt,
    }

    /// <summary>
    /// Prove a password — step 28.
    ///
    /// ⚠⚠ THE THREE ROUTES, IN THIS ORDER, AND THE ORDER IS THE DESIGN:
    ///
    /// 1. **This till's own verifier**, if it has one. No network, and the reason a till that has
    ///    seen somebody before keeps working through an outage.
    /// 2. **The platform**, if it can be reached. On success this MINTS the verifier, so step 1
    ///    answers next time. This is the "online-first" of default 16.
    /// 3. **The platform hash the roster shipped** — the pre-step-28 behaviour, kept because the
    ///    server still ships hashes and will until a separate flagged change stops it. ⚠ When it
    ///    does, this branch becomes the one that returns `NeedsOnline`, and nothing else moves.
    ///
    /// ⚠⚠ A NULL FROM `_verifyOnline` IS "COULD NOT ASK", NOT "NO". Offline, a timeout and a dead
    /// server must route to *connect once*; only `false` is a wrong password. Getting this backwards
    /// tells an operator with a correct password that it is wrong every time the line drops.
    /// </summary>
    private async Task<PasswordProof> ProvePasswordAsync(
        TillOperatorDto op, string password, CancellationToken ct)
    {
        // 1 ── this device's verifier
        if (_verifiers is not null)
        {
            DeviceVerifier.Record? local = null;
            try { local = await _verifiers.GetAsync(op.UserId, ct); }
            catch { /* ⚠ An unreadable store is "no verifier", never a failed sign-in. */ }

            if (DeviceVerifier.CanVerify(local))
                return DeviceVerifier.Verify(local, password) ? PasswordProof.Ok : PasswordProof.Wrong;
        }

        // 2 ── the platform, which also earns this till a verifier
        if (_verifyOnline is not null)
        {
            bool? answer = null;
            try { answer = await _verifyOnline(op.Email ?? op.UserId.ToString(), password, ct); }
            catch { /* ⚠ A throw is "could not ask", same as null. */ }

            if (answer == false) return PasswordProof.Wrong;

            if (answer == true)
            {
                // ⚠⚠ MINTED ONLY AFTER THE SERVER SAID YES. Minting from an unproved password would
                // let anyone at an offline till enrol their own password against somebody else's
                // account — step 28's door, installed backwards.
                if (_verifiers is not null)
                {
                    try { await _verifiers.SaveAsync(DeviceVerifier.Mint(op.UserId, password, _utcNow()), ct); }
                    catch { /* ⚠ A failed mint costs a reconnection next time, never this sign-in. */ }
                }

                return PasswordProof.Ok;
            }
        }

        // 3 ── the platform hash the roster shipped (pre-step-28, still the server's behaviour)
        if (!string.IsNullOrEmpty(op.CredentialHashBase64) && !string.IsNullOrEmpty(op.CredentialSaltBase64))
        {
            byte[] hash, salt;
            try
            {
                hash = Convert.FromBase64String(op.CredentialHashBase64);
                salt = Convert.FromBase64String(op.CredentialSaltBase64);
            }
            catch (FormatException)
            {
                // ⚠ A corrupt cache entry must not read as a wrong password — that is a support call
                // about a typo nobody made. It reads as "this till needs to talk to Plutus".
                return PasswordProof.Corrupt;
            }

            return Pbkdf2.Verify(password, salt, hash) ? PasswordProof.Ok : PasswordProof.Wrong;
        }

        // ⚠ Nothing local, nothing shipped, and the platform unreachable.
        return PasswordProof.NeedsOnline;
    }
}

/// <summary>
/// Who authorised an action the signed-in operator could not do themselves.
///
/// ⚠ This is what goes in the audit trail, and it must name BOTH people. "A refund was authorised"
/// is worthless; "Sam rang it, Priya authorised it" is the record that makes a ceiling mean
/// anything. Recording only the supervisor would also quietly re-attribute the sale.
/// </summary>
public sealed record Override(
    Guid RequestedByUserId,
    Guid AuthorisedByUserId,
    string AuthorisedByName,
    string Permission,
    long? AmountPence,
    DateTime AtUtc);

/// <summary>Why an override was refused. Each says something different to the person holding the
/// screen, and collapsing them into "no" is how a shop stops trusting the till.</summary>
public enum OverrideFailure
{
    None = 0,
    /// <summary>The second operator's credentials did not check out.</summary>
    NotAuthenticated,
    /// <summary>They signed in fine — they simply do not hold the permission either.</summary>
    NotPermitted,
    /// <summary>They hold it, but not for this much.</summary>
    OverTheirCeiling,
    /// <summary>They authorised themselves. ⚠ Not an override at all.</summary>
    SamePerson,
}

public sealed record OverrideResult(Override? Granted, OverrideFailure Failure, string Message)
{
    public bool Succeeded => Granted != null;
}

/// <summary>Pulls the roster down and caches it.</summary>
public sealed class OperatorSync
{
    private readonly PlutusApiClient _api;
    private readonly IOperatorStore _store;

    /// <summary>
    /// ⚠ OPTIONAL, and step 28's server half depends on it being passed. With it, the roster fetch
    /// names the operators this till can already verify offline and the server omits their platform
    /// password hashes. Without it — an older caller, a test, a till whose store will not open — the
    /// roster arrives exactly as it always has, which is the safe direction.
    /// </summary>
    private readonly IDeviceVerifierStore? _verifiers;

    public OperatorSync(PlutusApiClient api, IOperatorStore store, IDeviceVerifierStore? verifiers = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _verifiers = verifiers;
    }

    /// <summary>
    /// Refresh this till's roster. Returns how many operators it now holds, or null if the server
    /// could not be asked.
    ///
    /// ⚠ On failure the EXISTING cache is left alone. Replacing a good roster with nothing because
    /// the wifi dropped would lock a shop out of its own till — the cache is the thing that makes
    /// offline sign-in possible, so it is only ever replaced by something better.
    /// </summary>
    public async Task<int?> RefreshAsync(Guid tillId, CancellationToken ct = default)
        => (await RefreshRosterAsync(tillId, ct))?.Operators.Length;

    /// <summary>
    /// Refresh and return the roster ITSELF — null when the server could not be asked.
    ///
    /// ⚠ THE DISTINCTION BETWEEN NULL AND EMPTY IS THE WHOLE POINT OF THIS OVERLOAD. The heartbeat
    /// uses it to decide whether a signed-in operator has been revoked (`OperatorRevocation`), and
    /// "the wifi dropped" must never look like "you are no longer allowed here" — that would sign a
    /// whole shop out mid-sale every time the line blipped.
    ///
    /// ⚠ The cached roster is still left alone on failure, for the same reason it always was.
    /// </summary>
    public async Task<TillOperatorsResult?> RefreshRosterAsync(Guid tillId, CancellationToken ct = default)
    {
        // ⚠⚠ TELL THE SERVER WHAT THIS TILL ALREADY HOLDS — step 28's server half. The reply then
        // omits the platform password hash for those operators, so a stolen till yields nothing for
        // anyone who has signed in on it. See `IDeviceVerifierStore.UsableVerifierUserIdsAsync`.
        //
        // ⚠ NEVER LETS A FAILURE HERE STOP THE ROSTER. An unreadable verifier store means "I hold
        // nothing", the server ships every hash exactly as before, and sign-in is unaffected. The
        // roster is how a shop signs in; it must not become conditional on a hardening feature.
        IReadOnlyList<Guid>? have = null;
        if (_verifiers is not null)
        {
            try { have = await _verifiers.UsableVerifierUserIdsAsync(ct); }
            catch (Exception) { have = null; }
        }

        var roster = await _api.GetTillOperatorsAsync(tillId, have, ct);
        if (roster is null) return null;

        await _store.SaveAsync(roster, ct);
        return roster;
    }
}
