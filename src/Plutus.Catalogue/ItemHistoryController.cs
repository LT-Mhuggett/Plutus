#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Catalogue
{
    /// <summary>
    /// Everything that has happened to one item — the change history.
    ///
    /// ⚠⚠ MATT, 2026-08-20: *"can they be a history kept of every change to this item, from initial
    /// creation etc, logging time, what was changed and who by?"*
    ///
    /// ⚠⚠ **THE HISTORY STARTS WHEN THE LOGGING DID, AND THIS SAYS SO RATHER THAN PRETENDING.** Item
    /// edits were audited for the first time on 2026-08-20 — before that, `/api/Item` wrote no audit
    /// row at all, so for an item edited last month there is genuinely nothing to show and no way to
    /// reconstruct it. What the item DOES carry from before then is its own `CreatedAt`/`CreatedBy`
    /// and `ModifiedAt`/`ModifiedBy` stamps, so the list is bookended with those: a real "created"
    /// row, and — when the last modification predates the audit trail — one row saying the item was
    /// changed then and that what changed was not recorded. Inventing detail for those would be worse
    /// than the gap.
    ///
    /// ⚠ Modelled on `CustomersController`'s history endpoint deliberately: same merged-then-sorted
    /// shape, same `{before, after}` audit payload, same "never throw on one malformed old row".
    /// </summary>
    [ApiController]
    public sealed class ItemHistoryController : ControllerBase
    {
        private readonly MySqlDbContext _db;

        public ItemHistoryController(MySqlDbContext db) => _db = db;

        /// <summary>
        /// The item's history, newest first.
        ///
        /// ⚠ Gated on `portal.reports.view` — the same permission the Inventory list itself needs. It
        /// is a record of who changed a price, so it is not more sensitive than the price; but it names
        /// staff, which is why it is not simply `[Authorize]`.
        /// </summary>
        [HttpGet("api/v1/items/{itemIdOne}/history")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> History([FromRoute] string itemIdOne, [FromQuery] int take = 100)
        {
            take = Math.Clamp(take, 1, 500);

            var item = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne == itemIdOne)
                .Select(i => new { i.IdOne, i.CreatedAt, i.CreatedBy, i.ModifiedAt, i.ModifiedBy })
                .FirstOrDefaultAsync();

            if (item is null) return NotFound(new { detail = "That item no longer exists." });

            var events = new List<Row>();

            // ── 1. the audit trail: creation, field edits, and every barcode change ──────────────
            //
            // ⚠ Barcode rows are keyed on the CODE, not the item, so they are matched on the item id
            // inside their detail payload. Fetched by action prefix and filtered in memory, because
            // the payload is a JSON string the database cannot index into.
            var itemRows = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == nameof(Item) && a.EntityId == itemIdOne)
                .ToListAsync();

            foreach (var a in itemRows)
            {
                events.Add(a.Action == "item.create"
                    ? new Row(a.AtUtc, "Created", Created(a.DetailJson), a.ActorUserId)
                    : new Row(a.AtUtc, "Details changed", Changed(a.DetailJson), a.ActorUserId));
            }

            var barcodeRows = (await _db.AuditLogs.AsNoTracking()
                    .Where(a => a.EntityType == nameof(ItemBarcode))
                    .ToListAsync())
                .Where(a => MentionsItem(a.DetailJson, itemIdOne))
                .ToList();

            foreach (var a in barcodeRows)
            {
                var (type, detail) = a.Action switch
                {
                    "catalogue.item-barcode" => ("Barcode added", $"Added {a.EntityId}"),
                    "catalogue.item-barcode.remove" => ("Barcode removed", $"Removed {a.EntityId}"),
                    "catalogue.item-barcode.rename" => ("Barcode corrected", Renamed(a.DetailJson, a.EntityId)),
                    _ => ("Barcode changed", a.EntityId),
                };
                events.Add(new Row(a.AtUtc, type, detail, a.ActorUserId));
            }

            // ── 2. the bookends, for everything that happened before auditing existed ────────────
            //
            // ⚠ ONLY when the audit trail has nothing covering them. An item created today has a real
            // `item.create` row and must not also get a synthetic one.
            if (!events.Any(e => e.Type == "Created") && item.CreatedAt != default)
            {
                events.Add(new Row(item.CreatedAt, "Created",
                    "Created before change logging began — no detail was recorded.",
                    ParseActor(item.CreatedBy)));
            }

            // ⚠ A modification stamp LATER than every audited change is a real edit nobody logged.
            // Reported as one row that says exactly that, rather than being dropped (which would show
            // an item as untouched since creation while its price plainly moved) or dressed up with a
            // guess at what changed.
            if (item.ModifiedAt != default
                && item.ModifiedAt > item.CreatedAt.AddSeconds(1)
                && !events.Any(e => e.Type != "Created" && e.AtUtc >= item.ModifiedAt.AddSeconds(-1)))
            {
                events.Add(new Row(item.ModifiedAt, "Details changed",
                    "Changed before change logging began — what changed was not recorded.",
                    ParseActor(item.ModifiedBy)));
            }

            // ── 3. who each actor was ────────────────────────────────────────────────────────────
            //
            // ⚠ Resolved to a NAME, because a Guid answers nobody's question. ⚠ And an id that no
            // longer matches a person still shows the id rather than a blank: staff leave, and an
            // audit trail that renders "(unknown)" for them answers less than one that says who it was.
            var actorIds = events.Where(e => e.ActorUserId.HasValue).Select(e => e.ActorUserId.Value).Distinct().ToList();
            var names = actorIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Employees.AsNoTracking()
                    .Where(e => actorIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => $"{e.FName} {e.LName}".Trim());

            var rows = events
                .OrderByDescending(e => e.AtUtc)
                .Take(take)
                .Select(e => new
                {
                    atUtc = e.AtUtc,
                    type = e.Type,
                    detail = e.Detail,
                    by = e.ActorUserId is Guid id
                        ? (names.TryGetValue(id, out var n) && n.Length > 0 ? n : id.ToString())
                        : null,
                })
                .ToList();

            return Ok(new { total = events.Count, rows });
        }

        private sealed record Row(DateTime AtUtc, string Type, string Detail, Guid? ActorUserId);

        private static Guid? ParseActor(string createdBy) =>
            Guid.TryParse(createdBy, out var g) && g != Guid.Empty ? g : (Guid?)null;

        /// <summary>⚠ NEVER THROWS. A history screen that dies on one malformed row from years ago is
        /// worse than one that says "changed" for it — the `CustomersController` rule, verbatim.</summary>
        private static bool MentionsItem(string detailJson, string itemIdOne)
        {
            if (string.IsNullOrWhiteSpace(detailJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(detailJson);
                return doc.RootElement.TryGetProperty("itemIdOne", out var v)
                    && string.Equals(v.GetString(), itemIdOne, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string Created(string detailJson)
        {
            if (string.IsNullOrWhiteSpace(detailJson)) return "Item created";
            try
            {
                using var doc = JsonDocument.Parse(detailJson);
                return doc.RootElement.TryGetProperty("name", out var n)
                    ? $"Created as \"{n.GetString()}\""
                    : "Item created";
            }
            catch { return "Item created"; }
        }

        private static string Renamed(string detailJson, string fallback)
        {
            if (string.IsNullOrWhiteSpace(detailJson)) return fallback;
            try
            {
                using var doc = JsonDocument.Parse(detailJson);
                var root = doc.RootElement;
                return root.TryGetProperty("from", out var f) && root.TryGetProperty("to", out var t)
                    ? $"{f.GetString()} → {t.GetString()}"
                    : fallback;
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Turn an `item.update` payload into a sentence naming each field that moved.
        ///
        /// ⚠ FIELD BY FIELD, with old → new. "Details changed" on its own answers nothing — the whole
        /// point of the history is *what* changed, and a price is the field people come here about.
        /// </summary>
        private static string Changed(string detailJson)
        {
            if (string.IsNullOrWhiteSpace(detailJson)) return "Details changed";

            try
            {
                using var doc = JsonDocument.Parse(detailJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("before", out var before) || !root.TryGetProperty("after", out var after))
                    return "Details changed (what it was is not recorded)";

                var labels = new (string Field, string Label)[]
                {
                    ("name", "Name"), ("brand", "Brand"), ("desc", "Description"),
                    ("cost", "Cost"), ("price", "Price"), ("exPrice", "Ex-VAT price"),
                    ("taxId", "Tax band"), ("catId", "Category"),
                    ("stockUntracked", "Stock tracking"), ("binnedAtUtc", "Bin"),
                };

                var parts = new List<string>();
                foreach (var (field, label) in labels)
                {
                    var was = before.TryGetProperty(field, out var b) ? b.ToString() : "";
                    var now = after.TryGetProperty(field, out var a) ? a.ToString() : "";
                    if (was == now) continue;

                    parts.Add(string.IsNullOrEmpty(was)
                        ? $"{label} set to “{now}”"
                        : string.IsNullOrEmpty(now)
                            ? $"{label} cleared (was “{was}”)"
                            : $"{label} “{was}” → “{now}”");
                }

                // ⚠ A save that changed nothing IS a real event — somebody pressed Save. Saying so is
                // more useful than an empty row or a fabricated field.
                return parts.Count == 0 ? "Saved with no changes" : string.Join(" · ", parts);
            }
            catch
            {
                return "Details changed";
            }
        }
    }
}
