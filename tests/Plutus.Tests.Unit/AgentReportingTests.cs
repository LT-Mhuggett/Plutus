using System;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// When a till tells the platform about its hardware agent (FE3.0).
///
/// ⚠ A C2 TWIN of the web till's `hardware.ts reportAgentStatus`, which has held this rule since
/// FE3.0. It moved here when MAUI needed it, rather than a second copy appearing in the till.
///
/// ⚠ The rule is pulling in two directions and both matter: MAUI's cadence is **60 seconds** against
/// the web till's five minutes, so sending every tick would be hundreds of times the useful traffic
/// to overwrite a row with what it already holds — but never re-sending makes a till that reported
/// once look identical to one that has been switched off since.
/// </summary>
public class AgentReportingTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    private static AgentSnapshot Agent(
        string version = "1.4.0", string printer = "EPSON TM-T20III", bool online = true) =>
        new(version, printer, online);

    // ── the first report ──────────────────────────────────────────────────────────────────────

    /// <summary>⚠ Nothing accepted yet — send whatever it says. This is what puts a newly enrolled
    /// till on the portal's fleet list at all.</summary>
    [Fact]
    public void A_till_that_has_never_reported_sends() =>
        Assert.True(AgentReporting.ShouldSend(Agent(), null, null, Now));

    /// <summary>
    /// ⚠⚠ "NO AGENT" IS A READING, NOT SILENCE. A till PC without an agent must report that — the
    /// portal shows it, and skipping it makes a till with no agent indistinguishable from a till that
    /// has never checked in.
    /// </summary>
    [Fact]
    public void A_till_with_no_agent_still_reports_that() =>
        Assert.True(AgentReporting.ShouldSend(AgentSnapshot.None, null, null, Now));

    // ── unchanged ─────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ THE COMMON CASE, and the one that protects the endpoint: nothing has changed and
    /// the last confirmation is recent, so say nothing. On a 60s beat this declines ~360 times
    /// between sends.</summary>
    [Fact]
    public void An_unchanged_snapshot_just_confirmed_says_nothing() =>
        Assert.False(AgentReporting.ShouldSend(Agent(), Agent(), Now.AddMinutes(-5), Now));

    /// <summary>⚠ But staleness forces one, or a till that reported once looks switched off. 6 hours,
    /// matching the web till exactly.</summary>
    [Fact]
    public void An_unchanged_snapshot_is_reconfirmed_when_it_goes_stale()
    {
        Assert.False(AgentReporting.ShouldSend(
            Agent(), Agent(), Now - AgentReporting.Reconfirm + TimeSpan.FromMinutes(1), Now));

        Assert.True(AgentReporting.ShouldSend(
            Agent(), Agent(), Now - AgentReporting.Reconfirm, Now));
    }

    // ── changes worth sending ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ EVERY FIELD IS PART OF THE ANSWER. A printer going offline is the reading somebody acts on,
    /// an agent version change is how a fleet upgrade is confirmed, and a printer being swapped is a
    /// change of hardware. Missing any of them would leave the portal confidently wrong.
    /// </summary>
    [Fact]
    public void An_agent_upgrade_is_reported() =>
        Assert.True(AgentReporting.ShouldSend(Agent(version: "1.4.0"), Agent(version: "1.3.3"), Now.AddMinutes(-1), Now));

    [Fact]
    public void A_printer_going_offline_is_reported_immediately() =>
        Assert.True(AgentReporting.ShouldSend(Agent(online: false), Agent(online: true), Now.AddMinutes(-1), Now));

    [Fact]
    public void A_printer_being_swapped_is_reported() =>
        Assert.True(AgentReporting.ShouldSend(Agent(printer: "Star TSP100"), Agent(printer: "EPSON TM-T20III"), Now.AddMinutes(-1), Now));

    /// <summary>⚠ And an agent DISAPPEARING is a change too — uninstalled, or stopped starting, which
    /// is exactly the fault that prompted this work.</summary>
    [Fact]
    public void An_agent_that_has_vanished_is_reported() =>
        Assert.True(AgentReporting.ShouldSend(AgentSnapshot.None, Agent(), Now.AddMinutes(-1), Now));

    /// <summary>⚠ …and an agent APPEARING, which is somebody finishing an install and wanting to see
    /// it land in the portal rather than in six hours.</summary>
    [Fact]
    public void An_agent_that_has_just_appeared_is_reported() =>
        Assert.True(AgentReporting.ShouldSend(Agent(), AgentSnapshot.None, Now.AddMinutes(-1), Now));

    // ── clocks ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ A CLOCK THAT JUMPED BACKWARDS MUST NOT MUTE THE TILL. Shop PCs skew — the platform ships a
    /// drift check for exactly that — and a naive `now - sentAt >= 6h` on a backwards jump yields a
    /// negative span, so the till would go quiet until the clock caught up. Days, potentially.
    /// </summary>
    [Fact]
    public void A_clock_that_jumped_backwards_does_not_mute_the_report() =>
        Assert.True(AgentReporting.ShouldSend(Agent(), Agent(), Now.AddHours(2), Now));

    [Fact]
    public void The_reconfirm_interval_matches_the_web_till() =>
        Assert.Equal(TimeSpan.FromHours(6), AgentReporting.Reconfirm);

    [Fact]
    public void A_null_snapshot_is_a_programming_error_not_a_reading() =>
        Assert.Throws<ArgumentNullException>(() => AgentReporting.ShouldSend(null!, null, null, Now));
}
