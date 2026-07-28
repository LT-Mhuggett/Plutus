#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>
    /// WP13.3 operator jobs surface: an HMAC-signed report endpoint (so out-of-process jobs like
    /// the nightly launchd backup can heartbeat in — same scheme as the billing webhook) and the
    /// platform-admin alerts feed the dashboard reads. JobRuns/OperatorAlerts are global tables.
    /// </summary>
    [ApiController]
    public sealed class PlatformJobsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly IConfiguration _config;

        public PlatformJobsController(MySqlDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>Out-of-process job heartbeat. Anonymous but HMAC-SHA256 verified over the raw
        /// body (header X-Plutus-Signature, secret JOBS_REPORT_SECRET). Records a point-in-time
        /// JobRun so the cadence monitor sees the job as alive.</summary>
        [HttpPost("api/v1/platform/jobs/report")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Report()
        {
            var secret = _config["JOBS_REPORT_SECRET"];
            if (string.IsNullOrEmpty(secret)) return StatusCode(503, "Job reporting is not configured.");

            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var signature = Request.Headers["X-Plutus-Signature"].ToString();
            if (!VerifyHmac(secret, body, signature)) return Unauthorized();

            string jobName = null, statusStr = "Succeeded", detail = null;
            Guid? tenantId = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                jobName = root.TryGetProperty("jobName", out var j) ? j.GetString() : null;
                if (root.TryGetProperty("status", out var s)) statusStr = s.GetString();
                if (root.TryGetProperty("detail", out var d)) detail = d.GetString();
                if (root.TryGetProperty("tenantId", out var t) && Guid.TryParse(t.GetString(), out var tid)) tenantId = tid;
            }
            catch { return BadRequest(new { detail = "Malformed JSON body." }); }

            if (string.IsNullOrWhiteSpace(jobName)) return BadRequest(new { detail = "jobName is required." });
            var status = Enum.TryParse<JobStatus>(statusStr, ignoreCase: true, out var st) ? st : JobStatus.Succeeded;

            _db.CurrentUser = "jobs-report";
            var now = DateTime.UtcNow;
            _db.JobRuns.Add(new JobRun
            {
                Id = Uuid7.New(), JobName = jobName.Trim(), TenantId = tenantId,
                StartedAtUtc = now, FinishedAtUtc = now, Status = status, Detail = detail,
            });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>WP13.4 Jobs grid: the latest run per (JobName, TenantId) with a derived cadence
        /// status — "failed" (last run failed), "silent" (past its max-silence window), or "ok".</summary>
        [HttpGet("api/v1/platform/jobs")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Jobs()
        {
            var now = DateTime.UtcNow;
            var runs = await _db.JobRuns.AsNoTracking().ToListAsync();
            var latest = runs
                .GroupBy(r => (r.JobName, r.TenantId))
                .Select(g => g.OrderByDescending(r => r.StartedAtUtc).First())
                .Select(r =>
                {
                    var reference = r.FinishedAtUtc ?? r.StartedAtUtc;
                    string status;
                    if (r.Status == JobStatus.Failed) status = "failed";
                    else if (JobCadence.MaxSilence.TryGetValue(r.JobName, out var w) && now - reference > w) status = "silent";
                    else status = "ok";
                    return new
                    {
                        jobName = r.JobName, tenantId = r.TenantId, runStatus = r.Status.ToString(),
                        startedAtUtc = r.StartedAtUtc, finishedAtUtc = r.FinishedAtUtc, detail = r.Detail,
                        cadenceStatus = status,
                    };
                })
                .OrderBy(x => x.cadenceStatus == "ok").ThenBy(x => x.jobName).ThenBy(x => x.tenantId)
                .ToList();
            return Ok(latest);
        }

        /// <summary>The operator alerts feed. Open alerts by default; ?includeCleared=true for the
        /// full recent history.</summary>
        [HttpGet("api/v1/platform/alerts")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Alerts([FromQuery] bool includeCleared = false)
        {
            var q = _db.OperatorAlerts.AsNoTracking();
            if (!includeCleared) q = q.Where(a => a.ClearedAtUtc == null);
            return Ok(await q.OrderByDescending(a => a.LastSeenAtUtc)
                .Select(a => new
                {
                    a.AlertKey, a.JobName, tenantId = a.TenantId, a.Kind, a.Message,
                    a.RaisedAtUtc, a.LastSeenAtUtc, a.ClearedAtUtc, a.Occurrences,
                })
                .ToListAsync());
        }

        private static bool VerifyHmac(string secret, string payload, string signature)
        {
            if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signature)) return false;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var provided = signature.Trim().ToLowerInvariant();
            return expected.Length == provided.Length &&
                   CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided));
        }
    }
}
