using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **The signup front door must stay rate-limited and flag-gated.**
///
/// ⚠⚠ WHY THIS IS A SOURCE SCAN AND NOT AN E2E TEST. `SignupE2eTests` floods the endpoint and gets a
/// 429 — but `PlutusAppFactory` sets `RATE_LIMIT_ENROL_PER_MIN = 100000` ("don't throttle the test
/// IP"), so in that host the enrol window is effectively off and the 429 arrives from the per-tenant
/// limiter instead. The flood test proves throttling happens; it **cannot** prove which policy did
/// it, and would keep passing if `[EnableRateLimiting("enrol")]` were deleted tomorrow.
///
/// ⚠ That gap was found by reading the factory rather than by the test failing, which is the whole
/// argument for pinning the attribute directly. Cheap, and it fails for the right reason.
///
/// Scanned on disk, matching the rest of this suite (no product references).
/// </summary>
public class SignupRateLimitPolicyTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string SignupController() =>
        Path.Combine(RepoRoot(), "src", "Plutus.Tenancy", "Controllers", "SignupController.cs");

    [Fact]
    public void The_signup_controller_still_exists_where_this_test_expects_it()
    {
        // ⚠ The positive control. A moved or renamed file would make every assertion below vacuous,
        // and a test that passes because it read nothing is worse than no test.
        Assert.True(File.Exists(SignupController()),
            "SignupController.cs is not where this test looks. Repoint it — do not delete the test.");
    }

    /// <summary>
    /// ⚠⚠ THE ANONYMOUS FRONT DOOR MUST CARRY THE RATE-LIMIT POLICY. Every route on it is
    /// `[AllowAnonymous]` and writes to a table, so without a limiter one script fills the
    /// applications queue and the operator's inbox.
    /// </summary>
    [Fact]
    public void The_signup_controller_is_on_the_enrol_rate_limit_policy()
    {
        var src = File.ReadAllText(SignupController());

        Assert.Contains("[EnableRateLimiting(\"enrol\")]", src, StringComparison.Ordinal);

        // ⚠ At CLASS level, so a new action inherits it. An action-level attribute is one forgotten
        // decoration away from an ungated route, and the next endpoint added here will be added in a
        // hurry by somebody who did not read this file.
        var classLine = src.IndexOf("public sealed class SignupController", StringComparison.Ordinal);
        var policyLine = src.IndexOf("[EnableRateLimiting(\"enrol\")]", StringComparison.Ordinal);
        Assert.True(policyLine >= 0 && policyLine < classLine,
            "[EnableRateLimiting(\"enrol\")] must sit on the CLASS, above the declaration, so every "
            + "action inherits it — including the next one somebody adds.");
    }

    /// <summary>
    /// ⚠⚠ AND EVERY ROUTE MUST CHECK THE FLAG. The signup API was reachable from the internet the
    /// moment it shipped, with no landing page anywhere — Caddy proxies `/api/*` on the till host and
    /// these routes are anonymous. `signup.public` defaults to CLOSED, and the check is per-action
    /// because there is no filter doing it for them.
    ///
    /// ⚠ Counted, not merely present: a gate on three of four routes is an open route.
    /// </summary>
    [Fact]
    public void Every_signup_action_checks_the_open_closed_flag()
    {
        var lines = File.ReadAllLines(SignupController());

        var actions = lines.Count(l => l.TrimStart().StartsWith("[Http", StringComparison.Ordinal));
        var gates = lines.Count(l => l.Contains("ClosedAsync(ct)", StringComparison.Ordinal));

        Assert.True(actions > 0, "Found no [Http…] actions — the scan is not reading what it thinks.");
        Assert.True(gates >= actions,
            $"{actions} signup actions but only {gates} flag checks. A gate on all-but-one route is "
            + "an open route, and the one left out will be the newest.");
    }
}
