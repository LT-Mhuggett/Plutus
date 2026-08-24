using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **WP-SIGNUP's front door, through the real middleware pipeline.**
///
/// ⚠⚠ THE PART THAT CANNOT BE UNIT-TESTED. The rate limiter and the flag gate are the two controls
/// standing between an anonymous internet endpoint and the applications table, and both live in the
/// pipeline rather than in a service — the limiter is middleware, and the gate's 404 is what a caller
/// actually sees. A unit test on `SignupGate` proves the boolean; only this proves the door.
///
/// ⚠ The signup routes are on the `enrol` policy: a FIXED WINDOW keyed by remote IP, 5/min by
/// default. Not the per-tenant limiter `RateLimitE2eTests` covers — that one is keyed by tenant,
/// exempts platform-admin and runs at 30 rps, and an anonymous applicant has no tenant at all.
/// </summary>
public class SignupE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SignupE2eTests(PlutusAppFactory f) => _f = f;

    private static object Body(string name, string email) => new
    {
        businessName = name, contactName = "A Person", contactEmail = email, phone = "", region = "UK",
    };

    private async Task SetDoorAsync(bool open)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "integration";
        var flag = await db.PlatformFlags.FirstOrDefaultAsync(f => f.FlagName == SignupGate.FlagName);
        if (flag == null) db.PlatformFlags.Add(flag = new PlatformFlag { FlagName = SignupGate.FlagName });
        flag.Enabled = open;
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// ⚠⚠ WITH THE FLAG OFF, THE FRONT DOOR IS NOT MERELY REFUSED — IT IS NOT THERE.
    ///
    /// This is the control Matt asked for: *"I would like to build out the landing page, but not have
    /// it externally facing for now. Can I have this as an operator portal setting?"* It can, and it
    /// had to be — the API was already public. Caddy could not do this job without a path carve-out
    /// on the host the till and web till share.
    ///
    /// ⚠ 404 rather than 403, deliberately: a 403 tells a stranger a signup API exists and is merely
    /// switched off, which is an invitation to come back.
    /// </summary>
    [Fact]
    public async Task With_the_flag_off_every_signup_route_is_404()
    {
        await SetDoorAsync(false);
        var c = _f.CreateClient();

        Assert.Equal(404, (int)(await c.PostAsJsonAsync("/api/v1/signup", Body("Closed Shop", "a@closed.test"))).StatusCode);
        Assert.Equal(404, (int)(await c.GetAsync("/api/v1/signup/dpa")).StatusCode);
        Assert.Equal(404, (int)(await c.PostAsync("/api/v1/signup/verify?token=whatever", null)).StatusCode);
        Assert.Equal(404, (int)(await c.PostAsJsonAsync("/api/v1/signup/dpa/accept",
            new { applicationId = Guid.NewGuid(), version = "2026-01" })).StatusCode);
    }

    /// <summary>⚠ And nothing was written while the door was shut — a 404 that still recorded the
    /// attempt would be a closed door with an open window.</summary>
    [Fact]
    public async Task A_closed_door_writes_nothing()
    {
        await SetDoorAsync(false);
        var c = _f.CreateClient();
        await c.PostAsJsonAsync("/api/v1/signup", Body("Ghost Shop", "ghost@closed.test"));

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        Assert.False(await db.TenantApplications.AnyAsync(a => a.ContactEmailFolded == "ghost@closed.test"));
    }

    /// <summary>
    /// ⚠⚠ THE DoD LINE: *"Rate limits hold under a scripted flood; the test asserts the 429, not
    /// just the happy path."* This is the end-to-end half of it.
    ///
    /// ⚠⚠ AND IT IS NOT PROOF THAT THE `enrol` POLICY IS DOING THE THROTTLING — read this before
    /// trusting it. `PlutusAppFactory` sets `RATE_LIMIT_ENROL_PER_MIN = 100000` ("don't throttle the
    /// test IP"), so in the integration host the enrol window is effectively disabled and the 429
    /// here comes from the per-tenant limiter (`RATE_LIMIT_DEFAULT_RPS = 30`). The env var is read
    /// once when services are built, so a test cannot lower it for itself.
    ///
    /// What this test therefore proves: **an anonymous flood against signup is throttled by the
    /// deployed pipeline**, and requests do get through when they should. That the specific control
    /// is the `enrol` policy is pinned separately and cheaply, by
    /// `SignupRateLimitPolicyTests` in the Architecture suite — because an attribute that gets
    /// deleted is exactly the kind of regression this test cannot see.
    ///
    /// ⚠ Each request carries a DISTINCT business name and email, so nothing is rejected as a
    /// duplicate. A flood that 400s on the name reservation would satisfy the 429 assertion while
    /// proving the limiter does nothing.
    /// </summary>
    [Fact]
    public async Task A_flood_of_signups_is_throttled_with_429()
    {
        await SetDoorAsync(true);
        var c = _f.CreateClient();
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var codes = await Task.WhenAll(Enumerable.Range(0, 40).Select(async i =>
            (int)(await c.PostAsJsonAsync("/api/v1/signup",
                Body($"Flood Shop {stamp} {i}", $"flood-{stamp}-{i}@example.test"))).StatusCode));

        Assert.Contains(429, codes);

        // ⚠ The positive control. Without it, a route that 429'd for the WRONG reason — or one that
        // was simply broken — would satisfy the assertion above and look like a working limiter.
        Assert.True(codes.Any(x => x == 202 || x == 400 || x == 409),
            "Nothing got through at all, so this proves the endpoint is unreachable rather than "
            + $"rate-limited. Codes: {string.Join(",", codes.Distinct().OrderBy(x => x))}");
    }

    /// <summary>
    /// The happy path through the pipeline, with the door open — and ⚠ the DPA gate refusing,
    /// because no agreement is published. That 409 is the intended end state today, not a fault:
    /// recording acceptance of unpublished wording would be evidence of nothing (WP-signup §4.3).
    /// </summary>
    [Fact]
    public async Task With_the_door_open_an_application_is_accepted_and_the_dpa_gate_still_refuses()
    {
        await SetDoorAsync(true);
        var c = _f.CreateClient();
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var applied = await c.PostAsJsonAsync("/api/v1/signup", Body($"Open Shop {stamp}", $"open-{stamp}@example.test"));
        Assert.Equal(202, (int)applied.StatusCode);

        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            Assert.True(await db.TenantApplications.AnyAsync(a => a.ContactEmailFolded == $"open-{stamp}@example.test"));
            // ⚠⚠ AND STILL NO TENANT. The whole premise of stage 1.
            Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Name.StartsWith("Open Shop")));
        }

        var dpa = await c.GetAsync("/api/v1/signup/dpa");
        Assert.Equal(409, (int)dpa.StatusCode);
    }
}
