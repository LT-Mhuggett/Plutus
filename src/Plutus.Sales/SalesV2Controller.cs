using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plutus.SharedKernel;

namespace Plutus.Sales
{
    /// <summary>
    /// T1.4 idempotent sale ingest. tenant/device identity come from the token (never the body);
    /// a body deviceId that disagrees with the token's did is a 403.
    /// </summary>
    [ApiController]
    [Route("api/v1/sales")]
    public sealed class SalesV2Controller : ControllerBase
    {
        private readonly SalesIngestService _ingest;
        private readonly ITenantContext _tenant;

        public SalesV2Controller(SalesIngestService ingest, ITenantContext tenant)
        {
            _ingest = ingest;
            _tenant = tenant;
        }

        [HttpPost]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        public async Task<IActionResult> Ingest([FromBody] IngestSaleRequest body)
        {
            if (body == null) return BadRequest(new { detail = "Body is required." });

            // Idempotency-Key, when present, must equal the body saleId.
            var idemKey = Request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrEmpty(idemKey) &&
                (!Guid.TryParse(idemKey, out var k) || k != body.SaleId))
                return BadRequest(new { detail = "Idempotency-Key must equal the body saleId." });

            // Device identity: token did wins. A conflicting body deviceId is a 403.
            var tokenDid = _tenant.DeviceId;
            if (tokenDid.HasValue && body.DeviceId != Guid.Empty && body.DeviceId != tokenDid.Value)
                return StatusCode(403, new { detail = "Body deviceId does not match the token device." });

            var deviceId = tokenDid ?? body.DeviceId;
            var acting = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "device";

            var outcome = await _ingest.IngestAsync(body, _tenant.TenantId, deviceId, acting);
            return StatusCode(outcome.Status, outcome.Body);
        }
    }
}
