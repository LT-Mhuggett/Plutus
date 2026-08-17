using System;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Whether to offer an agent update (W5), against Matt's ruling of 2026-08-17:
/// *"I would not install silently, I would inform with a 'Continue or cancel' option… But if they say
/// no, it needs to remind them."*
///
/// ⚠ The third requirement — **remind** — is the only one that needs state, and therefore the one a
/// later change is most likely to drop. Most of these tests are about it.
/// </summary>
public class AgentUpdatePromptTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 14, 0, 0, DateTimeKind.Utc);

    private static AgentUpdateAction Decide(
        string? installed = "1.3.3",
        string? offered = "1.4.0",
        string? declinedVersion = null,
        DateTime? declinedAt = null,
        bool basketIsOpen = false) =>
        AgentUpdatePrompt.Decide(installed, offered, declinedVersion, declinedAt, Now, basketIsOpen);

    // ── ask ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ A newer agent, nobody asked yet, no sale in progress — ask. This is the whole
    /// feature.</summary>
    [Fact]
    public void A_newer_agent_is_offered() => Assert.Equal(AgentUpdateAction.Ask, Decide());

    // ── nothing to do ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.4.0", "1.4.0")]   // same
    [InlineData("1.5.0", "1.4.0")]   // ⚠ the till carries an OLDER agent — never offer a downgrade
    public void An_equal_or_older_offer_is_not_an_update(string installed, string offered) =>
        Assert.Equal(AgentUpdateAction.Nothing, Decide(installed: installed, offered: offered));

    /// <summary>
    /// ⚠⚠ NO AGENT IS NOT AN UPDATE. "Install one" is a different sentence to the operator and a
    /// different decision; offering to *update* a till that has never had an agent reads as a fault.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_till_with_no_agent_is_not_offered_an_update(string? installed) =>
        Assert.Equal(AgentUpdateAction.Nothing, Decide(installed: installed));

    /// <summary>⚠ And a till that cannot say what it carries must not invent an update.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void A_till_that_cannot_name_its_own_agent_offers_nothing(string? offered) =>
        Assert.Equal(AgentUpdateAction.Nothing, Decide(offered: offered));

    /// <summary>
    /// ⚠⚠ NEVER MID-SALE. A modal over a part-rung basket is how the 2026-08-13 checkout deadlock
    /// presented, and an operator with a customer waiting dismisses anything to make it go away —
    /// which turns the prompt back into the silent install it exists to prevent.
    /// </summary>
    [Fact]
    public void An_open_basket_is_never_interrupted() =>
        Assert.Equal(AgentUpdateAction.Nothing, Decide(basketIsOpen: true));

    // ── the reminder ──────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ A fresh "no" is respected — the till does not re-ask on the next beat. Asking every
    /// 60 seconds IS a silent install with extra steps.</summary>
    [Fact]
    public void A_recent_decline_is_respected() =>
        Assert.Equal(AgentUpdateAction.Nothing,
            Decide(declinedVersion: "1.4.0", declinedAt: Now.AddMinutes(-30)));

    /// <summary>⚠⚠ BUT IT COMES BACK. *"If they say no, it needs to remind them."* — this is that
    /// sentence, and without it a till stays on an old agent for ever and never says so.</summary>
    [Fact]
    public void A_stale_decline_is_asked_again()
    {
        Assert.Equal(AgentUpdateAction.Nothing,
            Decide(declinedVersion: "1.4.0", declinedAt: Now - AgentUpdatePrompt.RemindAfter + TimeSpan.FromMinutes(1)));

        Assert.Equal(AgentUpdateAction.Ask,
            Decide(declinedVersion: "1.4.0", declinedAt: Now - AgentUpdatePrompt.RemindAfter));
    }

    /// <summary>
    /// ⚠⚠ A DECLINE IS PER VERSION. Saying no to 1.4.0 is not saying no to 1.5.0 — treating it as
    /// blanket would let one "not now" suppress every future update on that till, permanently and
    /// invisibly, which is the worst outcome available here.
    /// </summary>
    [Fact]
    public void Declining_one_version_does_not_decline_the_next()
    {
        Assert.Equal(AgentUpdateAction.Ask,
            Decide(offered: "1.5.0", declinedVersion: "1.4.0", declinedAt: Now.AddMinutes(-1)));
    }

    /// <summary>
    /// ⚠⚠ A DECLINE WITH NO TIMESTAMP IS NOT A DECLINE FOR EVER. If the stamp was never written or has
    /// been lost, ask — the failure that costs a shop is an agent that never updates and never
    /// mentions it, not one that asks twice.
    /// </summary>
    [Fact]
    public void A_decline_with_no_timestamp_asks_again() =>
        Assert.Equal(AgentUpdateAction.Ask, Decide(declinedVersion: "1.4.0", declinedAt: null));

    /// <summary>
    /// ⚠ A clock that jumped BACKWARDS must not mute the reminder. Otherwise it waits for the clock
    /// rather than the interval — possibly for days — and shop-PC skew is common enough that this
    /// platform ships a drift check for it.
    /// </summary>
    [Fact]
    public void A_backwards_clock_does_not_mute_the_reminder() =>
        Assert.Equal(AgentUpdateAction.Ask,
            Decide(declinedVersion: "1.4.0", declinedAt: Now.AddHours(2)));

    // ── versions as they really arrive ────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The artefacts carry an informational suffix — `PlutusVersion.Of` produces
    /// "1.4.0+6e4e787e…". Comparing that whole string would never match, so the suffix is trimmed.
    /// </summary>
    [Fact]
    public void An_informational_version_suffix_is_tolerated()
    {
        Assert.Equal(AgentUpdateAction.Nothing,
            Decide(installed: "1.4.0+6e4e787e", offered: "1.4.0+6e4e787e"));

        Assert.Equal(AgentUpdateAction.Ask,
            Decide(installed: "1.3.3+abc123", offered: "1.4.0+def456"));
    }

    // ── what the operator reads ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ BOTH VERSIONS, and what it touches. "An update is available" gives somebody nothing to weigh
    /// — and this dialog's real job is making a later printer fault attributable to the change.
    /// </summary>
    [Fact]
    public void The_prompt_names_both_versions_and_what_it_affects()
    {
        var message = AgentUpdatePrompt.PromptMessage("1.3.3", "1.4.0");

        Assert.Contains("1.3.3", message);
        Assert.Contains("1.4.0", message);
        Assert.Contains("printer", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⚠ And a decline says it will return, so nobody assumes they have switched it off.</summary>
    [Fact]
    public void Declining_says_it_will_ask_again() =>
        Assert.Contains("again", AgentUpdatePrompt.DeclinedMessage(), StringComparison.OrdinalIgnoreCase);
}
