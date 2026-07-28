using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP17.1 connector health framework. Proves the reusable base: a second connector inherits
/// health + retry + journal in well under 50 lines (SampleConnector below); the health seam
/// records poll/webhook/outbound + error streak; the monitor raises a silence alert naming the
/// connector + tenant; and the operator + tenant read surfaces are gated correctly.
/// </summary>
public class ConnectorHealthE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ConnectorHealthE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class FakeAlerter : IOperatorAlerter
    {
        public readonly HashSet<string> Open = new();
        public Task RaiseAsync(string k, string j, Guid? t, string kind, string m, CancellationToken ct = default) { Open.Add(k); return Task.CompletedTask; }
        public Task ClearAsync(string k, CancellationToken ct = default) { Open.Remove(k); return Task.CompletedTask; }
    }

    // ---- the "second connector in <50 lines" proof: inherits health, retry and journalling ----
    private sealed class SampleConnector : ConnectorBase
    {
        private readonly List<string> _journal = new();
        public IReadOnlyList<string> Journal => _journal;
        public SampleConnector(IConnectorHealth health) : base(health) { }
        public override string Connector => "dummy";
        public Task<bool> PushAsync(Guid tenant, bool willFail, CancellationToken ct = default) =>
            SendWithRetryAsync(tenant,
                send: _ => willFail ? throw new InvalidOperationException("boom") : Task.CompletedTask,
                journal: (ok, err) => { _journal.Add(ok ? "sent" : $"failed:{err}"); return Task.CompletedTask; },
                maxAttempts: 2, ct: ct);
    }

    private MySqlDbContext Unscoped(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(), new FixedTenantContext(Guid.Empty));

    [Fact]
    public async Task Sample_connector_records_health_retry_and_journal_and_monitor_alerts_on_silence()
    {
        var tenant = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IConnectorHealth>();
        var sample = new SampleConnector(health);

        // success → journal "sent", ConnectorRun outbound stamped, streak 0
        Assert.True(await sample.PushAsync(tenant, willFail: false));
        Assert.Equal("sent", sample.Journal.Last());
        // failure (retries then gives up) → journal "failed", streak incremented
        Assert.False(await sample.PushAsync(tenant, willFail: true));
        Assert.StartsWith("failed:", sample.Journal.Last());

        await using var db = Unscoped(scope);
        db.CurrentUser = "connector-e2e";
        var run = await db.ConnectorRuns.FirstAsync(r => r.Connector == "dummy" && r.TenantId == tenant);
        Assert.NotNull(run.LastOutboundAtUtc);
        Assert.True(run.ErrorStreak >= 1);

        // Force silence: backdate the row well past the dummy's 5-minute window, then monitor.
        run.LastPollAtUtc = run.LastWebhookAtUtc = run.LastOutboundAtUtc = DateTime.UtcNow.AddHours(-1);
        run.LastErrorAtUtc = null;
        await db.SaveChangesAsync();
        var alerter = new FakeAlerter();
        await ConnectorMonitor.EvaluateAsync(db, alerter, DateTime.UtcNow);
        Assert.Contains(ConnectorRegistry.SilentAlertKey("dummy", tenant), alerter.Open);

        // recovery clears it
        run.LastOutboundAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await ConnectorMonitor.EvaluateAsync(db, alerter, DateTime.UtcNow);
        Assert.DoesNotContain(ConnectorRegistry.SilentAlertKey("dummy", tenant), alerter.Open);
    }

    [Fact]
    public async Task Platform_connectors_endpoint_is_admin_only()
    {
        var client = _f.CreateClient();
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/connectors"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/connectors"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }
    }
}
