#nullable disable

using System;
using System.Collections.Generic;
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
    public sealed record GiftCardSettingsBody(string Treatment);
    public sealed record GenerateCardsBody(int Count, int? ExpiresMonths, string Batch);
    public sealed record ActivateCardBody(long AmountPence, Guid? SaleId, Guid? CustomerId, Guid? EntryId);
    public sealed record RedeemCardBody(long AmountPence, Guid? SaleId, Guid? EntryId);
    public sealed record AdjustCardBody(long AmountPence, string Reason);
    public sealed record LinkCustomerBody(Guid? CustomerId);

    /// <summary>
    /// FE7 gift cards. Codes are generated in the portal (worthless until sold), ACTIVATED at a till
    /// when someone buys one, and REDEEMED as a tender against a later sale. The balance is the sum
    /// of an append-only ledger (see <see cref="GiftCardLedgerService"/>) — there is no mutable
    /// balance column to drift.
    ///
    /// Gating: the lookup a till needs before it can take a card is open to any authenticated
    /// principal (like the customer/credit reads); activate and redeem use the SalesIngest policy
    /// (a device token OR an operator holding pos.sell — the same gate store-credit redemption uses,
    /// because both are tender operations a till performs); everything administrative (generate, void,
    /// adjust, link a customer) needs <c>giftcards.manage</c>. All writes are audited.
    ///
    /// ⚠ Money treatment (the trap): a card is a LIABILITY, and WHEN its VAT falls due depends on the
    /// tenant's declared voucher treatment (<see cref="GiftCardVatTreatment"/>). Nothing here works
    /// until the store owner has made that decision — generate/activate/redeem all 409 — because the
    /// wrong default files someone's VAT return for them. See <see cref="GiftCardSaleItem"/> for the
    /// catalogue mechanics and the liability endpoint for where the money surfaces.
    /// </summary>
    [ApiController]
    public sealed class GiftCardsController : ControllerBase
    {
        /// <summary>A single generate call is a print run, not a migration.</summary>
        private const int MaxBatch = 500;

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly GiftCardLedgerService _cards;

        public GiftCardsController(MySqlDbContext db, ITenantContext tenant, GiftCardLedgerService cards)
        {
            _db = db;
            _tenant = tenant;
            _cards = cards;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // ── the VAT-treatment decision (the gate) ──
        // HMRC's 2019 voucher rules make the treatment a function of what a card can buy: one VAT
        // rate across the catalogue → single-purpose (VAT when the card is SOLD); mixed rates →
        // multi-purpose (VAT when the card is SPENT). The store owner must declare which describes
        // their shop BEFORE any card can be minted or sold — silently defaulting would file someone's
        // VAT return for them.

        private const string TreatMulti = "multi";
        private const string TreatSingle = "single";

        private static string Wire(GiftCardVatTreatment t) =>
            t == GiftCardVatTreatment.SinglePurpose ? TreatSingle : TreatMulti;

        private Task<GiftCardSettings> SettingsAsync() => _db.GiftCardSettings.FirstOrDefaultAsync();

        private ObjectResult NotConfigured() => Conflict(new
        {
            detail = "Gift cards aren't enabled yet — the store owner must first choose the VAT " +
                     "treatment (management portal → Gift cards).",
        });

        /// <summary>The current decision. Open to any authenticated principal — the till needs it to
        /// price an activation line, and it reveals nothing sensitive.</summary>
        [HttpGet("api/v1/giftcards/settings")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSettings()
        {
            var s = await SettingsAsync();
            return Ok(new
            {
                treatment = s == null ? null : Wire(s.Treatment),
                decidedAtUtc = s?.DecidedAtUtc,
                // the choice locks once VAT has actually been posted under it
                locked = s != null && await _db.GiftCardEntries.AnyAsync(),
            });
        }

        /// <summary>
        /// Make (or change) the decision. Changing is allowed only while NO ledger entry exists —
        /// the moment a card has been sold, VAT has been declared under the old treatment and
        /// flipping it would misstate a return. Re-affirming the same value is always a no-op.
        /// </summary>
        [HttpPut("api/v1/giftcards/settings")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> PutSettings([FromBody] GiftCardSettingsBody body)
        {
            var wanted = body?.Treatment?.Trim().ToLowerInvariant() switch
            {
                TreatMulti => GiftCardVatTreatment.MultiPurpose,
                TreatSingle => GiftCardVatTreatment.SinglePurpose,
                _ => (GiftCardVatTreatment?)null,
            };
            if (wanted == null)
                return BadRequest(new { detail = $"treatment must be '{TreatMulti}' (VAT when spent) or '{TreatSingle}' (VAT when sold)." });

            _db.CurrentUser = Actor.ToString();
            var s = await SettingsAsync();
            if (s == null)
            {
                s = new GiftCardSettings
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId,
                    Treatment = wanted.Value, DecidedByUserId = Actor, DecidedAtUtc = DateTime.UtcNow,
                };
                _db.GiftCardSettings.Add(s);
            }
            else if (s.Treatment != wanted.Value)
            {
                if (await _db.GiftCardEntries.AnyAsync())
                    return Conflict(new
                    {
                        detail = "The VAT treatment is locked: cards have already been sold under the " +
                                 "current choice, so changing it would misstate a VAT return. Speak to " +
                                 "your accountant — a change needs a fresh card programme.",
                    });
                s.Treatment = wanted.Value;
                s.DecidedByUserId = Actor;
                s.DecidedAtUtc = DateTime.UtcNow;
            }
            else
            {
                // same value re-affirmed — nothing to write, and never a conflict
                return Ok(new { treatment = Wire(s.Treatment), decidedAtUtc = s.DecidedAtUtc, locked = await _db.GiftCardEntries.AnyAsync() });
            }

            _db.Audit(_tenant.TenantId, Actor, "giftcard.settings", nameof(GiftCardSettings), s.Id.ToString(),
                new { treatment = Wire(s.Treatment) });
            await _db.SaveChangesAsync();
            return Ok(new { treatment = Wire(s.Treatment), decidedAtUtc = s.DecidedAtUtc, locked = false });
        }

        /// <summary>Status as the UI shows it — derived, never stored, so it can never disagree with
        /// the ledger.</summary>
        private static string StatusOf(GiftCard c, long balance, DateTime now) =>
            c.VoidedAtUtc != null ? "void"
            : c.IssuedAtUtc == null ? "unsold"
            : c.ExpiresAtUtc != null && c.ExpiresAtUtc <= now ? "expired"
            : balance <= 0 ? "spent"
            : "active";

        // ── read ──

        /// <summary>The card list. Reads are gated on giftcards.manage — a balance list is
        /// commercially sensitive, and the till only ever needs ONE card (see Lookup).</summary>
        [HttpGet("api/v1/giftcards")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] string search, [FromQuery] string status, [FromQuery] int take = 200)
        {
            take = Math.Clamp(take, 1, 1000);
            var q = _db.GiftCards.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                // A scanned/typed code jumps straight to its card; anything else searches the batch
                // label and the linked customer's name.
                var code = GiftCardCodes.TryCanonicalise(search);
                if (code != null) q = q.Where(c => c.Code == code);
                else
                {
                    var ids = await _db.Customers.AsNoTracking()
                        .Where(c => c.Name.Contains(search)).Select(c => c.Id).ToListAsync();
                    q = q.Where(c => c.Batch.Contains(search) || (c.CustomerId != null && ids.Contains(c.CustomerId.Value)));
                }
            }

            var cards = await q.OrderByDescending(c => c.CreatedAtUtc).Take(take).ToListAsync();
            var rows = await DecorateAsync(cards);

            // Status is derived, so it can only be filtered after decoration — done here rather than
            // in SQL precisely so the list and the redeem path agree on what "active" means.
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
                rows = rows.Where(r => r.Status == status).ToList();

            return Ok(rows);
        }

        /// <summary>One card with its full history — the portal's detail dialog and the printable
        /// voucher both read this.</summary>
        [HttpGet("api/v1/giftcards/{code}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Detail([FromRoute] string code)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound();

            var entries = await _db.GiftCardEntries.AsNoTracking()
                .Where(e => e.GiftCardId == card.Id)
                .OrderBy(e => e.CreatedAtUtc).ToListAsync();
            var customer = card.CustomerId is Guid cid
                ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid)
                : null;
            var balance = entries.Sum(e => e.AmountPence);

            return Ok(new
            {
                code = card.Code,
                pretty = GiftCardCodes.Pretty(card.Code),
                barcode = GiftCardCodes.BarcodePayload(card.Code),
                balancePence = balance,
                status = StatusOf(card, balance, DateTime.UtcNow),
                issuedAtUtc = card.IssuedAtUtc,
                expiresAtUtc = card.ExpiresAtUtc,
                voidedAtUtc = card.VoidedAtUtc,
                soldSaleId = card.SoldSaleId,
                batch = card.Batch,
                createdAtUtc = card.CreatedAtUtc,
                customerId = card.CustomerId,
                customerName = customer?.Name,
                customerMemberNo = customer?.MemberNo,
                entries = entries.Select(e => new
                {
                    id = e.Id, type = e.Type.ToString(), amountPence = e.AmountPence,
                    reason = e.Reason, saleId = e.SaleId, actorUserId = e.ActorUserId, atUtc = e.CreatedAtUtc,
                }),
            });
        }

        /// <summary>
        /// The till's pre-flight: "what is this thing I just scanned?" Open to any authenticated
        /// principal because a cashier must be able to answer "how much is on this card?" — it returns
        /// ONE card by exact code, so it cannot be used to browse balances.
        /// </summary>
        [HttpGet("api/v1/giftcards/{code}/lookup")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Lookup([FromRoute] string code)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound(new { detail = "That code isn't a gift card for this shop." });
            var settings = await SettingsAsync();
            if (settings == null) return NotConfigured();   // a card can't exist without a decision, but be explicit
            var balance = await _cards.BalanceAsync(card.Id);
            return Ok(new
            {
                code = card.Code,
                pretty = GiftCardCodes.Pretty(card.Code),
                balancePence = balance,
                status = StatusOf(card, balance, DateTime.UtcNow),
                expiresAtUtc = card.ExpiresAtUtc,
                customerId = card.CustomerId,
                // the till builds its activation line against this catalogue row
                itemIdOne = GiftCardSaleItem.ItemIdOne,
                // ...and prices it by the tenant's declared treatment: "single" = VAT charged at the
                // sale of the card (line carries 20%), "multi" = zero now, VAT when spent.
                vatTreatment = Wire(settings.Treatment),
            });
        }

        // ── generate ──

        /// <summary>Mint N unsold codes. They are worthless until a till activates one, so this is
        /// safe to run before a print job.</summary>
        [HttpPost("api/v1/giftcards/generate")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Generate([FromBody] GenerateCardsBody body)
        {
            if (await SettingsAsync() == null) return NotConfigured();
            var count = body?.Count ?? 0;
            if (count < 1 || count > MaxBatch)
                return BadRequest(new { detail = $"Generate between 1 and {MaxBatch} cards at a time." });
            if (body.ExpiresMonths is int m && (m < 1 || m > 120))
                return BadRequest(new { detail = "Expiry must be between 1 and 120 months (or blank for none)." });

            _db.CurrentUser = Actor.ToString();
            var now = DateTime.UtcNow;
            var expires = body.ExpiresMonths is int months ? now.AddMonths(months) : (DateTime?)null;
            var batch = string.IsNullOrWhiteSpace(body.Batch) ? null : body.Batch.Trim();

            // Collision is astronomically unlikely (60 bits) but a duplicate would be a live bug, so
            // check the batch against itself and the DB rather than trusting the odds.
            var existing = new HashSet<string>(
                await _db.GiftCards.AsNoTracking().Select(c => c.Code).ToListAsync(), StringComparer.Ordinal);
            var created = new List<GiftCard>(count);
            while (created.Count < count)
            {
                var code = GiftCardCodes.New();
                if (!existing.Add(code)) continue;
                var card = new GiftCard
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId, Code = code,
                    ExpiresAtUtc = expires, Batch = batch, CreatedAtUtc = now,
                };
                _db.GiftCards.Add(card);
                created.Add(card);
            }

            _db.Audit(_tenant.TenantId, Actor, "giftcard.generate", nameof(GiftCard), batch ?? "batch",
                new { count, expiresMonths = body.ExpiresMonths, batch });
            await _db.SaveChangesAsync();

            return Created("/api/v1/giftcards", created.Select(c => new
            {
                code = c.Code,
                pretty = GiftCardCodes.Pretty(c.Code),
                barcode = GiftCardCodes.BarcodePayload(c.Code),
                expiresAtUtc = c.ExpiresAtUtc,
            }));
        }

        // ── activate / redeem (the till) ──

        /// <summary>
        /// Sell a card: load it and mark it active. Gated on pos.sell — selling a card is selling.
        /// Idempotent by EntryId so a re-sent activation cannot load the card twice.
        /// </summary>
        [HttpPost("api/v1/giftcards/{code}/activate")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Activate([FromRoute] string code, [FromBody] ActivateCardBody body)
        {
            if (await SettingsAsync() == null) return NotConfigured();
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound(new { detail = "That code isn't a gift card for this shop." });

            _db.CurrentUser = Actor.ToString();
            try
            {
                var entry = await _cards.ActivateAsync(
                    card, body?.AmountPence ?? 0, body?.SaleId, Actor, body?.EntryId);
                if (body?.CustomerId is Guid cid && card.CustomerId == null) card.CustomerId = cid;

                _db.Audit(_tenant.TenantId, Actor, "giftcard.activate", nameof(GiftCard), card.Code,
                    new { amountPence = body?.AmountPence, saleId = body?.SaleId, customerId = body?.CustomerId });
                await _db.SaveChangesAsync();
                return Ok(new
                {
                    entryId = entry.Id, code = card.Code,
                    balancePence = await _cards.BalanceAsync(card.Id),
                    expiresAtUtc = card.ExpiresAtUtc,
                });
            }
            catch (GiftCardException ex)
            {
                return StatusCode(ex.Status, new { detail = ex.Message });
            }
        }

        /// <summary>Spend a card as a tender. Partial redemption leaves the remainder on the card.</summary>
        [HttpPost("api/v1/giftcards/{code}/redeem")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Redeem([FromRoute] string code, [FromBody] RedeemCardBody body)
        {
            if (await SettingsAsync() == null) return NotConfigured();
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound(new { detail = "That code isn't a gift card for this shop." });

            _db.CurrentUser = Actor.ToString();
            try
            {
                var entry = await _cards.RedeemAsync(card, body?.AmountPence ?? 0, body?.SaleId, Actor, body?.EntryId);
                _db.Audit(_tenant.TenantId, Actor, "giftcard.redeem", nameof(GiftCard), card.Code,
                    new { amountPence = body?.AmountPence, saleId = body?.SaleId });
                await _db.SaveChangesAsync();
                return Ok(new { entryId = entry.Id, code = card.Code, balancePence = await _cards.BalanceAsync(card.Id) });
            }
            catch (GiftCardException ex)
            {
                return StatusCode(ex.Status, new { detail = ex.Message });
            }
        }

        // ── administration ──

        /// <summary>Void a card (lost, stolen, mis-issued). The ledger is left intact so the money can
        /// still be explained; the balance simply stops being spendable.</summary>
        [HttpPost("api/v1/giftcards/{code}/void")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Void([FromRoute] string code, [FromBody] AdjustCardBody body)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound();
            if (card.VoidedAtUtc != null) return Conflict(new { detail = "That card is already void." });

            _db.CurrentUser = Actor.ToString();
            var balance = await _cards.BalanceAsync(card.Id);
            card.VoidedAtUtc = DateTime.UtcNow;
            _db.Audit(_tenant.TenantId, Actor, "giftcard.void", nameof(GiftCard), card.Code,
                new { reason = body?.Reason, balanceAtVoidPence = balance });
            await _db.SaveChangesAsync();
            return Ok(new { code = card.Code, status = "void", balancePence = balance });
        }

        /// <summary>Un-void — the mirror of Void, for when a "lost" card turns up.</summary>
        [HttpPost("api/v1/giftcards/{code}/unvoid")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Unvoid([FromRoute] string code)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            card.VoidedAtUtc = null;
            _db.Audit(_tenant.TenantId, Actor, "giftcard.unvoid", nameof(GiftCard), card.Code, new { });
            await _db.SaveChangesAsync();
            var balance = await _cards.BalanceAsync(card.Id);
            return Ok(new { code = card.Code, status = StatusOf(card, balance, DateTime.UtcNow), balancePence = balance });
        }

        /// <summary>Correct a balance by hand (goodwill top-up, or fixing a mis-keyed activation).
        /// Both signs allowed; always audited with the reason.</summary>
        [HttpPost("api/v1/giftcards/{code}/adjust")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Adjust([FromRoute] string code, [FromBody] AdjustCardBody body)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound();
            if (body == null || body.AmountPence == 0)
                return BadRequest(new { detail = "An adjustment needs a non-zero amount." });
            if (string.IsNullOrWhiteSpace(body.Reason))
                return BadRequest(new { detail = "A reason is required for a manual adjustment." });
            if (card.IssuedAtUtc == null)
                return BadRequest(new { detail = "That card hasn't been sold yet — activate it instead of adjusting it." });

            var balance = await _cards.BalanceAsync(card.Id);
            if (balance + body.AmountPence < 0)
                return BadRequest(new { detail = $"That would take the balance below zero (it holds {balance / 100m:0.00})." });

            _db.CurrentUser = Actor.ToString();
            _cards.Adjust(card, body.AmountPence, body.Reason.Trim(), Actor);
            _db.Audit(_tenant.TenantId, Actor, "giftcard.adjust", nameof(GiftCard), card.Code,
                new { amountPence = body.AmountPence, reason = body.Reason, balanceBeforePence = balance });
            await _db.SaveChangesAsync();
            return Ok(new { code = card.Code, balancePence = await _cards.BalanceAsync(card.Id) });
        }

        /// <summary>Link (or unlink, with a null customerId) a card to a loyalty customer.</summary>
        [HttpPost("api/v1/giftcards/{code}/customer")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.GiftCardsManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> LinkCustomer([FromRoute] string code, [FromBody] LinkCustomerBody body)
        {
            var card = await _cards.FindAsync(code);
            if (card == null) return NotFound();
            if (body?.CustomerId is Guid cid && !await _db.Customers.AnyAsync(c => c.Id == cid))
                return NotFound(new { detail = "No such customer." });

            _db.CurrentUser = Actor.ToString();
            card.CustomerId = body?.CustomerId;
            _db.Audit(_tenant.TenantId, Actor, "giftcard.link", nameof(GiftCard), card.Code,
                new { customerId = body?.CustomerId });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── liability (FE7.5) ──

        /// <summary>
        /// Outstanding gift-card liability: the money customers have paid that the shop still owes in
        /// goods. Mirrors the store-credit outstanding number.
        ///
        /// ⚠ This is a LIABILITY, not turnover. Activations are also inside the day's takings (the till
        /// really did take the cash), so an accountant preparing accounts must back the activation
        /// total out of product revenue — which is what <c>activatedPence</c> is for. VAT is not
        /// affected either way: activation lines post zero VAT by construction.
        /// </summary>
        [HttpGet("api/v1/giftcards/liability")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Liability([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc)
        {
            var now = DateTime.UtcNow;
            var cards = await _db.GiftCards.AsNoTracking().ToListAsync();
            var entries = await _db.GiftCardEntries.AsNoTracking().ToListAsync();
            var balances = entries.GroupBy(e => e.GiftCardId)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.AmountPence));

            long Bal(Guid id) => balances.TryGetValue(id, out var b) ? b : 0;

            // Outstanding = spendable money only. A voided or expired card is no longer a liability
            // (the shop will not honour it), which is exactly why status is derived from the same
            // rules the redeem path enforces.
            var live = cards.Where(c => StatusOf(c, Bal(c.Id), now) == "active").ToList();

            var window = entries.Where(e =>
                (fromUtc == null || e.CreatedAtUtc >= fromUtc) && (toUtc == null || e.CreatedAtUtc <= toUtc)).ToList();

            return Ok(new
            {
                outstandingPence = live.Sum(c => Bal(c.Id)),
                outstandingCards = live.Count,
                unsoldCards = cards.Count(c => c.VoidedAtUtc == null && c.IssuedAtUtc == null),
                // in-window movement, so "how much did we sell / how much got spent" is answerable
                activatedPence = window.Where(e => e.Type == GiftCardEntryType.Issue).Sum(e => e.AmountPence),
                redeemedPence = -window.Where(e => e.Type == GiftCardEntryType.Redeem).Sum(e => e.AmountPence),
                adjustedPence = window.Where(e => e.Type == GiftCardEntryType.Adjust).Sum(e => e.AmountPence),
                expiredPence = -window.Where(e => e.Type == GiftCardEntryType.Expire).Sum(e => e.AmountPence),
                // the money on cards that can no longer be spent — reconciles outstanding vs Σledger
                lockedPence = cards.Where(c => StatusOf(c, Bal(c.Id), now) is "void" or "expired").Sum(c => Bal(c.Id)),
                fromUtc, toUtc,
            });
        }

        /// <summary>A list row. A named shape (not an anonymous type) so the derived status can be
        /// filtered on without reflection.</summary>
        private sealed record CardRow(
            string Code, string Pretty, long BalancePence, string Status,
            DateTime? IssuedAtUtc, DateTime? ExpiresAtUtc, DateTime? VoidedAtUtc,
            string Batch, DateTime CreatedAtUtc, Guid? CustomerId, string CustomerName);

        /// <summary>Card rows + derived status + balance, in one pass (avoids N+1 on the list).</summary>
        private async Task<List<CardRow>> DecorateAsync(List<GiftCard> cards)
        {
            var ids = cards.Select(c => c.Id).ToList();
            var balances = (await _db.GiftCardEntries.AsNoTracking()
                    .Where(e => ids.Contains(e.GiftCardId))
                    .GroupBy(e => e.GiftCardId)
                    .Select(g => new { Id = g.Key, Bal = g.Sum(e => e.AmountPence) }).ToListAsync())
                .ToDictionary(x => x.Id, x => x.Bal);

            var customerIds = cards.Where(c => c.CustomerId != null).Select(c => c.CustomerId.Value).Distinct().ToList();
            var customers = customerIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                        .Select(c => new { c.Id, c.Name }).ToListAsync())
                    .ToDictionary(x => x.Id, x => x.Name);

            var now = DateTime.UtcNow;
            return cards.Select(c =>
            {
                var balance = balances.TryGetValue(c.Id, out var b) ? b : 0;
                return new CardRow(
                    c.Code, GiftCardCodes.Pretty(c.Code), balance, StatusOf(c, balance, now),
                    c.IssuedAtUtc, c.ExpiresAtUtc, c.VoidedAtUtc, c.Batch, c.CreatedAtUtc, c.CustomerId,
                    c.CustomerId is Guid cid && customers.TryGetValue(cid, out var n) ? n : null);
            }).ToList();
        }
    }
}
