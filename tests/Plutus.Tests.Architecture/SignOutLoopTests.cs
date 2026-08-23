using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **`signOut()` reloads the page, and on 2026-08-22 that locked every user out of the portal.**
///
/// ⚠⚠ THE LOOP, IN FULL: `api.ts` calls `signOut()` on **any** 401. `signOut()` calls
/// `window.location.reload()`. So a single authed API call made *before* sign-in becomes: login
/// screen → 401 → reload → login screen, for ever. The console errors flash past too fast to read,
/// which is exactly how it was reported.
///
/// ⚠ WHAT CAUSED IT was a boot-time `fetchVatPeriods()` added by WP-TZ with `[]` deps, against an
/// endpoint carrying `[Authorize(perm:portal.company.manage)]`. ⚠⚠ **ITS `.catch()` DID NOT HELP** —
/// `signOut()` runs inside the api layer *before* the rejection is handed back, so the catch
/// swallowed the message and kept the reload. That is the part that made it hard to see.
///
/// ⚠ SO THE FIX IS IN TWO PLACES AND THIS PINS THE SECOND ONE. Gating that one effect fixes the one
/// bug; guarding `signOut()` fixes the class, because signing out of nothing is not a sign-out —
/// with no session the user is already where the reload would send them, so it only re-runs
/// whatever 401'd.
///
/// ⚠ BOTH FRONTENDS, though only the portal actually looped. The till has no pre-auth authed call
/// today, but it carries the identical `signOut()`, and the same mistake there stops a shop selling
/// rather than stopping an office reading a report.
///
/// ⚠ Source text, not behaviour — this suite references no product projects (and neither frontend
/// has a runner that can drive `window.location`). It is a crude pin, and a deliberate one: it
/// cannot prove the guard works, only that nobody has quietly deleted it.
/// </summary>
public class SignOutLoopTests
{
    [Theory]
    [InlineData("Plutus/Frontend/Plutus.Frontend.Portal/src/auth.ts")]
    [InlineData("Plutus/Frontend/Plutus.Frontend.WebApp/src/auth.ts")]
    public void SignOut_returns_without_reloading_when_there_is_no_session(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root(), relativePath));

        var start = text.IndexOf("export function signOut(): void {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{relativePath}: no `signOut()` — if it was renamed, this pin must follow it.");

        var end = text.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{relativePath}: could not find the end of `signOut()`.");
        var body = text[start..end];

        var reload = body.IndexOf("window.location.reload()", StringComparison.Ordinal);
        if (reload < 0) return; // no reload at all — the loop is impossible by construction

        // ⚠⚠ `everHadAToken`, NOT `getSession()` — CORRECTED 2026-08-23. This pin originally demanded
        // a `getSession()` check, and that check was itself the bug: `getSession()` returns null for an
        // EXPIRED session, so the guard fired exactly when an operator's token died mid-shift and
        // suppressed the reload that should have returned them to the login screen. The till stayed up
        // with a dead token and every call answered 401. A pin can enforce the wrong rule as
        // confidently as the right one.
        var guard = body.IndexOf("everHadAToken", StringComparison.Ordinal);
        Assert.True(
            guard >= 0 && guard < reload,
            $"{relativePath}: `signOut()` reaches `window.location.reload()` without first checking "
            + "`everHadAToken`. The question is NOT \"is there a session\" — `getSession()` answers "
            + "null for an expired one — but \"was there ever a token on this page\". A 401 after a "
            + "token has been in play means it died, and the operator must be returned to the login "
            + "screen; a 401 with no token ever means we are already there, and reloading would loop.");
    }
}
