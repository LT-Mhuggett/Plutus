using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        [ProducesResponseType(StatusCodes.Status201Created)]   // recorded
        [ProducesResponseType(StatusCodes.Status200OK)]        // duplicate → stored outcome
        [ProducesResponseType(StatusCodes.Status202Accepted)]  // quarantined
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        /// <summary>
        /// Read one sale back — the endpoint a till uses to refund goods it did not itself sell.
        ///
        /// ⚠ THIS DID NOT EXIST, and its absence is why CROSS-TILL REFUNDS HAVE NEVER WORKED.
        /// `PlutusApiClient.GetSaleAsync` has targeted `GET /api/v1/sales/{saleId}` since it was
        /// written, `ReturnLookup.TryServerAsync` calls it and — by design — swallows the failure
        /// and falls back to this till's own record. So a refund for goods bought at another branch
        /// silently became "we have no record of that sale", on a platform that had the sale all
        /// along. Nothing errored, no test caught it: no test anywhere called `GetSaleAsync`, and
        /// the contract (`SaleDto`, `SaleLineDto`, `SaleAdjustmentDto`) was fully specified. Found
        /// 2026-08-10 by asking the live server, which answered 404 for a route four documents
        /// assumed was live.
        ///
        /// ⚠ A DEVICE TOKEN IS ENOUGH, deliberately. The whole point is a till looking up a sale it
        /// did not take, and requiring an operator token would make cross-till refunds fail exactly
        /// when the shop is busiest — the same argument that gates the heartbeat this way. The
        /// boundary that matters is the TENANT, and it is enforced by the global query filter, not
        /// by a predicate here.
        ///
        /// ⚠ `AlreadyRefundedPence` counts VOIDS as well as refunds. A voided line's money left the
        /// drawer just as surely as a refunded one's, and treating a void as "not a refund" leaves
        /// exactly that much refundable a second time (binding default 12).
        /// </summary>
        [HttpGet("{saleId:guid}")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid saleId)
        {
            var dto = await _ingest.FindSaleAsync(saleId, _tenant.TenantId);

            // ⚠ 404, never an empty sale. A till that cannot tell "no such sale" from "a sale with
            // nothing on it" would refuse a legitimate refund and blame the customer's receipt.
            return dto is null
                ? NotFound(new { detail = "No sale with that id." })
                : Ok(dto);
        }
    }
}
