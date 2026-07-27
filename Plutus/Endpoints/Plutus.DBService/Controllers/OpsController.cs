using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Infrastructure.Outbox;
using Plutus.SharedKernel;

namespace Plutus.DBService.Controllers
{
    /// <summary>T1.5 platform-admin ops surface: parked (dead-lettered) outbox events + per-
    /// consumer lag. Read-only.</summary>
    [ApiController]
    [Route("api/v1/ops")]
    public sealed class OpsController : ControllerBase
    {
        private readonly RepositoryContext _repo;

        public OpsController(RepositoryContext repo) => _repo = repo;

        [HttpGet("deadletters")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        public async Task<IActionResult> DeadLetters([FromQuery] int take = 200)
        {
            if (_repo is not MySqlDbContext db)
                return Ok(new { deadLetters = System.Array.Empty<object>(), lag = System.Array.Empty<object>() });

            var deadLetters = await db.ConsumerDeadLetters
                .AsNoTracking().OrderByDescending(d => d.CreatedAtUtc).Take(take)
                .Select(d => new { d.Id, d.ConsumerName, d.EventId, d.OutboxId, d.EventType, d.Error, d.CreatedAtUtc })
                .ToListAsync();

            var consumers = await db.ConsumerOffsets.AsNoTracking().Select(o => o.ConsumerName).ToListAsync();
            var lag = new System.Collections.Generic.List<object>();
            foreach (var name in consumers)
                lag.Add(new { consumer = name, lag = await OutboxDrainer.LagAsync(db, name) });

            return Ok(new { deadLetters, lag });
        }
    }
}
