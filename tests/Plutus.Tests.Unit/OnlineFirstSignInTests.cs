using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **Step 28 — online-first sign-in, and the device-local verifier.**
///
/// ⚠⚠ THE PROBLEM, FROM `OfflineCredentials`' OWN HEADER: *"A stolen till holds hashes at
/// PBKDF2-SHA1/101,010 … Those are the operators' PLATFORM passwords, and they work on the web till
/// too."* The roster ships every operator's platform credential to every till. Step 28 makes the
/// first sign-in of an account on a device ONLINE, mints a device-local verifier from it, and
/// verifies offline against that instead.
///
/// ⚠ THESE TESTS PIN THE DECISION, not the crypto — `DeviceVerifierTests` owns the mint/verify.
/// </summary>
public class OnlineFirstSignInTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Who = Guid.Parse("01931f3c-0000-7000-8000-0000000000aa");

    private sealed class FakeStore : IOperatorStore
    {
        private readonly TillOperatorsResult _roster;
        public FakeStore(TillOperatorsResult roster) => _roster = roster;
        public Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default) => Task.FromResult<TillOperatorsResult?>(_roster);
        public Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>⚠ Records what was saved, because "did the online sign-in MINT a verifier" is half
    /// the feature — without it the second sign-in still needs the network.</summary>
    private sealed class FakeVerifiers : IDeviceVerifierStore
    {
        public readonly Dictionary<Guid, DeviceVerifier.Record> Saved = new();
        public int Saves;
        public bool ThrowOnGet;

        public Task<DeviceVerifier.Record?> GetAsync(Guid userId, CancellationToken ct = default)
        {
            if (ThrowOnGet) throw new InvalidOperationException("unreadable store");
            return Task.FromResult(Saved.TryGetValue(userId, out var r) ? r : null);
        }

        public Task SaveAsync(DeviceVerifier.Record record, CancellationToken ct = default)
        {
            Saved[record.UserId] = record; Saves++;
            return Task.CompletedTask;
        }

        /// <summary>⚠ Mirrors the real store: only records this build can actually VERIFY count.
        /// A fake that claimed everything would let the server withhold a hash the till cannot
        /// replace, which is the one way step 28's server half can lock somebody out.</summary>
        public Task<IReadOnlyList<Guid>> UsableVerifierUserIdsAsync(CancellationToken ct = default)
        {
            if (ThrowOnGet) throw new InvalidOperationException("unreadable store");

            IReadOnlyList<Guid> ids = Saved.Values
                .Where(DeviceVerifier.CanVerify)
                .Select(r => r.UserId)
                .ToList();

            return Task.FromResult(ids);
        }

        public Task ForgetAsync(Guid userId, CancellationToken ct = default)
        {
            Saved.Remove(userId);
            return Task.CompletedTask;
        }
    }

    /// <summary>A roster entry. ⚠ `hash: null` is the shape the server will ship once it STOPS
    /// sending platform hashes — the separate flagged change step 28 anticipates.</summary>
    private static TillOperatorsResult Roster(string? hash = null, string? salt = null) =>
        new(Guid.NewGuid(), Now,
            new[] { new TillOperatorDto(Who, "Sam", "sam@kapow.test", hash, salt, Array.Empty<OperatorGrantDto>()) });

    private static (byte[] hash, byte[] salt) PlatformCredential(string password) => Pbkdf2.Hash(password);

    private static OperatorLogin Build(
        TillOperatorsResult roster,
        IDeviceVerifierStore? verifiers = null,
        Func<string, string, CancellationToken, Task<bool?>>? online = null) =>
        new(new FakeStore(roster), OfflineCredentialPolicy.Default, () => Now, verifiers, online);

    /// <summary>
    /// ⚠⚠ THE HEADLINE: a till that has never seen this account, with no platform hash shipped and
    /// no way to ask, refuses with CONNECT ONCE — not "wrong password".
    /// </summary>
    [Fact]
    public async Task A_first_ever_sign_in_offline_is_refused_with_connect_once_wording()
    {
        var login = Build(Roster(), new FakeVerifiers(), online: (_, _, _) => Task.FromResult<bool?>(null));

        var result = await login.SignInAsync("sam@kapow.test", "correct horse");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailure.NeedsOnlineFirstSignIn, result.Failure);
        Assert.Contains("connect", result.Message, StringComparison.OrdinalIgnoreCase);
        // ⚠ AND IT MUST NOT SAY THE PASSWORD IS WRONG. Somebody who typed correctly and is told
        // otherwise tries three more times and then phones a manager.
        Assert.DoesNotContain("wrong password", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⚠⚠ AND AFTER ONE ONLINE SIGN-IN, THE SAME ACCOUNT WORKS OFFLINE — the other half of
    /// default 16, and the reason this is not just a lockout.</summary>
    [Fact]
    public async Task After_one_online_sign_in_the_same_account_signs_in_offline()
    {
        var verifiers = new FakeVerifiers();

        // online: the platform says yes
        var onlineLogin = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(true));
        Assert.True((await onlineLogin.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
        Assert.Equal(1, verifiers.Saves);

        // offline: the platform cannot be asked, and it no longer matters
        var offlineLogin = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(null));
        Assert.True((await offlineLogin.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
    }

    /// <summary>⚠ The verifier is a real check, not a rubber stamp: the WRONG password against a
    /// minted verifier is a wrong password, offline.</summary>
    [Fact]
    public async Task The_wrong_password_against_a_minted_verifier_is_refused()
    {
        var verifiers = new FakeVerifiers();
        await Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(true))
            .SignInAsync("sam@kapow.test", "correct horse");

        var offline = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(null));
        var result = await offline.SignInAsync("sam@kapow.test", "wrong horse");

        Assert.Equal(LoginFailure.WrongPassword, result.Failure);
    }

    /// <summary>
    /// ⚠⚠ THE VERIFIER IS MINTED ONLY AFTER THE SERVER SAYS YES. Minting from an unproved password
    /// would let anyone at an offline till enrol their own password against somebody else's
    /// account — step 28's door, installed backwards.
    /// </summary>
    [Fact]
    public async Task A_refused_online_sign_in_mints_nothing()
    {
        var verifiers = new FakeVerifiers();
        var login = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(false));

        var result = await login.SignInAsync("sam@kapow.test", "guessing");

        Assert.Equal(LoginFailure.WrongPassword, result.Failure);
        Assert.Empty(verifiers.Saved);
    }

    /// <summary>
    /// ⚠⚠ NULL FROM THE ONLINE CHECK IS "COULD NOT ASK", NOT "NO". Offline, a timeout and a dead
    /// server must all route to *connect once*; only `false` is a wrong password. Getting this
    /// backwards tells an operator with a correct password that it is wrong every time the line
    /// drops — and mints nothing, so it never recovers.
    /// </summary>
    [Fact]
    public async Task An_unreachable_platform_is_not_a_wrong_password()
    {
        var verifiers = new FakeVerifiers();
        var login = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(null));

        var result = await login.SignInAsync("sam@kapow.test", "correct horse");

        Assert.Equal(LoginFailure.NeedsOnlineFirstSignIn, result.Failure);
        Assert.Empty(verifiers.Saved);
    }

    /// <summary>⚠ A throwing online check is the same as null — a network stack that raises rather
    /// than returning must not become a wrong password either.</summary>
    [Fact]
    public async Task A_throwing_online_check_is_treated_as_unreachable()
    {
        var login = Build(Roster(), new FakeVerifiers(),
            online: (_, _, _) => throw new TimeoutException("no route"));

        var result = await login.SignInAsync("sam@kapow.test", "correct horse");

        Assert.Equal(LoginFailure.NeedsOnlineFirstSignIn, result.Failure);
    }

    /// <summary>
    /// ⚠⚠ THE LOCAL VERIFIER IS TRIED FIRST, AND THE NETWORK IS NEVER TOUCHED. That is the whole
    /// point of holding one: a till that has seen somebody before keeps working through an outage,
    /// and does not wait on a timeout to do it.
    /// </summary>
    [Fact]
    public async Task A_till_with_a_verifier_does_not_call_the_platform_at_all()
    {
        var verifiers = new FakeVerifiers();
        await Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(true))
            .SignInAsync("sam@kapow.test", "correct horse");

        var called = 0;
        var login = Build(Roster(), verifiers, online: (_, _, _) => { called++; return Task.FromResult<bool?>(true); });

        Assert.True((await login.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
        Assert.Equal(0, called);
    }

    /// <summary>⚠ An unreadable verifier store is "no verifier", never a failed sign-in — it falls
    /// through to the platform rather than locking somebody out of a working till.</summary>
    [Fact]
    public async Task An_unreadable_verifier_store_falls_through_rather_than_failing()
    {
        var verifiers = new FakeVerifiers { ThrowOnGet = true };
        var login = Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(true));

        Assert.True((await login.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
    }

    // ── the migration path ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ NOTHING BREAKS WHILE THE SERVER STILL SHIPS HASHES. Step 28 lands before the separate,
    /// flagged change that stops the roster carrying platform credentials — so a till that cannot
    /// reach the platform must still sign somebody in against the shipped hash, exactly as before.
    ///
    /// ⚠ THIS IS THE TEST THAT MAKES THE ROLLOUT SAFE. Without it, deploying step 28 to a fleet
    /// mid-outage would refuse every sign-in on every till that had not yet minted a verifier.
    /// </summary>
    [Fact]
    public async Task A_shipped_platform_hash_still_works_offline_for_now()
    {
        var (hash, salt) = PlatformCredential("correct horse");
        var roster = Roster(Convert.ToBase64String(hash), Convert.ToBase64String(salt));

        var login = Build(roster, new FakeVerifiers(), online: (_, _, _) => Task.FromResult<bool?>(null));

        Assert.True((await login.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
    }

    /// <summary>⚠ And it is still a real check — the wrong password against a shipped hash is
    /// refused as a wrong password, not as "connect once".</summary>
    [Fact]
    public async Task A_wrong_password_against_a_shipped_hash_is_still_a_wrong_password()
    {
        var (hash, salt) = PlatformCredential("correct horse");
        var roster = Roster(Convert.ToBase64String(hash), Convert.ToBase64String(salt));

        var login = Build(roster, new FakeVerifiers(), online: (_, _, _) => Task.FromResult<bool?>(null));
        var result = await login.SignInAsync("sam@kapow.test", "wrong horse");

        Assert.Equal(LoginFailure.WrongPassword, result.Failure);
    }

    /// <summary>
    /// ⚠⚠ PRE-STEP-28 CALLERS ARE UNCHANGED. Constructed with no verifier store and no online
    /// check — which is every caller that has not been wired up — the behaviour is exactly what it
    /// was: verify against the shipped hash. This is why step 28 landed without a flag day.
    /// </summary>
    [Fact]
    public async Task A_login_built_the_old_way_behaves_exactly_as_before()
    {
        var (hash, salt) = PlatformCredential("correct horse");
        var roster = Roster(Convert.ToBase64String(hash), Convert.ToBase64String(salt));

        var login = new OperatorLogin(new FakeStore(roster), OfflineCredentialPolicy.Default, () => Now);

        Assert.True((await login.SignInAsync("sam@kapow.test", "correct horse")).Succeeded);
        Assert.Equal(LoginFailure.WrongPassword, (await login.SignInAsync("sam@kapow.test", "nope")).Failure);
    }

    /// <summary>
    /// ⚠⚠ THE HORIZONS STILL WIN. A verifier says "this password is right on this till"; it says
    /// nothing about whether the till has heard from the platform recently enough to be trusted.
    /// A leaver dismissed in the portal is revoked offline by the horizon and by nothing else —
    /// `OfflineCredentials`' header calls that the only erasure mechanism an offline till has.
    /// </summary>
    [Fact]
    public async Task A_verifier_does_not_bypass_the_staleness_horizon()
    {
        var verifiers = new FakeVerifiers();
        await Build(Roster(), verifiers, online: (_, _, _) => Task.FromResult<bool?>(true))
            .SignInAsync("sam@kapow.test", "correct horse");

        // A roster far past the sell horizon, with the verifier still perfectly valid.
        var stale = new TillOperatorsResult(Guid.NewGuid(), Now.AddDays(-365),
            new[] { new TillOperatorDto(Who, "Sam", "sam@kapow.test", null, null, Array.Empty<OperatorGrantDto>()) });

        var login = new OperatorLogin(new FakeStore(stale), OfflineCredentialPolicy.Default, () => Now,
            verifiers, (_, _, _) => Task.FromResult<bool?>(null));

        var result = await login.SignInAsync("sam@kapow.test", "correct horse");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailure.CredentialsTooStale, result.Failure);
    }
}
