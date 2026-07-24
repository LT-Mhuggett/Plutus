using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>T1.2 tenant provisioning (platform-admin only): creates a tenant + default
    /// company, store and admin user.</summary>
    [ApiController]
    [Route("api/v1/tenants")]
    public sealed class TenantsController : ControllerBase
    {
        private readonly ProvisioningService _provisioning;

        public TenantsController(ProvisioningService provisioning) => _provisioning = provisioning;

        [HttpPost]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        public async Task<IActionResult> Provision([FromBody] ProvisionRequest body)
        {
            try
            {
                var acting = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "platform-admin";
                var result = await _provisioning.ProvisionAsync(body, acting);
                return Created($"/api/v1/tenants/{result.TenantId}", result);
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }
    }
}
