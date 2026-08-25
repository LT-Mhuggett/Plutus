using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **`Device` has no global tenant filter, so every LISTING of it must scope by hand.**
///
/// ⚠⚠ THE FAULT THIS PINS. Matt, 2026-08-25: *"when I log into the portal as test business I still
/// see 5 active tills and 2 active stores. That looks like Kapow?"* It was. The portal dashboard
/// counted `_db.Devices.Where(Status == Active)` with no tenant predicate, and `Device` is **not** in
/// `MySqlDbContext.TenantOwned` — so it returned Kapow's five to a tenant that owns none.
///
/// ⚠ What made it convincing: every OTHER figure was right. `People`, `Stores`, `StockLocation` and
/// `WebStoreDetails` are all filtered, so users, stores, warehouses and webstores read correctly.
/// One unfiltered table among four filtered ones reads as a data problem, not a scoping one.
///
/// ⚠⚠ WHY `Device` IS NOT SIMPLY ADDED TO `TenantOwned` — and why this test is a tripwire rather
/// than the real fix. The device-auth paths look a device up by id with **no tenant context**, because
/// the caller *is* the device, presenting a secret rather than a tenant claim:
/// `SalesIngestService`, `HeartbeatController`, `CashModule`, `EnrolmentService`. A global filter
/// would resolve those to the fallback tenant and stop any non-Kapow till getting a token — an
/// estate-wide outage traded for a dashboard fix. Doing it properly means adding the filter AND
/// `IgnoreQueryFilters()` on each auth path, which is recorded in `Build/Platform Gaps.md`.
///
/// ⚠ This is a SOURCE SCAN and therefore a tripwire, not a proof. It cannot tell a correct predicate
/// from an incorrect one; it only notices when the predicate disappears entirely from a file whose
/// job is to report per-tenant figures.
/// </summary>
public class DeviceTenantScopeTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string Src(params string[] parts) =>
        Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());

    [Fact]
    public void Device_is_still_outside_the_global_tenant_filter()
    {
        // ⚠ If this ever fails, somebody has added Device to TenantOwned — which is the RIGHT
        // direction, but it must come with IgnoreQueryFilters() on the device-auth paths or every
        // non-Kapow till stops getting a token. Read the class comment before deleting this.
        var ctx = File.ReadAllText(Src("Plutus", "Commons", "Plutus.Entities", "MySqlDbContext.cs"));
        var owned = Regex.Match(ctx, @"TenantOwned\s*=\s*\{(.*?)\n\s*\};", RegexOptions.Singleline).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(owned), "could not find the TenantOwned list — this scan proves nothing.");

        Assert.DoesNotContain("typeof(Device)", owned);
    }

    [Fact]
    public void The_portal_dashboard_scopes_its_device_count_by_tenant()
    {
        var reports = File.ReadAllText(Src("src", "Plutus.Reporting", "ReportsController.cs"));

        // The dashboard's device query, and everything up to the end of that statement.
        var m = Regex.Match(reports, @"_db\.Devices\.AsNoTracking\(\)(.*?);", RegexOptions.Singleline);
        Assert.True(m.Success, "the dashboard's Devices query has moved — update this scan.");

        Assert.True(
            m.Value.Contains("TenantId"),
            "The dashboard counts Devices with NO tenant predicate. `Device` has no global query "
            + "filter, so this reports every tenant's tills to whoever is looking — it showed Kapow's "
            + "five to Test Business on 2026-08-25. Scope it with `d.TenantId == _tenant.TenantId` "
            + "(allowing Guid.Empty for an unscoped platform-admin).");
    }
}
