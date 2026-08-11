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

    public OperatorLogin(IOperatorStore store, OfflineCredentialPolicy? policy = null, Func<DateTime>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? OfflineCredentialPolicy.Default;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
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

        if (string.IsNullOrEmpty(op.CredentialHashBase64) || string.IsNullOrEmpty(op.CredentialSaltBase64))
            return LoginResult.Failed(LoginFailure.NoCredential,
                $"{op.DisplayName} doesn't have a Plutus password yet — an administrator sets one in the portal.");

        byte[] hash, salt;
        try
        {
            hash = Convert.FromBase64String(op.CredentialHashBase64);
            salt = Convert.FromBase64String(op.CredentialSaltBase64);
        }
        catch (FormatException)
        {
            // A corrupt cache entry must not read as a wrong password — that is a support call
            // about a typo nobody made.
            return LoginResult.Failed(LoginFailure.NoCredential,
                "This till's copy of that account is damaged. Sync it again from the Plutus tab.");
        }

        if (!Pbkdf2.Verify(password ?? string.Empty, salt, hash))
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

    public OperatorSync(PlutusApiClient api, IOperatorStore store)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
        var roster = await _api.GetTillOperatorsAsync(tillId, ct);
        if (roster is null) return null;

        await _store.SaveAsync(roster, ct);
        return roster;
    }
}
