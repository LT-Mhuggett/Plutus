#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    public sealed record CustomerBody(string Name, string Email, string Phone);
    public sealed record CreditBody(long AmountPence, string Reason, Guid? SaleId, Guid? EntryId);
    /// <summary>FE1: pass <paramref name="TierId"/> to assign a catalogue tier — its name, rate and
    /// duration win and Tier/AutoDiscountRate are ignored. The free-text pair stays accepted for
    /// back-compat (legacy callers / pre-catalogue clients).</summary>
    public sealed record MembershipBody(string Tier, decimal AutoDiscountRate, DateOnly? StartDay, DateOnly? RenewalDay, Guid? TierId);

    /// <summary>
    /// Phase 8 customers / store credit / loyalty. Reads for the till (customer lookup, credit
    /// balance, membership auto-discount) are open to any authenticated principal — tills and
    /// the web POS consume them; management writes (create/edit customer, grant/expire credit,
    /// memberships) are gated on customers.manage and audited. Refund-to-credit and
    /// redeem-as-tender issue credit entries tied to the sale.
    /// </summary>
    [ApiController]
    public sealed class CustomersController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly CreditLedgerService _credit;

        public CustomersController(MySqlDbContext db, ITenantContext tenant, CreditLedgerService credit)
        {
            _db = db;
            _tenant = tenant;
            _credit = credit;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>FE1: the catalogue tier behind a membership, or null for the legacy free-text
        /// path (and for a tier row that has since vanished — the snapshot columns cover it).</summary>
        private static LoyaltyTier ResolveTier(Membership m, System.Collections.Generic.IReadOnlyDictionary<Guid, LoyaltyTier> tiers) =>
            m?.TierId is Guid id && tiers.TryGetValue(id, out var t) ? t : null;

        // ── customers ──

        [HttpGet("api/v1/customers")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string search, [FromQuery] int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            var q = _db.Customers.AsNoTracking().Where(c => c.Active);
            if (!string.IsNullOrWhiteSpace(search))
            {
                // FE2: a scanned loyalty card (or a typed membership number) resolves to that exact
                // customer — this is what makes scan-to-attach work at the till, since scanners are
                // keyboard-wedge into this same search box.
                var memberNo = MemberNumbers.TryCanonicalise(search);
                q = memberNo == null
                    ? q.Where(c => c.Name.Contains(search) || c.Email.Contains(search) || c.Phone.Contains(search))
                    : q.Where(c => c.MemberNo == memberNo
                        || c.Name.Contains(search) || c.Email.Contains(search) || c.Phone.Contains(search));
            }
            return Ok(await q.OrderBy(c => c.Name).Take(take)
                .Select(c => new { id = c.Id, name = c.Name, email = c.Email, phone = c.Phone, memberNo = c.MemberNo })
                .ToListAsync());
        }

        /// <summary>Loyalty view (till + portal): customers who are members OR hold store credit,
        /// with their tier, auto-discount, renewal and live balance. Balances summed in bulk.</summary>
        [HttpGet("api/v1/loyalty")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Loyalty([FromQuery] string search, [FromQuery] int take = 200)
        {
            take = Math.Clamp(take, 1, 500);
            var members = await _db.Memberships.AsNoTracking().Where(m => m.Active).ToListAsync();
            // FE1 live-follow: a tier-assigned membership shows the tier's CURRENT name + rate.
            var tiers = await _db.LoyaltyTiers.AsNoTracking().ToDictionaryAsync(t => t.Id);
            var accounts = await _db.CreditAccounts.AsNoTracking().ToListAsync();
            var balByAccount = (await _db.CreditEntries.AsNoTracking()
                    .GroupBy(e => e.CreditAccountId).Select(g => new { AccountId = g.Key, Bal = g.Sum(e => e.AmountPence) }).ToListAsync())
                .ToDictionary(x => x.AccountId, x => x.Bal);

            var custIds = members.Select(m => m.CustomerId).Concat(accounts.Select(a => a.CustomerId)).Distinct().ToList();
            var customers = await _db.Customers.AsNoTracking().Where(c => c.Active && custIds.Contains(c.Id)).ToListAsync();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var memberNo = MemberNumbers.TryCanonicalise(search); // FE2: scan/type a card number
                customers = customers.Where(c => (c.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (c.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (memberNo != null && c.MemberNo == memberNo)).ToList();
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var rows = customers.Select(c =>
            {
                var acc = accounts.FirstOrDefault(a => a.CustomerId == c.Id);
                var bal = acc != null && balByAccount.TryGetValue(acc.Id, out var b) ? b : 0L;
                var mem = members.Where(m => m.CustomerId == c.Id).OrderByDescending(m => m.RenewalDay).FirstOrDefault();
                var memTier = ResolveTier(mem, tiers);
                return new
                {
                    id = c.Id, name = c.Name, email = c.Email, phone = c.Phone,
                    memberNo = c.MemberNo,
                    tierId = mem?.TierId,
                    tier = memTier?.Name ?? mem?.Tier,
                    autoDiscountRate = memTier?.AutoDiscountRate ?? mem?.AutoDiscountRate,
                    renewalDay = mem?.RenewalDay,
                    expired = mem != null && mem.RenewalDay < today,
                    creditBalancePence = bal,
                };
            }).OrderByDescending(r => r.creditBalancePence).ThenBy(r => r.name).Take(take);
            return Ok(new { count = customers.Count, rows });
        }

        /// <summary>Customer with live credit balance + active membership (the till's
        /// at-sale lookup).</summary>
        [HttpGet("api/v1/customers/{id}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            var account = await _db.CreditAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == id);
            long balance = account == null ? 0 : await _credit.BalanceAsync(account.Id);
            var membership = await _db.Memberships.AsNoTracking()
                .Where(m => m.CustomerId == id && m.Active)
                .OrderByDescending(m => m.RenewalDay).FirstOrDefaultAsync();
            // FE1 live-follow: prefer the catalogue tier's current name/rate over the snapshot.
            var tier = membership?.TierId == null ? null
                : await _db.LoyaltyTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == membership.TierId.Value);
            // WP5.3: cross-channel links (e.g. a WooCommerce account matched by email).
            var externalRefs = await _db.CustomerExternalRefs.AsNoTracking()
                .Where(r => r.CustomerId == id)
                .OrderBy(r => r.Provider)
                .Select(r => new { provider = r.Provider, externalId = r.ExternalId, email = r.Email, lastSeenAtUtc = r.LastSeenAtUtc })
                .ToListAsync();
            return Ok(new
            {
                id = c.Id, name = c.Name, email = c.Email, phone = c.Phone,
                memberNo = c.MemberNo,
                memberBarcode = c.MemberNo == null ? null : MemberNumbers.BarcodePayload(c.MemberNo),
                creditAccountId = account?.Id,
                creditBalancePence = balance,
                membership = membership == null ? null : new
                {
                    tierId = membership.TierId,
                    tier = tier?.Name ?? membership.Tier,
                    autoDiscountRate = tier?.AutoDiscountRate ?? membership.AutoDiscountRate,
                    renewalDay = membership.RenewalDay, expired = membership.RenewalDay < DateOnly.FromDateTime(DateTime.UtcNow),
                },
                externalRefs,
            });
        }

        /// <summary>
        /// Sign a new loyalty member up. ⚠ Accepts **`pos.customers.add` OR `customers.manage`** —
        /// the `CheckAny` shape from `pos.stock.adjust`.
        ///
        /// ⚠ WHY A CASHIER MAY DO THIS AND NOTHING ELSE TO A CUSTOMER (Matt, 2026-08-13: *"Till
        /// operator to add new loyalty members"*, binding default 20). Signing someone up happens at
        /// the counter with a queue behind them; if it needs a supervisor it stops happening. But
        /// `Update` below and the membership endpoints stay on `customers.manage`, because changing
        /// an email quietly redirects an account and changing a tier changes every future basket —
        /// an added row can be deactivated, an altered one leaves no trace of what it was.
        ///
        /// ⚠ **Online-only on every till, and no permission changes that**: the number comes from a
        /// tenant-wide counter, so two offline tills would mint the same one.
        /// </summary>
        [HttpPost("api/v1/customers")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage + "," + PermissionCatalogue.PosCustomersAdd)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CustomerBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            _db.CurrentUser = Actor.ToString();
            // FE2: claim the tenant's next membership number first (its own save — the counter row
            // serialises concurrent creates), then create the customer holding it.
            var memberNo = await MemberNoAllocator.NextAsync(_db, _tenant.TenantId);
            var customer = new Customer
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, Name = body.Name.Trim(),
                Email = body.Email?.Trim(), Phone = body.Phone?.Trim(), MemberNo = memberNo,
                Active = true, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Customers.Add(customer);
            _db.Audit(_tenant.TenantId, Actor, "customer.create", nameof(Customer), customer.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/customers/{customer.Id}", new { id = customer.Id, memberNo });
        }

        /// <summary>Edit a customer's contact details (name/email/phone). Loyalty usability:
        /// customers were previously write-once. Master-data mutation (not an event), audited.</summary>
        [HttpPut("api/v1/customers/{id}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] CustomerBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id && c.Active);
            if (customer == null) return NotFound();

            _db.CurrentUser = Actor.ToString();

            // ⚠⚠ CAPTURED BEFORE THE MUTATION, AND THAT ORDER IS THE WHOLE POINT — Matt, 2026-08-18:
            // *"A customer needs to have a unique ID, because people can change emails over time. Audit
            // please."*
            //
            // The audit already recorded the request body, which is what the customer BECAME. It did not
            // record what they WERE — so an audit trail could tell you somebody's email is now
            // `x@y.com` and never that it used to be something else, which is precisely the change the
            // ruling is about. A record of the destination is not a record of the journey.
            //
            // ⚠ The identity is `Customer.Id` (a UUIDv7) and never the email, which is what makes
            // editing an email safe at all: nothing resolves a customer by it, so a change cannot
            // redirect somebody's account. That is the ruling's first half, and it is why the till is
            // allowed an edit path now.
            //
            // ⚠ Anonymous objects, so the payload is self-describing in the audit row rather than
            // needing this method to be read alongside it.
            var before = new { name = customer.Name, email = customer.Email, phone = customer.Phone };

            customer.Name = body.Name.Trim();
            customer.Email = body.Email?.Trim();
            customer.Phone = body.Phone?.Trim();

            var after = new { name = customer.Name, email = customer.Email, phone = customer.Phone };

            _db.Audit(_tenant.TenantId, Actor, "customer.update", nameof(Customer), customer.Id.ToString(),
                new { before, after });
            await _db.SaveChangesAsync();
            return Ok(new { id = customer.Id, name = customer.Name, email = customer.Email, phone = customer.Phone });
        }

        // ── store credit ──

        [HttpGet("api/v1/customers/{id}/credit")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Credit([FromRoute] Guid id)
        {
            if (await _db.Customers.AllAsync(c => c.Id != id)) return NotFound();
            var account = await _db.CreditAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == id);
            if (account == null) return Ok(new { balancePence = 0L, entries = Array.Empty<object>() });
            var entries = await _db.CreditEntries.AsNoTracking()
                .Where(e => e.CreditAccountId == account.Id)
                .OrderByDescending(e => e.CreatedAtUtc).Take(100)
                .Select(e => new { e.Type, e.AmountPence, e.Reason, e.SaleId, e.CreatedAtUtc })
                .ToListAsync();
            return Ok(new
            {
                balancePence = await _credit.BalanceAsync(account.Id),
                entries = entries.Select(e => new { type = e.Type.ToString(), amountPence = e.AmountPence, reason = e.Reason, saleId = e.SaleId, createdAtUtc = e.CreatedAtUtc }),
            });
        }

        /// <summary>Grant credit (refund-to-credit or a permission-gated goodwill grant).</summary>
        [HttpPost("api/v1/customers/{id}/credit/issue")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> IssueCredit([FromRoute] Guid id, [FromBody] CreditBody body)
        {
            if (body == null || body.AmountPence <= 0) return BadRequest(new { detail = "amountPence must be positive." });

            // ⚠⚠ A REASON IS MANDATORY — Matt, 2026-08-18: *"Adding credit needs to have a reason and
            // be viewable in the customers history."*
            //
            // This used to read `body.Reason?.Trim() ?? "grant"`, and the portal sent
            // `issueReason || "goodwill grant"`. So credit could be put on somebody's account with **no
            // reason anybody typed**, and the history would then show a plausible word that means
            // nothing — which is worse than a blank, because it READS as an audit trail. A substituted
            // reason is the same class of fault as a substituted figure.
            //
            // ⚠ Refused rather than defaulted, and refused HERE rather than only in the portal: the
            // endpoint is the only thing both the portal and any future till path go through.
            //
            // ⚠ The customer is known by construction — the route carries the id and an unknown one is
            // a 404 below. There is deliberately no anonymous credit: it is a liability the shop owes a
            // NAMED person, and a bearer instrument is what a gift card is (WP13).
            var reason = body.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest(new { detail = "A reason is required when granting credit, and it is kept on the customer's history." });

            if (await _db.Customers.AllAsync(c => c.Id != id)) return NotFound();

            _db.CurrentUser = Actor.ToString();
            var account = await _credit.EnsureAccountAsync(_tenant.TenantId, id);
            await _db.SaveChangesAsync(); // persist a new account before the entry
            var entry = await _credit.IssueAsync(_tenant.TenantId, account.Id, body.AmountPence,
                reason, body.SaleId, Actor, body.EntryId);
            _db.Audit(_tenant.TenantId, Actor, "credit.issue", nameof(CreditEntry), entry.Id.ToString(),
                new { customerId = id, body.AmountPence, body.Reason });
            await _db.SaveChangesAsync();
            return Ok(new { entryId = entry.Id, balancePence = await _credit.BalanceAsync(account.Id) });
        }

        /// <summary>Redeem credit as a tender (till/web POS). 400 if it would overdraw.</summary>
        [HttpPost("api/v1/customers/{id}/credit/redeem")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RedeemCredit([FromRoute] Guid id, [FromBody] CreditBody body)
        {
            if (body == null || body.AmountPence <= 0) return BadRequest(new { detail = "amountPence must be positive." });
            var account = await _db.CreditAccounts.FirstOrDefaultAsync(a => a.CustomerId == id);
            if (account == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            try
            {
                var entry = await _credit.RedeemAsync(_tenant.TenantId, account.Id, body.AmountPence,
                    body.Reason?.Trim() ?? "redeem", body.SaleId, Actor, body.EntryId);
                return Ok(new { entryId = entry.Id, balancePence = await _credit.BalanceAsync(account.Id) });
            }
            catch (InsufficientCreditException ex)
            {
                return BadRequest(new { detail = ex.Message });
            }
        }

        // ── membership / loyalty ──

        [HttpPost("api/v1/customers/{id}/membership")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetMembership([FromRoute] Guid id, [FromBody] MembershipBody body)
        {
            if (body == null) return BadRequest(new { detail = "a body is required." });

            // FE1: a catalogue tier supplies name + rate + duration; free-text is the legacy path.
            LoyaltyTier tier = null;
            if (body.TierId is Guid tierId)
            {
                tier = await _db.LoyaltyTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tierId);
                if (tier == null) return BadRequest(new { detail = "unknown tierId." });
                if (!tier.Active) return BadRequest(new { detail = $"tier '{tier.Name}' is inactive." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.Tier)) return BadRequest(new { detail = "tier is required." });
                if (body.AutoDiscountRate < 0 || body.AutoDiscountRate > 1)
                    return BadRequest(new { detail = "autoDiscountRate must be between 0 and 1." });
            }
            if (await _db.Customers.AllAsync(c => c.Id != id)) return NotFound();

            _db.CurrentUser = Actor.ToString();
            // one active membership per customer — deactivate any prior
            var prior = await _db.Memberships.Where(m => m.CustomerId == id && m.Active).ToListAsync();
            foreach (var m in prior) m.Active = false;

            var start = body.StartDay ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var membership = new Membership
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, CustomerId = id,
                TierId = tier?.Id,
                // when a tier is assigned these are the as-assigned snapshot; reads prefer the tier
                Tier = tier?.Name ?? body.Tier.Trim(),
                AutoDiscountRate = tier?.AutoDiscountRate ?? body.AutoDiscountRate,
                StartDay = start,
                RenewalDay = body.RenewalDay ?? (tier != null ? start.AddMonths(tier.DurationMonths) : start.AddYears(1)),
                Active = true, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Memberships.Add(membership);
            _db.Audit(_tenant.TenantId, Actor, "membership.set", nameof(Membership), membership.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/customers/{id}", new { id = membership.Id });
        }
    }
}
