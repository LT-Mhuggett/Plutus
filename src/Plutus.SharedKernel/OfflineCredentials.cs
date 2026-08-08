using System;
using System.Collections.Generic;

namespace Plutus.SharedKernel;

// ─────────────────────────────────────────────────────────────────────────────
// HOW LONG MAY A TILL TRUST A CACHED CREDENTIAL?
//
// A till caches operator password hashes and permission sets so staff can sign in with the network
// down (retrofit plan §9 binding default 2). That cache has to expire, and picking the number is a
// three-way argument that a single value cannot win:
//
//   · KEEP SELLING.       A shop whose till refuses logins during an outage falls back to a cash
//                         tin and paper — which is a WORSE compliance event than a stale staff
//                         roster, because it produces no HMRC-attributable records at all.
//   · SHRINK THE THEFT.   A stolen till holds hashes at PBKDF2-SHA1/101,010 — roughly 13× below
//                         current OWASP guidance for that PRF. Those are the operators' PLATFORM
//                         passwords, and they work on the web till too.
//   · REACH THE LEAVER.   Nothing can be PUSHED to an offline till (risk #5). Expiry is the ONLY
//                         mechanism that ever revokes a dismissed employee's access on one, so the
//                         horizon IS the erasure SLA you can write into a DPA.
//
// THE RESOLUTION IS TO STOP ASKING FOR ONE NUMBER. Tier it by what the permission can do:
// ringing up sales is how a shop survives an outage, and it is worth almost nothing to a thief —
// the money lands in the ledger. Refunds, cash-out and price overrides are how a stolen till turns
// into cash, and they are exactly what a shop can live without for a few days.
//
// So: SELLING stays alive for a long time. MONEY-OUT expires quickly. The till NEVER hard-locks.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>How far a till may trust a cached credential, and for how long.</summary>
public enum OfflineTrust
{
    /// <summary>Within the money-out horizon: every cached permission applies.</summary>
    Full = 0,

    /// <summary>Past the money-out horizon, inside the sell horizon. The till KEEPS SELLING with
    /// the floor set; refunds, cash-out, overrides and admin are withdrawn until it reconnects.</summary>
    SellOnly = 1,

    /// <summary>Past the sell horizon. Offline sign-in is refused — the till needs a connection or
    /// a break-glass extension. ⚠ Reaching this at all means the fleet alerting failed weeks ago.</summary>
    Refused = 2,
}

/// <summary>
/// The horizons. Defaults are the recommendation; a tenant may tighten them, and the values live
/// in one place so the till, the portal warning and the DPA statement cannot disagree.
/// </summary>
/// <param name="MoneyOutMaxAge">Beyond this, only <see cref="OfflineCredentials.SellFloor"/> survives.</param>
/// <param name="SellMaxAge">Beyond this, offline sign-in is refused outright.</param>
/// <param name="WarnAfter">When the operator starts being told, so the warning precedes the loss.</param>
/// <param name="IdleLock">No input for this long locks the screen. ⚠ LOCK, never log out — see
/// <see cref="OfflineCredentials"/>.</param>
/// <param name="MaxSession">Absolute cap on one signed-in session, whatever the activity.</param>
public sealed record OfflineCredentialPolicy(
    TimeSpan MoneyOutMaxAge,
    TimeSpan SellMaxAge,
    TimeSpan WarnAfter,
    TimeSpan IdleLock,
    TimeSpan MaxSession)
{
    /// <summary>
    /// The recommended policy.
    ///
    /// <b>7 days for money-out</b> — long enough to cover the realistic worst case a UK shop hits
    /// (a Friday-night line fault on an end-of-next-working-day care level, over a bank holiday,
    /// is ~5 days) and short enough to state as an erasure SLA inside the UK GDPR Art 12(3)
    /// one-month window even if the request lands on day one of an outage.
    ///
    /// <b>30 days for selling</b> — because the alternative to a stale roster is a shop that cannot
    /// trade. It also covers the two cases that actually meet this boundary: the spare till from the
    /// cupboard, powered on the morning the main one dies, and a trade-stand or convention till that
    /// is offline for a planned week at a time.
    ///
    /// <b>3 days before warning</b> — a warning that first appears an hour before the cliff is
    /// decoration. The entire job of the banner is to get someone to plug the cable in.
    ///
    /// <b>15 minutes idle</b> — the PCI-DSS 8.2.8 figure, and right on its own merits for an
    /// unattended shop-floor device. It costs seconds because it locks rather than logging out.
    ///
    /// <b>12 hours absolute</b> — matches the server's own token TTL, and a session must never
    /// cross a business-day rollover or a Z-close (see <see cref="SessionExpiresAtUtc"/>): a shift
    /// change with no re-auth records Bob's sales against Alice, which is the "every sale
    /// attributable to a named operator" requirement failing silently.
    /// </summary>
    public static readonly OfflineCredentialPolicy Default = new(
        MoneyOutMaxAge: TimeSpan.FromDays(7),
        SellMaxAge: TimeSpan.FromDays(30),
        WarnAfter: TimeSpan.FromDays(3),
        IdleLock: TimeSpan.FromMinutes(15),
        MaxSession: TimeSpan.FromHours(12));
}

/// <summary>What the till decided, and what to tell the person standing in front of it.</summary>
public sealed record OfflineAssessment(
    OfflineTrust Trust,
    TimeSpan Age,
    bool ShouldWarn,
    string Message)
{
    public bool MaySignIn => Trust != OfflineTrust.Refused;
}

/// <summary>
/// The staleness rule, in one place. ⚠ Both halves of a till must agree on this — the login screen
/// that decides who gets in and the permission gate that decides what they may do — and so must the
/// portal, which has to explain the same boundary to a manager reading a fleet list.
/// </summary>
public static class OfflineCredentials
{
    /// <summary>
    /// What a stale-but-not-expired till keeps. ⚠ DEFAULT-DENY: this is an allow-list, so a
    /// permission added to the catalogue later is withdrawn when stale until someone deliberately
    /// puts it here. A new permission that silently survived staleness would be the exact bug this
    /// tiering exists to prevent.
    ///
    /// <see cref="PermissionCatalogue.SupportTickets"/> is in the floor for the same reason it is
    /// seeded to every built-in role: a lone cashier on a broken till must be able to shout for
    /// help, and that is doubly true when the thing that is broken is the connection.
    /// </summary>
    public static readonly IReadOnlySet<string> SellFloor = new HashSet<string>(StringComparer.Ordinal)
    {
        PermissionCatalogue.PosSell,
        PermissionCatalogue.SupportTickets,
    };

    /// <summary>Would this permission still work on a till that has been offline past the money-out
    /// horizon? Everything outside <see cref="SellFloor"/> is withdrawn.</summary>
    public static bool SurvivesStaleness(string permissionCode) =>
        permissionCode != null && SellFloor.Contains(permissionCode);

    /// <summary>
    /// Assess a cached credential.
    /// </summary>
    /// <param name="syncedAtUtc">When this operator's record last came down from the server —
    /// <c>LocalOperator.UpdatedAtUtc</c>. ⚠ Not when the till last talked to the server for any
    /// reason: the question is how old the ROSTER is, and a catalogue sync does not refresh it.</param>
    public static OfflineAssessment Assess(DateTime syncedAtUtc, DateTime nowUtc, OfflineCredentialPolicy? policy = null)
    {
        var p = policy ?? OfflineCredentialPolicy.Default;

        // A clock that has gone backwards (or a record stamped in the future) must not read as
        // "fresh forever" — clamp to zero and treat it as current, because refusing to let staff in
        // over a wrong clock fails in the one direction that stops a shop trading.
        var age = nowUtc - syncedAtUtc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;

        var days = (int)Math.Floor(age.TotalDays);

        if (age > p.SellMaxAge)
            return new OfflineAssessment(OfflineTrust.Refused, age, true,
                $"This till hasn't reached Plutus for {days} days and can't verify staff logins any more. " +
                "Connect it to the internet, or ask a manager for a temporary code.");

        if (age > p.MoneyOutMaxAge)
            return new OfflineAssessment(OfflineTrust.SellOnly, age, true,
                $"Offline for {days} days. Sales work normally — refunds, cash out and manager " +
                "functions need a connection.");

        if (age > p.WarnAfter)
            return new OfflineAssessment(OfflineTrust.Full, age, true,
                $"Offline for {days} days. Reconnect soon to refresh staff logins — refunds and " +
                $"manager functions stop working after {p.MoneyOutMaxAge.TotalDays:0} days.");

        return new OfflineAssessment(OfflineTrust.Full, age, false, "");
    }

    /// <summary>
    /// When a session must end, whatever the operator is doing: the earliest of the absolute cap
    /// and the business-day rollover.
    ///
    /// ⚠ The rollover is the load-bearing half. A session spanning two business days leaks
    /// yesterday's operator into today's X/Z breakdown, and a shift change with no re-auth attributes
    /// the incoming person's sales to the outgoing one — silently, and in exactly the records HMRC
    /// would ask about.
    /// </summary>
    /// <param name="businessDayEndsAtUtc">The till's own rollover instant. Deliberately the TILL's
    /// wall-clock day, not UTC midnight — a business day is a shop's day (see the business-day rule
    /// in till-design Part C).</param>
    public static DateTime SessionExpiresAtUtc(
        DateTime signedInAtUtc, DateTime businessDayEndsAtUtc, OfflineCredentialPolicy? policy = null)
    {
        var cap = signedInAtUtc + (policy ?? OfflineCredentialPolicy.Default).MaxSession;
        return businessDayEndsAtUtc < cap ? businessDayEndsAtUtc : cap;
    }
}
