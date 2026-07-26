using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Webstore;

namespace Plutus.DBService.Controllers
{
    /// <summary>
    /// WP6.2a — the WooCommerce webhook receiver. Anonymous by necessity (Woo sends no bearer);
    /// authenticated by the per-connection HMAC and tenant-scoped per delivery — ALL of that logic
    /// lives in the framework-free <see cref="WebstoreWebhookHandler"/> (unit-tested offline);
    /// this controller only moves bytes: raw body in, (status, body) out. The RAW body is what
    /// gets HMAC-verified — never a re-serialised model.
    /// </summary>
    [ApiController]
    public sealed class WebstoreWebhookController : ControllerBase
    {
        private readonly WebstoreWebhookHandler _handler;
        public WebstoreWebhookController(WebstoreWebhookHandler handler) => _handler = handler;

        [HttpPost("api/v1/webstores/{id:guid}/webhook")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status410Gone)]
        public async Task<IActionResult> Receive(Guid id)
        {
            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync();
            var signature = Request.Headers["X-WC-Webhook-Signature"].ToString();

            var result = await _handler.HandleOrderWebhookAsync(
                id, rawBody, string.IsNullOrEmpty(signature) ? null : signature, HttpContext.RequestAborted);
            return StatusCode(result.Status, result.Body);
        }
    }
}
