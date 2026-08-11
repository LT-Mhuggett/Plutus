using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed class SetTillReleaseBody
    {
        public string ExpectedMauiVersion { get; set; }
        public string ExpectedWebVersion { get; set; }
    }

    /// <summary>
    /// Which till build the platform expects — the reference point the heartbeat hands out.
    ///
    /// ⚠ Matt, 2026-08-11: *"Does the heartbeat from the till check for updates? All tills should do
    /// this."* It carried no version at all. This is where the answer is SET; `HeartbeatController`
    /// is where it is handed out, and `PlutusVersion.IsOlderThan` is how a till decides whether it
    /// is behind.
    ///
    /// ⚠ **A SETTING, NOT SOMETHING DERIVED**, and that was Matt's choice over "newest version any
    /// till has reported". Deriving it would start nagging forty tills the instant one machine ran a
    /// test build, and a rolled-back till would drag the bar backwards for everyone. Somebody
    /// decides when a release becomes the one you should be on.
    ///
    /// ⚠ **PLATFORM ADMIN ONLY.** A till build is the operator's release decision; a shop cannot pin
    /// itself to an old till, and one tenant's upgrade window is not another's to configure.
    ///
    /// ⚠ **ADVISORY. IT GATES NOTHING.** A till below the expected version still sells, still takes
    /// money, still drains its queue. There is no self-update for MAUI (Matt, same day) — it is an
    /// unpackaged .exe that cannot fetch its own replacement, so a gate would strand a shop with no
    /// route out. `426 Upgrade Required` stays deferred (WP5, risk #6) and would be a separate,
    /// deliberate decision.
    /// </summary>
    [ApiController]
    public sealed class TillReleaseController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        public TillReleaseController(MySqlDbContext db) => _db = db;

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/platform/till-release")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get()
        {
            var row = await _db.TillReleaseSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
            return Ok(new
            {
                expectedMauiVersion = row?.ExpectedMauiVersion ?? "",
                expectedWebVersion = row?.ExpectedWebVersion ?? "",
                updatedAtUtc = row?.UpdatedAtUtc,
                updatedBy = row?.UpdatedBy,
            });
        }

        /// <summary>
        /// Set the expected builds. **Blank clears it**, which turns the prompt off.
        ///
        /// ⚠ THE SHAPE IS VALIDATED, because a typo here is silent everywhere else: a value that
        /// `PlutusVersion.IsOlderThan` cannot parse makes the comparison answer "cannot say", so the
        /// banner simply never appears and whoever set it believes it is working. "1.46" is fine
        /// (it pads); "1.46.0-rc1", "latest" and "v1.46.0" are not, and are refused HERE rather
        /// than ignored silently on forty tills.
        /// </summary>
        [HttpPut("api/v1/platform/till-release")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Set([FromBody] SetTillReleaseBody body)
        {
            if (body == null) return BadRequest(new { detail = "A body is required." });

            var maui = (body.ExpectedMauiVersion ?? "").Trim();
            var web = (body.ExpectedWebVersion ?? "").Trim();

            foreach (var (value, name) in new[] { (maui, "ExpectedMauiVersion"), (web, "ExpectedWebVersion") })
            {
                if (value.Length == 0) continue;   // blank is how you turn it off

                // ⚠ Round-tripped through the REAL comparer rather than a regex of its own — a
                // second opinion about what a version looks like is how the two drift apart. If
                // `IsOlderThan` cannot tell that 0.0.1 is older than this, no till will be able to
                // either.
                if (!PlutusVersion.IsOlderThan("0.0.1", value) && value != "0.0.1")
                    return BadRequest(new
                    {
                        detail = $"{name} must look like 1.46.0 — digits and dots only, up to three parts. "
                               + "Blank turns the update prompt off.",
                    });
            }

            var row = await _db.TillReleaseSettings.FirstOrDefaultAsync(r => r.Id == 1);
            _db.CurrentUser = Actor.ToString();   // pitfall #1 — every save needs this
            if (row == null) _db.TillReleaseSettings.Add(row = new TillReleaseSettings { Id = 1 });

            row.ExpectedMauiVersion = maui;
            row.ExpectedWebVersion = web;
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = Actor.ToString();

            // ⚠ Audited. "Who told the estate it was out of date, and when" is exactly the question
            // asked after forty tills start showing a banner nobody expected.
            _db.Audit(Guid.Empty, Actor, "platform.till-release", nameof(TillReleaseSettings), "1",
                new { maui, web });

            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
