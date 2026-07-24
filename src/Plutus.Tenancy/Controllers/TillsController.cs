using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record CreateTillRequest(int StoreId, string Name);
    public sealed record EnrolRequest(string EnrolmentCode);

    /// <summary>T1.2 till lifecycle: portal creates tills + enrolment codes; a device redeems a
    /// code anonymously. Business logic lives in <see cref="EnrolmentService"/>.</summary>
    [ApiController]
    [Route("api/v1/tills")]
    public sealed class TillsController : ControllerBase
    {
        private readonly EnrolmentService _enrolment;
        private readonly ITenantContext _tenant;

        public TillsController(EnrolmentService enrolment, ITenantContext tenant)
        {
            _enrolment = enrolment;
            _tenant = tenant;
        }

        private string ActingUser => User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "portal";

        [HttpPost]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        public async Task<IActionResult> CreateTill([FromBody] CreateTillRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return BadRequest("storeId and name are required.");
            var result = await _enrolment.CreateTillAsync(_tenant.TenantId, body.StoreId, body.Name.Trim(), ActingUser);
            return Created($"/api/v1/tills/{result.TillId}", result);
        }

        [HttpPost("enrol")]
        [AllowAnonymous]
        [EnableRateLimiting("enrol")]
        public async Task<IActionResult> Enrol([FromBody] EnrolRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.EnrolmentCode)) return BadRequest("enrolmentCode is required.");
            try
            {
                var result = await _enrolment.EnrolAsync(body.EnrolmentCode, "enrol");
                return Ok(result);
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        [HttpPost("{id}/revoke")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        public async Task<IActionResult> Revoke([FromRoute] Guid id)
        {
            await _enrolment.RevokeTillAsync(id, ActingUser);
            return NoContent();
        }
    }
}
