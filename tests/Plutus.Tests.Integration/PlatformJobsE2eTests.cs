using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP13.3 gate: the HMAC-signed job-report endpoint accepts a valid signature (and records a
/// JobRun) but rejects a bad one; the operator alerts feed is platform-admin only.
/// </summary>
public class PlatformJobsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public PlatformJobsE2eTests(PlutusAppFactory f) => _f = f;

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PlutusAppFactory.JobsSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    [Fact]
    public async Task Job_report_verifies_hmac_and_records_a_run()
    {
        var client = _f.CreateClient();
        var body = "{\"jobName\":\"backup\",\"status\":\"Succeeded\",\"detail\":\"nightly ok\"}";

        // bad signature → 401, nothing recorded
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/jobs/report"))
        {
            req.Headers.Add("X-Plutus-Signature", "deadbeef");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
        }

        // valid signature → 204
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/jobs/report"))
        {
            req.Headers.Add("X-Plutus-Signature", Sign(body));
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(req)).StatusCode);
        }

        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var run = await db.JobRuns.FirstOrDefaultAsync(r => r.JobName == "backup");
            Assert.NotNull(run);
            Assert.Equal(JobStatus.Succeeded, run.Status);
        }
    }

    [Fact]
    public async Task Jobs_grid_is_platform_admin_only_and_derives_cadence_status()
    {
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "jobs-grid-seed";
            var old = DateTime.UtcNow.AddHours(-3); // woo-poll cadence is 30m → "silent"
            db.JobRuns.Add(new JobRun
            {
                Id = Uuid7.New(), JobName = "woo-poll", TenantId = Guid.NewGuid(),
                StartedAtUtc = old, FinishedAtUtc = old, Status = JobStatus.Succeeded,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/jobs"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/jobs"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"jobName\":\"woo-poll\"", body);
            Assert.Contains("\"cadenceStatus\":\"silent\"", body);
        }
    }

    [Fact]
    public async Task Alerts_feed_is_platform_admin_only()
    {
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "jobs-e2e-seed";
            db.OperatorAlerts.Add(new OperatorAlert
            {
                Id = Uuid7.New(), AlertKey = "job-stall:woo-poll:seed", JobName = "woo-poll",
                Kind = "silent", Message = "seeded", RaisedAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow, Occurrences = 1,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        var operatorTok = PlutusAppFactory.OperatorToken("pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/alerts"))
        {
            req.Headers.Authorization = new("Bearer", operatorTok);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/alerts"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("job-stall:woo-poll:seed", await resp.Content.ReadAsStringAsync());
        }
    }
}
