using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **THE TENANCY INVARIANT: if a row belongs to a tenant, the model must say so.**
///
/// ⚠⚠ WHY THIS EXISTS. On 2026-08-25 three separate cross-tenant faults surfaced in one day, each by
/// a different mechanism, and none of them was caught by a test:
///
///   1. an impersonating operator resolved to `Guid.Empty` and saw EVERY tenant
///      (`HttpTenantContext` checked platform-admin before `tid`);
///   2. the portal dashboard counted every tenant's tills, because `Device` carries a `TenantId`
///      and was never added to `TenantOwned`, so it has no query filter;
///   3. a non-Kapow user's login token came out holding `pos.sell` alone, because the assignment
///      lookup IS filtered and login is anonymous, so it resolved against the fallback tenant.
///
/// ⚠ They share one shape: **the scoping is decided in a place a reader has to go and check.** A
/// `TenantId` column looks like protection; it is inert without a filter, and the filter lives in a
/// hand-maintained list two thousand lines away.
///
/// ⚠ This test asks **EF's own model** rather than the source or the database, because that is the
/// only place that knows the table, the shadow properties and the query filter together. Guessing at
/// the mapping between CLR names and table names is precisely how `Device` was missed.
/// </summary>
public class TenancyInvariantTests
{
    /// <summary>
    /// ⚠⚠ THE DELIBERATE EXCEPTIONS, AND EVERY ONE NEEDS A REASON HERE.
    ///
    /// Adding a name to this list is a decision to scope that entity BY HAND at every call site
    /// forever. If you are adding one, say why, and say what enforces the hand-scoping — otherwise
    /// put it in `TenantOwned` instead.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        // ── looked up BEFORE a tenant is known, so a filter would break the lookup ──────────────
        ["EnrolmentCode"] =
            "Resolved BY CODE by a till that has no identity yet — the code is what tells you which "
            + "tenant it is. Filtering it would make enrolment impossible for every tenant but the "
            + "fallback one.",
        ["PasswordResetToken"] =
            "Resolved BY TOKEN from an anonymous request, for the same reason. The token is the "
            + "credential and is globally unique.",
        ["DpaAcceptance"] =
            "Signup runs before a tenant exists. Deliberately global — same precedent as "
            + "EnrolmentCode, recorded when WP-SIGNUP was built.",
        ["Device"] =
            "The device-auth paths look a device up by id with NO tenant context, because the caller "
            + "IS the device presenting a secret rather than a tenant claim (SalesIngestService, "
            + "HeartbeatController, CashModule, EnrolmentService). A global filter would resolve "
            + "those to the fallback tenant and stop every non-Kapow till getting a token. Listings "
            + "are scoped by hand and pinned by DeviceTenantScopeTests; the proper fix (filter + "
            + "IgnoreQueryFilters on each auth path) is in Build/Platform Gaps.md.",

        // ── the operator's own surfaces: cross-tenant BY DESIGN ────────────────────────────────
        ["TenantContract"] = "Platform billing. The operator console reads every tenant's contract.",
        ["TenantEntitlementOverride"] = "Platform entitlements, set BY the operator across tenants.",
        ["TenantSignal"] = "Operator alerting (dpa-missing and friends) — the point is the estate view.",
        ["OperatorAlert"] = "Same: the operator's alert queue spans tenants.",
        ["DeletionSchedule"] = "Tenant lifecycle, driven by the operator, not by a tenant.",

        // ── background workers with no request and therefore no tenant ─────────────────────────
        ["OutboxEvent"] =
            "Drained by a hosted dispatcher with no HTTP context. A filter would resolve to the "
            + "fallback tenant and silently stop dispatching every other tenant's events.",
        ["MessageEvent"] = "Notification outbox, drained by the same kind of background pass.",
        ["JobRun"] = "Job heartbeats, written and read by background services and the operator console.",
        ["ConnectorRun"] = "Connector health, same shape as JobRun.",

        // ── ⚠ UNTRIAGED — passing here is NOT a judgement that these are safe ──────────────────
        ["MemberNoCounter"] =
            "⚠ UNTRIAGED. A per-tenant counter for member numbers. If it is read unfiltered, two "
            + "tenants could advance or collide on one counter — member numbers are supposed to be "
            + "unique per tenant. No `_db.MemberNoCounters` call site was found in the audit, so it "
            + "is reached some other way and needs eyes. Recorded in Build/Platform Gaps.md.",
        ["TenantSendingIdentity"] =
            "⚠ UNTRIAGED. Per-tenant email sending identities. Plausibly operator-managed like the "
            + "other Tenant* rows, but not verified. Recorded in Build/Platform Gaps.md.",
    };

    private static MySqlDbContext Ctx()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        return new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options);
    }

    /// <summary>
    /// Is this entity covered by a filter — its own, or one inherited from its hierarchy root?
    ///
    /// ⚠ THE ROOT WALK IS NOT A DETAIL. EF only allows a query filter on the root of an inheritance
    /// hierarchy, so `Employee` (TPT-derived from `Person`) carries a TenantId and no filter of its
    /// own, and is nonetheless perfectly scoped — `Person` is in `TenantOwned` and the filter
    /// cascades. Without this walk the test reports `Employee` as a leak, which would be a false
    /// alarm about the People table, and a false alarm in a security test is how the real ones stop
    /// being read.
    /// </summary>
    private static bool IsCovered(Microsoft.EntityFrameworkCore.Metadata.IEntityType e)
    {
        for (var t = e; t != null; t = t.BaseType)
            if (t.GetQueryFilter() != null) return true;
        return false;
    }

    /// <summary>Entities that carry a TenantId (real or shadow) but have NO query filter.</summary>
    private static string[] UnfilteredTenantEntities(MySqlDbContext db) =>
        db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") != null)
            .Where(e => !IsCovered(e))
            .Select(e => e.ClrType.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Every_entity_with_a_TenantId_is_filtered_or_explicitly_exempt()
    {
        using var db = Ctx();
        var offenders = UnfilteredTenantEntities(db).Where(n => !Exempt.ContainsKey(n)).ToArray();

        Assert.True(
            offenders.Length == 0,
            "These entities carry a TenantId but have NO global query filter, so every query on them "
            + "returns EVERY TENANT'S ROWS unless the caller remembered to scope it by hand:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nAdd them to MySqlDbContext.TenantOwned, or — if they genuinely cannot be filtered "
            + "— add them to the Exempt list in this test WITH A REASON and scope every listing by "
            + "hand. A TenantId column is not protection; the filter is.");
    }

    /// <summary>
    /// ⚠ The exemption list must not rot. If an entity is exempt here but has since been filtered,
    /// the entry is misleading — it tells the next reader to hand-scope something that no longer
    /// needs it, and hand-scoping an already-filtered query is how you get an empty result.
    /// </summary>
    [Fact]
    public void The_exemption_list_contains_nothing_that_is_already_filtered()
    {
        using var db = Ctx();
        var actuallyUnfiltered = UnfilteredTenantEntities(db).ToHashSet(StringComparer.Ordinal);

        var stale = Exempt.Keys.Where(k => !actuallyUnfiltered.Contains(k)).ToArray();

        Assert.True(stale.Length == 0,
            "These are listed as exempt from tenant filtering but ARE filtered now — remove them "
            + "from the list: " + string.Join(", ", stale));
    }

    /// <summary>
    /// ⚠ The reverse direction: a name in `TenantOwned` that has no `TenantId` to filter on is a
    /// typo that silently does nothing. EF would happily build a filter over a shadow property it
    /// creates on demand, so this catches the entry that looks right and protects nothing.
    /// </summary>
    [Fact]
    public void Every_filtered_entity_really_has_a_TenantId_to_filter_on()
    {
        using var db = Ctx();
        var filteredWithoutColumn = db.Model.GetEntityTypes()
            .Where(e => e.GetQueryFilter() != null)
            .Where(e => e.FindProperty("TenantId") == null)
            .Select(e => e.ClrType.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(filteredWithoutColumn.Length == 0,
            "These have a query filter but no TenantId property: " + string.Join(", ", filteredWithoutColumn));
    }

    /// <summary>
    /// A named guard for the one that actually leaked, so the failure message tells the story rather
    /// than making somebody re-derive it.
    /// </summary>
    [Fact]
    public void Till_and_Store_are_filtered_because_the_portal_counts_them()
    {
        using var db = Ctx();
        foreach (var name in new[] { "Till", "Store", "StoreDetails", "RbacRoleAssignment" })
        {
            var e = db.Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == name);
            Assert.True(e != null, $"{name} is not in the model — this guard is checking nothing.");
            Assert.True(e!.GetQueryFilter() != null,
                $"{name} lost its tenant query filter. The portal counts it per tenant; without the "
                + "filter it reports the whole estate to whoever is looking.");
        }
    }
}
