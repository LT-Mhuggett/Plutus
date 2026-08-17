using System;

namespace Plutus.Client.Core;

/// <summary>What the till should do about an agent update, right now.</summary>
public enum AgentUpdateAction
{
    /// <summary>Say nothing. Up to date, mid-sale, or recently declined.</summary>
    Nothing = 0,

    /// <summary>Ask — Continue or Cancel. ⚠ Never install without this.</summary>
    Ask = 1,
}

/// <summary>
/// Whether to offer the operator an agent update (W5).
///
/// ⚠⚠ MATT'S RULING, 2026-08-17, verbatim: *"I would not install silently, I would inform with a
/// 'Continue or cancel' option, do not want to be doing things when nobody knows. But if they say no,
/// it needs to remind them."*
///
/// Three requirements, and the third is the one that needs state and is therefore the one most likely
/// to be dropped:
///
/// | | |
/// |---|---|
/// | **Ask** | A Continue/Cancel prompt naming what is about to happen. Never a background swap |
/// | **Obey a no** | The current agent keeps running, and the till does **not** re-ask on a loop — asking every 60 s *is* a silent install with extra steps |
/// | **Remind** | A declined update **comes back**. "Not now" during a rush is not "never" |
///
/// ⚠ THE REASON FOR THE PROMPT IS DIAGNOSTIC, NOT COURTESY. Matt's *"do not want to be doing things
/// when nobody knows"* is the operative half: a printer that stops working right after a silent agent
/// swap is un-attributable, and somebody spends an afternoon on the printer instead of on the change
/// that caused it. A prompt makes the connection obvious to whoever was standing there.
/// </summary>
public static class AgentUpdatePrompt
{
    /// <summary>
    /// How long a "no" is respected before asking again.
    ///
    /// ⚠ FOUR HOURS is chosen to be revisited within the same trading day but not within the same
    /// queue of customers. Shorter turns the reminder into nagging, and an operator who is nagged
    /// learns to dismiss the dialog without reading it — at which point the prompt has become the
    /// silent install Matt ruled against, with an extra click.
    /// </summary>
    public static readonly TimeSpan RemindAfter = TimeSpan.FromHours(4);

    /// <summary>
    /// Decide.
    /// </summary>
    /// <param name="installedVersion">What the agent reports, or null when there is no agent. ⚠ No
    /// agent means nothing to UPDATE — installing one is a separate decision and not this one's.</param>
    /// <param name="offeredVersion">What the till is carrying, from its own package.</param>
    /// <param name="declinedVersion">The version the operator last said no to, or null.</param>
    /// <param name="declinedAtUtc">When they said it. ⚠ Must be PERSISTED — see below.</param>
    /// <param name="basketIsOpen">⚠ Never interrupt a sale. `TillCadence.BasketIsOpen` already
    /// exists for exactly this, and it is the same reason a catalogue sync is suppressed mid-basket.</param>
    public static AgentUpdateAction Decide(
        string? installedVersion,
        string? offeredVersion,
        string? declinedVersion,
        DateTime? declinedAtUtc,
        DateTime nowUtc,
        bool basketIsOpen)
    {
        // ⚠ NOTHING TO OFFER. A till that cannot say what it carries must not invent an update.
        if (!TryParse(offeredVersion, out var offered)) return AgentUpdateAction.Nothing;

        // ⚠ NO AGENT AT ALL IS NOT AN UPDATE. "Install one" is a different sentence to the operator
        // and a different decision — conflating them would offer to "update" a till that has never
        // had an agent, which reads as a fault.
        if (!TryParse(installedVersion, out var installed)) return AgentUpdateAction.Nothing;

        // ⚠ `>=`, so a till carrying an OLDER agent than the one installed never offers a downgrade.
        // That happens the moment somebody runs an older till build against an updated agent.
        if (offered <= installed) return AgentUpdateAction.Nothing;

        // ⚠⚠ NEVER MID-SALE. A modal over a part-rung basket is how the 2026-08-13 checkout deadlock
        // presented, and an operator with a customer waiting will dismiss anything to get rid of it.
        if (basketIsOpen) return AgentUpdateAction.Nothing;

        // ⚠⚠ A DECLINE APPLIES TO THE VERSION THAT WAS DECLINED, not to updates in general. Saying no
        // to 1.4.0 is not saying no to 1.5.0 — treating it as blanket would let one "not now" suppress
        // every future update on that till, permanently and invisibly.
        if (!TryParse(declinedVersion, out var declined) || declined != offered)
            return AgentUpdateAction.Ask;

        // ⚠⚠ AND A DECLINE WITH NO TIMESTAMP IS NOT A DECLINE FOR EVER. If the stamp is missing —
        // never written, or lost — ask again rather than stay quiet: the failure that costs a shop is
        // an agent that never updates and never says so.
        if (declinedAtUtc is not DateTime declinedAt) return AgentUpdateAction.Ask;

        // ⚠ `nowUtc < declinedAt` catches a clock that jumped BACKWARDS, which would otherwise make
        // the reminder wait for the clock rather than the interval — and shop-PC skew is common enough
        // that this platform ships a drift check for it.
        return nowUtc - declinedAt >= RemindAfter || nowUtc < declinedAt
            ? AgentUpdateAction.Ask
            : AgentUpdateAction.Nothing;
    }

    /// <summary>
    /// What to put in front of the operator.
    ///
    /// ⚠ It names BOTH versions and says what the update touches. "An update is available" tells
    /// somebody nothing they can weigh, and this is the dialog that has to make a later printer fault
    /// attributable to the change.
    /// </summary>
    public static string PromptMessage(string installedVersion, string offeredVersion) =>
        $"The receipt-printer helper on this PC can be updated from {installedVersion} to "
        + $"{offeredVersion}.\n\nIt takes a few seconds and the printer will be briefly unavailable. "
        + "Nothing else about the till changes.";

    /// <summary>⚠ Said after a decline, so the operator knows it will return rather than assuming
    /// they have turned it off.</summary>
    public static string DeclinedMessage() =>
        "Left as it is. The till will ask again later.";

    private static bool TryParse(string? version, out Version parsed)
    {
        parsed = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(version)) return false;

        // ⚠ Tolerates the informational suffix the artefacts carry — "1.4.0+6e4e787e" is what
        // `PlutusVersion.Of` produces, and comparing that as a whole string would never match.
        var cleaned = version.Trim();
        var plus = cleaned.IndexOf('+');
        if (plus > 0) cleaned = cleaned[..plus];

        return Version.TryParse(cleaned, out parsed!);
    }
}
