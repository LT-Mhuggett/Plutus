using System;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Putting a revoked operator out of the till, on the heartbeat.
///
/// ⚠ Matt, 2026-08-11: *"As part of the heartbeat, the re-read of permissions needs to happen. If a
/// user is disabled, the user needs immediately logging out."* Until then the roster was refreshed
/// at the LOGIN SCREEN and by a manual button, and nowhere else — so a till left signed in through a
/// shift never re-read permissions at all.
///
/// ⚠⚠ THIS FILE EXISTS FOR ONE LINE. The check has exactly one way to be catastrophically wrong,
/// and the wrong version is indistinguishable from the right one at a glance: treating "the server
/// could not be asked" as "you are not on the list". That version signs the WHOLE SHOP out, mid-sale,
/// every time the broadband hiccups, with a message accusing the operator of being disabled — a
/// worse outage than the one the feature prevents, hitting the flakiest sites first.
/// </summary>
public class OperatorRevocationTests
{
    private static readonly Guid Sam = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Alex = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TillOperatorDto Op(Guid id) =>
        new(id, "Someone", "someone@example.com", "hash", "salt", Array.Empty<OperatorGrantDto>());

    private static TillOperatorsResult Roster(params Guid[] ids) =>
        new(Guid.NewGuid(), DateTime.UtcNow, Array.ConvertAll(ids, Op));

    [Fact]
    public void An_operator_still_on_the_roster_stays_signed_in()
    {
        Assert.Equal(RevocationReason.None, OperatorRevocation.Check(Sam, Roster(Sam, Alex)));
    }

    [Fact]
    public void An_operator_GONE_from_the_roster_is_revoked()
    {
        // ⚠ The server filters the roster on `e.Active` AND on holding at least one `pos.*`
        // permission — so "disabled" and "no longer allowed at a counter" both arrive as an
        // absence, and both mean the same thing here.
        Assert.Equal(RevocationReason.NoLongerPermitted, OperatorRevocation.Check(Sam, Roster(Alex)));
    }

    [Fact]
    public void A_ROSTER_THAT_COULD_NOT_BE_FETCHED_IS_NOT_AN_EMPTY_ROSTER()
    {
        // ⚠⚠ THE ONE THAT MATTERS. `RefreshRosterAsync` returns null when the server could not be
        // asked, and deliberately leaves the cached roster alone — because replacing a good roster
        // with nothing when the wifi drops would lock a shop out of its own till.
        //
        // If this returned NoLongerPermitted, every dropped connection would sign the shop out.
        Assert.Equal(RevocationReason.None, OperatorRevocation.Check(Sam, null));
    }

    [Fact]
    public void An_EMPTY_roster_the_server_actually_sent_DOES_revoke()
    {
        // ⚠ The other half of the same rule, and it must not be lost while protecting the first.
        // An empty roster is a real answer — nobody is assigned to this till any more — and
        // treating it as "no answer" would make the feature unreachable in exactly the case where
        // a whole till has been de-staffed.
        Assert.Equal(RevocationReason.NoLongerPermitted,
            OperatorRevocation.Check(Sam, new TillOperatorsResult(Guid.NewGuid(), DateTime.UtcNow,
                Array.Empty<TillOperatorDto>())));
    }

    [Fact]
    public void Nobody_signed_in_is_never_a_revocation()
    {
        // ⚠ The beat runs before anyone signs in and after they are signed out. Without this the
        // login screen would raise "your account has been disabled" at an empty till, every minute.
        Assert.Equal(RevocationReason.None, OperatorRevocation.Check(null, Roster(Alex)));
        Assert.Equal(RevocationReason.None, OperatorRevocation.Check(null, null));
    }

    [Fact]
    public void A_null_Operators_array_is_treated_as_empty_not_as_a_crash()
    {
        // ⚠ A malformed answer must not throw on a background timer — an escape there takes the
        // app down from a thread with no handler.
        var malformed = new TillOperatorsResult(Guid.NewGuid(), DateTime.UtcNow, null!);

        Assert.Equal(RevocationReason.NoLongerPermitted, OperatorRevocation.Check(Sam, malformed));
    }

    [Fact]
    public void The_message_names_the_ACCOUNT_not_the_till()
    {
        // ⚠ Matt's wording, verbatim. "You have been logged out" sends somebody to reboot the
        // machine; this sends them to the person who can actually help.
        Assert.Equal("Your account has been disabled, please speak to your manager",
            OperatorRevocation.DisabledMessage);
    }
}
