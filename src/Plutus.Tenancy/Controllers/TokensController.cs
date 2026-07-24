using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Plutus.Tenancy.Controllers
{
    public sealed record DeviceTokenRequest(Guid DeviceId, string ClientSecret);

    /// <summary>T1.2 device token exchange: a device presents its id + one-time secret and gets
    /// a short-lived HMAC token carrying tid/did/scope:"device".</summary>
    [ApiController]
    [Route("api/v1/tokens")]
    public sealed class TokensController : ControllerBase
    {
        private readonly EnrolmentService _enrolment;

        public TokensController(EnrolmentService enrolment) => _enrolment = enrolment;

        [HttpPost("device")]
        [AllowAnonymous]
        [EnableRateLimiting("enrol")]
        public async Task<IActionResult> DeviceToken([FromBody] DeviceTokenRequest body)
        {
            if (body == null || body.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(body.ClientSecret))
                return BadRequest("deviceId and clientSecret are required.");
            try
            {
                var result = await _enrolment.IssueDeviceTokenAsync(body.DeviceId, body.ClientSecret);
                return Ok(result);
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }
    }
}
