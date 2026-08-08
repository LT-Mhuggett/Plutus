using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>GET /api/v1/ping — see <see cref="PingController"/> for why this exists.</summary>
    public sealed record PingResult(bool Ok, DateTime UtcNow, string? ApiVersion);

    /// <summary>
    /// The cheapest possible proof that a till is talking to a Plutus backend.
    ///
    /// ⚠ THIS ENDPOINT DELIBERATELY TOUCHES NO DATABASE, and that is the whole design. A till's
    /// connection question has two halves that operators confuse constantly and that need different
    /// answers:
    ///
    ///   1. "Can I reach the platform at all?" — DNS, routing, TLS, the reverse proxy, the process
    ///      being up. That is this endpoint. It must answer even when MySQL is unreachable, or a
    ///      till would report "no server" during a database blip and staff would start rebooting
    ///      routers.
    ///   2. "Does the platform still accept THIS till?" — the device row, its secret, revocation.
    ///      That is <c>POST /api/v1/tokens/device</c>, which does hit the database.
    ///
    /// A client runs the two in order (see <c>Plutus.Client.Core.ConnectivityProbe</c>) and can
    /// therefore tell an operator which of "no network", "no server", or "this till has been
    /// revoked" is actually true — three problems with three different people to call.
    ///
    /// Anonymous on purpose: a till that has not enrolled yet, or whose secret has been revoked,
    /// still needs to know whether the address it was given is reachable. Requiring a token here
    /// would collapse cases 1 and 2 back together and make first-run setup undiagnosable.
    /// </summary>
    [ApiController]
    [Route("api/v1/ping")]
    public sealed class PingController : ControllerBase
    {
        // The BACKEND's version (versions/backend.txt), read from the entry assembly rather than
        // this module's — a module is not a deployable and has no release of its own.
        //
        // ⚠ A till reports a DIFFERENT number, on purpose: components version independently, so
        // "web till 1.2.0 against backend 1.0.4" is a fact worth being able to state. This is the
        // cheapest place to learn the server half of that pair, since every till pings anyway.
        private static readonly string Version = PlutusVersion.Current;

        /// <summary>200 with the server's clock. <see cref="PingResult.UtcNow"/> is not decoration:
        /// device-token HMAC validation and VAT effective-dating both turn on time, so a till that
        /// silently has the wrong clock produces sales the server judges against a different
        /// instant. A client comparing this against its own clock can warn before that bites.</summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Get() => Ok(new PingResult(true, DateTime.UtcNow, Version));
    }
}
