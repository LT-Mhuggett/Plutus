using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using Plutus.SharedKernel;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ItemController : CompositeApiControllerBaseCRU<Item, ItemBody, string, Guid, ItemParameters>
    {
        protected override ICompositeRepositoryBase<Item, string, Guid> Repository => RepositoryWrapper.ItemRepository;

        /// <summary>
        /// ⚠ Injected for the ALIAS lookup only (multi-barcode plan, MB2). The legacy repository
        /// wrapper knows nothing about `ItemBarcodes`, and this controller is the till's exact-scan
        /// door, so the read has to happen here.
        /// </summary>
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public ItemController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor, MySqlDbContext db, ITenantContext tenant) : base(repositoryWrapper, httpContextAccessor)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor =>
            Guid.TryParse(User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// VAT guardrail (VAT-Investigation plan §5.4, 2026-07-23): an item's inc/ex prices
        /// must be consistent with its tax band (|Price − ExPrice × rate| ≤ 2p). Free-typed
        /// ex-prices corrupted the live catalogue (47 known items, e.g. a £7.99 item with a
        /// £799.00 ex-price) and with them every downstream VAT figure. Existing bad rows are
        /// deliberately untouched — this blocks NEW damage only. Sync writes (isSync=true)
        /// are exempt so legacy tills replaying historic records don't dead-letter.
        /// </summary>
        private async Task<string> BandInconsistency(Guid businessId, int taxId, decimal price, decimal exPrice)
        {
            var tax = await RepositoryWrapper.TaxRepository.FindById(taxId, businessId);
            if (tax == null) return $"Unknown tax band {taxId}.";

            var expected = Math.Round(exPrice * (decimal)tax.Rate, 2);
            if (Math.Abs(price - expected) > 0.02m)
                return $"Price £{price:0.00} does not match ex-VAT £{exPrice:0.00} at band '{tax.Name}' " +
                       $"(expected ≈ £{expected:0.00}). Correct the ex-VAT price or pick the right band.";
            return null;
        }

        /// <summary>
        /// FE5.4: the till's barcode lookup goes through FindById, NOT the ItemParameters filter —
        /// so without this override a binned item could still be scanned and sold. A binned barcode
        /// now behaves like an unknown one (404), which the till already handles.
        /// `includeBinned=true` lets the portal's Bin view load a binned item for restore/inspection.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ MULTI-BARCODE (MB2): THIS IS THE TILL'S EXACT-SCAN DOOR, so it is where an alias
        /// resolves. On a miss, `id1` is looked up in `ItemBarcodes` and the ALIASED ITEM is
        /// returned — through the identical binned rules, so a binned item's alias 404s exactly as
        /// its own barcode does (plan D10).
        ///
        /// ⚠⚠ THE CALLER RECEIVES THE CANONICAL ITEM, whose `IdOne` is its own — never the scanned
        /// alias (plan D2). Every till then carries that canonical id onto the basket line, and the
        /// two silent faults an alias leak would cause (a phantom `StockLevel` from
        /// `StockProjectionConsumer`, a null VAT band from `VatBandStamp`) cannot arise.
        ///
        /// ⚠ Resolution is EXACT-after-trim, matching how `IdOne` itself matches here. No case
        /// folding (plan D6) — an alias must not match where the item's own barcode would not.
        /// </remarks>
        public override async Task<ActionResult<Item>> FindById([FromRoute] string id1, [FromHeader] Guid businessId)
        {
            var includeBinned = string.Equals(Request.Query["includeBinned"], "true", StringComparison.OrdinalIgnoreCase);

            var result = await base.FindById(id1, businessId);
            if (result.Result is OkObjectResult ok && ok.Value is Item item)
                return !includeBinned && item.BinnedAtUtc != null ? NotFound() : result;

            // Not an item's own barcode — is it one of its ADDITIONAL ones?
            var canonical = await CanonicalIdOneForAliasAsync(id1);
            if (canonical == null) return result;

            var aliased = await base.FindById(canonical, businessId);
            if (!includeBinned && aliased.Result is OkObjectResult aliasOk
                && aliasOk.Value is Item aliasItem && aliasItem.BinnedAtUtc != null)
                return NotFound();

            return aliased;
        }

        /// <summary>
        /// The item an additional barcode belongs to, or null when the string is not an alias.
        ///
        /// ⚠ Scoped by the ambient tenant automatically — `ItemBarcode` is in
        /// `MySqlDbContext.TenantOwned`, so the query filter is applied without this method having
        /// to remember it.
        /// </summary>
        private async Task<string> CanonicalIdOneForAliasAsync(string code)
        {
            var trimmed = Plutus.SharedKernel.ItemBarcodeRules.Normalise(code);
            if (trimmed == null) return null;

            return await _db.ItemBarcodes.AsNoTracking()
                .Where(b => b.Code == trimmed)
                .Select(b => b.ItemIdOne)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// ⚠⚠ SUPERVISOR AND ABOVE — Matt, 2026-08-21: *"I also need editing of items to be a
        /// supervisor and above permission across all tills."*
        ///
        /// ⚠⚠ THIS IS THE FIRST SERVER-SIDE GATE THIS CAPABILITY HAS EVER HAD. It inherited a bare
        /// <c>[Authorize]</c> from the legacy CRUD base, so **any signed-in user could create or edit
        /// any item** — and the web till's item editor had no client gate at all, while MAUI's was
        /// real but client-side only. A client-side gate is a suggestion.
        ///
        /// ⚠ EITHER code: <c>portal.prices.manage</c> keeps the portal working unchanged, and
        /// <c>pos.items.manage</c> is the till code a **Supervisor** actually holds — a supervisor
        /// holds no portal permission at all, which is why the portal code alone could never express
        /// "supervisor and above". Third instance of the <c>pos.stock.adjust</c> shape.
        ///
        /// ⚠ A CASHIER IS REFUSED, deliberately. A price is what the customer is charged; changing one
        /// unsupervised is a discount with no reason, no ceiling and no audit row.
        ///
        /// ⚠⚠ NEEDS AN OPERATOR TOKEN, NOT A DEVICE TOKEN. <c>perm:*</c> resolves RBAC by the token's
        /// <c>NameIdentifier</c>, which on a device token is the DEVICE id — and a device holds no
        /// grants, so a device token 403s here whatever the operator's role. Both tills already use
        /// their operator client for item writes. ⚠ The legacy <c>isSync=true</c> path is driven by no
        /// client in this repo (checked 2026-08-21); if anything ever drives it from a device, this is
        /// the line that will refuse it.
        /// </summary>
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage + "," + PermissionCatalogue.PosItemsManage)]
        public override async Task<ActionResult<Item>> Post([FromHeader] Guid businessId, [FromBody] ItemBody body, [FromQuery] bool isSync = false)
        {
            if (!isSync && body != null)
            {
                var problem = await BandInconsistency(body.BusinessId == Guid.Empty ? businessId : body.BusinessId, body.TaxId, body.Price, body.ExPrice);
                if (problem != null) return BadRequest(problem);

                // ⚠⚠ MULTI-BARCODE (MB2), THE REVERSE GUARD (plan D8). Without it, an unresolved
                // scan could mint a NEW item on a barcode that is already an alias — and then two
                // rows answer one scan, which is unresolvable at a counter. It is reachable from
                // both tills' "add this item" offers, which is exactly where a shop meets it.
                //
                // ⚠ It also gives the portal its check for free: `checkBarcodeFree` calls
                // GET /api/Item/{code}, which is now alias-aware.
                var alias = await CanonicalIdOneForAliasAsync(body.Id);
                if (alias != null)
                {
                    var owner = await _db.Items.AsNoTracking()
                        .Where(i => i.IdOne == alias).Select(i => i.Name).FirstOrDefaultAsync();

                    return Conflict($"That barcode already points at '{owner ?? alias}'. " +
                                    "Remove it from that item's barcodes first.");
                }
            }

            var result = await base.Post(businessId, body, isSync);

            // ⚠ The first row of the item's history — so the list opens with "Created", by whom and
            // when, rather than starting mid-story at the first edit.
            if (!isSync && body != null && result.Result is CreatedAtActionResult)
                await WriteAuditAsync("item.create", body.Id, new { name = body.Name, price = body.Price });

            return result;
        }

        /// <summary>⚠ Same gate as <see cref="Post"/> — see its remarks. Supervisor and above.</summary>
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage + "," + PermissionCatalogue.PosItemsManage)]
        public override async Task<ActionResult<Item>> Put([FromRoute] string id1, [FromHeader] Guid businessId, [FromBody] Item entity, [FromQuery] bool isSync = false)
        {
            if (!isSync && entity != null)
            {
                var problem = await BandInconsistency(businessId, entity.TaxId, entity.Price, entity.ExPrice);
                if (problem != null) return BadRequest(problem);
            }

            // ⚠⚠ READ THE BEFORE STATE FIRST — Matt, 2026-08-20: *"can there be a history kept of every
            // change to this item … logging time, what was changed and who by?"* A payload alone is the
            // DESTINATION with no way to know what moved, which is the exact shortcoming the customer
            // history records about its own older rows. So the audit carries `{before, after}`.
            //
            // ⚠ `AsNoTracking` matters: the base `Put` fetches and mutates the tracked entity, and a
            // tracked copy read here would BE that entity — before and after would then be identical.
            var before = isSync || entity == null
                ? null
                : await _db.Items.AsNoTracking()
                    .Where(i => i.IdOne == id1 && i.IdTwo == businessId)
                    .Select(i => Snapshot(i))
                    .FirstOrDefaultAsync();

            var result = await base.Put(id1, businessId, entity, isSync);

            // ⚠ Only when the write actually succeeded. An audit row for a rejected edit would put a
            // change in the history that never happened.
            if (before != null && result.Result is not BadRequestObjectResult && result.Result is not NotFoundResult)
                await WriteAuditAsync("item.update", id1, new { before, after = Snapshot(entity) });

            return result;
        }

        /// <summary>The fields a person edits, as the history reports them. ⚠ Money as the legacy
        /// decimal it is stored in — this is a record of what was typed, not an arithmetic path.</summary>
        private static object Snapshot(Item i) => new
        {
            name = i.Name,
            brand = i.Brand,
            desc = i.Desc,
            cost = i.Cost,
            price = i.Price,
            exPrice = i.ExPrice,
            taxId = i.TaxId,
            catId = i.CatId,
            stockUntracked = i.StockUntracked,
            binnedAtUtc = i.BinnedAtUtc,
        };

        /// <summary>
        /// One history row for this item.
        ///
        /// ⚠ Its own SaveChanges, because the base controller has already committed by the time we get
        /// here — there is no shared transaction left to join. A failed audit write must therefore not
        /// fail the edit: the change is already saved, and throwing here would tell the operator their
        /// successful save had failed. Swallowed deliberately, and the row is simply missing.
        /// </summary>
        private async Task WriteAuditAsync(string action, string itemIdOne, object detail)
        {
            try
            {
                _db.Audit(_tenant.TenantId, Actor, action, nameof(Item), itemIdOne, detail);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // History is a record OF the change, never a gate ON it.
            }
        }
    }
}
