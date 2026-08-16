using System.Net;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Whether a till is still allowed to be a till (WP4, step 21).
///
/// ⚠⚠ THIS RULE CAN BE WRONG IN TWO DIRECTIONS AND BOTH ARE SERIOUS. Too lax and a lost or stolen
/// till keeps selling for twelve hours, because device tokens have no server-side denylist. Too
/// strict and a dropped connection closes a shop mid-sale — on the flakiest sites first. The tests
/// below are mostly about the second one, because it is the failure that would be blamed on
/// something else.
/// </summary>
public class DeviceRevocationTests
{
    private static DeviceStatusResult Status(string s) => new(s);

    // ── the one case that stops a till ────────────────────────────────────────────────────────

    [Fact]
    public void An_explicit_revocation_stops_the_till()
    {
        var standing = DeviceRevocation.Check(HttpStatusCode.OK, Status("Revoked"));

        Assert.Equal(DeviceStanding.Revoked, standing);
        Assert.True(DeviceRevocation.MustStop(standing));
    }

    /// <summary>⚠ Case-insensitively — the contract folds case, and whether a shop's till stops must
    /// not depend on a backend's capitalisation.</summary>
    [Theory]
    [InlineData("revoked")]
    [InlineData("REVOKED")]
    public void A_revocation_in_any_case_stops_the_till(string spelling) =>
        Assert.Equal(DeviceStanding.Revoked, DeviceRevocation.Check(HttpStatusCode.OK, Status(spelling)));

    // ── the cases that must NOT stop a till ───────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ PendingRemoval KEEPS TRADING. Somebody has asked for the till back and nobody has approved
    /// it. Stopping here would turn un-enrolment into a way to take a shop down — request removal of
    /// a till you do not own, and it stops serving customers before any human looks at it.
    /// </summary>
    [Fact]
    public void A_removal_request_does_not_stop_the_till()
    {
        var standing = DeviceRevocation.Check(HttpStatusCode.OK, Status("PendingRemoval"));

        Assert.Equal(DeviceStanding.RemovalRequested, standing);
        Assert.False(DeviceRevocation.MustStop(standing));
    }

    [Fact]
    public void An_active_device_trades()
    {
        Assert.Equal(DeviceStanding.Trading, DeviceRevocation.Check(HttpStatusCode.OK, Status("Active")));
    }

    /// <summary>
    /// ⚠⚠ THE LOAD-BEARING TEST. No answer is not a revocation. If a failed poll stopped the till,
    /// every dropped connection would close a shop mid-sale — a worse outage than the one this
    /// prevents, and the same rule `OperatorRevocation` is built on.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void A_poll_that_failed_never_stops_the_till(HttpStatusCode code)
    {
        Assert.Equal(DeviceStanding.Trading, DeviceRevocation.Check(code, null));
        Assert.False(DeviceRevocation.MustStop(DeviceRevocation.Check(code, null)));
    }

    /// <summary>
    /// ⚠⚠ AND NEITHER DO 401/403/404, deliberately. Each looks terminal and none reliably is: a
    /// clock-skewed till, a mis-issued token, a wrong device id or a tenant filter that did not
    /// match all land here, and a routing mistake would otherwise close every till at once. A truly
    /// un-enrolled device gets the explicit "Revoked" answer instead — approving a removal sets the
    /// status, it does not delete the row.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void An_auth_or_unknown_device_answer_does_not_stop_the_till(HttpStatusCode code) =>
        Assert.False(DeviceRevocation.MustStop(DeviceRevocation.Check(code, null)));

    /// <summary>⚠ A 200 with no usable body is still "no answer" — the platform replied, but not
    /// with anything that says this till is finished.</summary>
    [Fact]
    public void A_success_with_no_body_does_not_stop_the_till() =>
        Assert.Equal(DeviceStanding.Trading, DeviceRevocation.Check(HttpStatusCode.OK, null));

    /// <summary>
    /// ⚠ A STATUS THIS BUILD HAS NOT BEEN TAUGHT KEEPS TRADING. Guessing that an unknown word means
    /// "stop" would let a backend rename break every till in the estate at once; the opposite
    /// failure is bounded by the token's 12h life and is visible on the Plutus tab.
    /// </summary>
    [Theory]
    [InlineData("Suspended")]
    [InlineData("Quarantined")]
    [InlineData("")]
    public void An_unrecognised_status_keeps_trading(string unknown) =>
        Assert.Equal(DeviceStanding.Trading, DeviceRevocation.Check(HttpStatusCode.OK, Status(unknown)));

    /// <summary>⚠ `MustStop` is the named door precisely so no caller writes its own comparison and
    /// quietly folds `RemovalRequested` into it.</summary>
    [Fact]
    public void Only_revoked_must_stop()
    {
        Assert.True(DeviceRevocation.MustStop(DeviceStanding.Revoked));
        Assert.False(DeviceRevocation.MustStop(DeviceStanding.RemovalRequested));
        Assert.False(DeviceRevocation.MustStop(DeviceStanding.Trading));
    }

    /// <summary>⚠ The message names the TILL, not the account — otherwise somebody tries another
    /// login, then another, and concludes the staff accounts are broken.</summary>
    [Fact]
    public void The_revocation_message_blames_the_till_and_points_somewhere()
    {
        Assert.Contains("till", DeviceRevocation.RevokedMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manager", DeviceRevocation.RevokedMessage, System.StringComparison.OrdinalIgnoreCase);
    }
}
