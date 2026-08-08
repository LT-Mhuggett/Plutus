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

namespace Plutus.Tenancy.Controllers
{
    /// <summary>Create a band, or restate its name/class. The RATE is never edited here — changing
    /// a rate is adding a point (see <see cref="VatRateChangeBody"/>), because the old rate has to
    /// stay true for the sales that were rung up under it.</summary>
    public sealed record VatBandBody(string Key, string DisplayName, string Class);

    /// <summary>A rate change: the same band, a new value, from a date. Never a mutation.</summary>
    public sealed record VatRateChangeBody(int RateBp, DateTime EffectiveFromUtc, string Note);

    /// <summary>WP2c-exempt: which band a legacy tax row means. Empty/null clears it back to
    /// rate-derivation.</summary>
    public sealed record VatTaxMappingBody(string Band);

    /// <summary>
    /// MAUI retrofit WP2c — THE PORTAL IS THE SOURCE OF VAT TRUTH.
    ///
    /// Matt's directive (2026-08-08): all VAT guidance comes from the portal, down to the tills. A
    /// till never decides a VAT rule; it receives bands, applies them, and reports what it charged.
    /// Before this controller there was no VAT surface at all — bands were seeded legacy `Taxes`
    /// rows, read-only in both frontends, and WP2b's `VatRatePoints` could only be changed by SQL.
    ///
    /// Two audiences, deliberately different shapes:
    ///   • <see cref="Published"/> (`GET /api/v1/vat/bands`, sales.ingest) is THE CONTRACT TILLS
    ///     READ. It ships the whole effective-dated history — including points that are not yet in
    ///     force — so a till that goes offline today still applies a rate change that lands next
    ///     week. Handing it only "the rates in force right now" would put the platform back where
    ///     WP2b started.
    ///   • <see cref="List"/> (`?admin=true`) is the editor's view: every point, with which one is
    ///     currently in force, for `perm:portal.company.manage` only.
    ///
    /// ⚠ THE BAND IS THE IDENTITY, NOT THE RATE. Standard moving 20% → 17.5% is one band changing
    /// value: two rows, same Band, different EffectiveFromUtc. Editing the rate in place would
    /// retrospectively rewrite what customers were charged.
    ///
    /// ⚠ CLASS IS NOT DERIVABLE FROM THE RATE. Zero-rated and exempt are both 0% to the customer
    /// and completely different in law (zero-rated is a taxable supply with input-tax recovery;
    /// exempt is not taxable and blocks recovery). That is why <see cref="VatClass"/> travels with
    /// the band and why this controller refuses to guess it.
    /// </summary>
    [ApiController]
    [Route("api/v1/vat")]
    public sealed class VatBandsController : ControllerBase
    {
        private const int MaxRateBp = 10000;     // 100% — a sanity ceiling, not a legal one
        private const int NoteCap = 200;

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public VatBandsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// THE PUBLISHED CONTRACT. Both tills cache this on the catalogue-sync cadence and apply it
        /// locally; neither may hold a hard-coded rate.
        ///
        /// ⚠ Ships FUTURE points too. A till offline across a rate change must be able to start
        /// charging the new rate on the day without reconnecting — that is the whole point of
        /// effective dating, and WP2b's quarantine is what happens when it can't.
        /// </summary>
        [HttpGet("bands")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Published()
        {
            var points = await PointsAsync();
            var now = DateTime.UtcNow;
            var taxBands = await ResolveTaxBandsAsync(points);

            // Group to bands so a till gets identity + class once, with its rate timeline attached.
            var bands = points
                .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.OrderByDescending(p => p.EffectiveFromUtc).First().RateBp)
                .Select(g =>
                {
                    var ordered = g.OrderBy(p => p.EffectiveFromUtc).ToList();
                    var latest = ordered[^1];
                    var current = ordered.LastOrDefault(p => p.EffectiveFromUtc <= now) ?? ordered[0];
                    return new
                    {
                        key = g.Key,
                        displayName = latest.DisplayName ?? g.Key,
                        vatClass = ((VatClass)latest.Class).ToString(),
                        // What to charge right now. A till that never re-syncs still gets the
                        // timeline below and can move itself on.
                        rateBp = current.RateBp,
                        effectiveFromUtc = current.EffectiveFromUtc,
                        rates = ordered.Select(p => new { rateBp = p.RateBp, effectiveFromUtc = p.EffectiveFromUtc }).ToArray(),
                        // WP2c-exempt: WHICH legacy tax rows mean this band. An item carries a
                        // TaxId, so this is how a till knows an item is EXEMPT rather than merely
                        // 0% — a distinction the rate can never carry.
                        legacyTaxIds = taxBands
                            .Where(kv => string.Equals(kv.Value, g.Key, StringComparison.OrdinalIgnoreCase))
                            .Select(kv => kv.Key).OrderBy(id => id).ToArray(),
                    };
                })
                .ToList();

            return Ok(new
            {
                asOfUtc = now,
                // Named so a till can log WHY it applied a rate; the basis text is the same one the
                // VAT return quotes, so portal, till and report never disagree about the rule.
                basis = VatGuidance.PricingBasis,
                bands,
            });
        }

        /// <summary>Every point, for the editor. Separate from <see cref="Published"/> because a
        /// till has no business seeing notes, ids, or superseded-point bookkeeping.</summary>
        [HttpGet("bands/admin")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var points = await PointsAsync();
            var now = DateTime.UtcNow;

            var bands = points
                .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var ordered = g.OrderBy(p => p.EffectiveFromUtc).ToList();
                    var latest = ordered[^1];
                    var current = ordered.LastOrDefault(p => p.EffectiveFromUtc <= now);
                    return new
                    {
                        key = g.Key,
                        displayName = latest.DisplayName ?? g.Key,
                        vatClass = ((VatClass)latest.Class).ToString(),
                        currentRateBp = current?.RateBp,
                        points = ordered.Select(p => new
                        {
                            id = p.Id,
                            rateBp = p.RateBp,
                            effectiveFromUtc = p.EffectiveFromUtc,
                            note = p.Note,
                            inForce = current != null && p.Id == current.Id,
                            pending = p.EffectiveFromUtc > now,
                        }).ToArray(),
                    };
                })
                .OrderBy(b => b.currentRateBp ?? int.MaxValue)
                .ToList();

            // WP2c-exempt: the tax rows items are priced against, and which band each one means.
            // A tenant with a zero-rated AND an exempt band cannot have this derived from the rate,
            // so unmapped rows are surfaced for a human to decide rather than quietly guessed.
            var taxBands = await ResolveTaxBandsAsync(points);
            var itemCounts = await _db.Items.AsNoTracking()
                .GroupBy(i => i.TaxId).Select(g => new { TaxId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TaxId, x => x.Count);
            var explicitMaps = await _db.VatBandTaxMaps.AsNoTracking()
                .ToDictionaryAsync(m => m.LegacyTaxId, m => m.Band);
            var taxRows = (await _db.Taxes.AsNoTracking().Select(t => new { t.IdOne, t.Name, t.Rate }).ToListAsync())
                .Select(t => new
                {
                    legacyTaxId = t.IdOne,
                    name = t.Name,
                    rateBp = (int)Math.Round((t.Rate - 1d) * 10000d),
                    band = taxBands.GetValueOrDefault(t.IdOne),
                    // Explicit = someone said so. Otherwise it was derived from the rate, and the
                    // editor shows that difference so an owner knows what they have and haven't
                    // actually decided.
                    mappedExplicitly = explicitMaps.ContainsKey(t.IdOne),
                    itemCount = itemCounts.GetValueOrDefault(t.IdOne),
                })
                .OrderBy(t => t.legacyTaxId)
                .ToList();

            var bandValues = bands
                .Select(b => new VatBand(b.key, b.displayName, Enum.Parse<VatClass>(b.vatClass),
                    b.currentRateBp ?? 0, now))
                .ToList();

            return Ok(new
            {
                asOfUtc = now,
                classes = Enum.GetNames<VatClass>(),
                bands,
                taxRows,
                // True once two bands share a rate — i.e. the tenant has both zero-rated and exempt
                // supplies. Until then the rate answers everything and the portal stays quiet.
                mappingRequired = VatBandResolution.NeedsExplicitMapping(bandValues),
                // The editor renders these verbatim — the rule and its citation ship together, so a
                // shopkeeper can see WHY the system does what it does without reading the source.
                guidance = VatGuidance.Rules,
            });
        }

        /// <summary>
        /// Create a band with its first rate point.
        ///
        /// The rate is a QUERY parameter rather than a body field on purpose: <see cref="VatBandBody"/>
        /// is shared with <see cref="Update"/>, and giving it a rate would make it possible to edit a
        /// rate in place through the update path — which is the one thing this whole model exists to
        /// prevent. A type that cannot express the mistake beats a check that catches it.
        /// </summary>
        [HttpPost("bands")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] VatBandBody body, [FromQuery] int rateBp)
        {
            var (key, cls, problem) = ValidateBand(body);
            if (problem != null) return problem;
            if (rateBp < 0 || rateBp > MaxRateBp)
                return BadRequest(new { detail = $"rateBp must be between 0 and {MaxRateBp} (2000 = 20%)." });
            if (!ClassAllowsRate(cls, rateBp, out var clash))
                return BadRequest(new { detail = clash });

            if (await _db.VatRatePoints.AnyAsync(p => p.Band == key))
                return Conflict(new { detail = $"A band '{key}' already exists. Add a rate change instead of recreating it." });

            _db.CurrentUser = Actor.ToString();
            var point = new VatRatePoint
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId,
                Band = key, DisplayName = body.DisplayName.Trim(), Class = (int)cls,
                RateBp = rateBp, EffectiveFromUtc = DateTime.UnixEpoch,
                Note = "Band created in the portal.",
            };
            _db.VatRatePoints.Add(point);
            _db.Audit(_tenant.TenantId, Actor, "vat.band.create", nameof(VatRatePoint), key,
                new { key, body.DisplayName, vatClass = cls.ToString(), rateBp });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/vat/bands/{key}", new { key });
        }

        /// <summary>
        /// Rename / reclassify a band. Applies to EVERY point of the band, because the name and
        /// class describe the band, not one moment of it.
        ///
        /// ⚠ Reclassifying is the expensive edit, not the cosmetic one: Zero → Exempt silently
        /// stops input tax being recoverable on everything sold in that band. It is allowed (it is
        /// occasionally the correct fix — Kapow's comics were mislabelled Exempt when the law
        /// zero-rates them) but it is audited with both values.
        /// </summary>
        [HttpPut("bands/{key}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] string key, [FromBody] VatBandBody body)
        {
            var points = await _db.VatRatePoints.Where(p => p.Band == key).ToListAsync();
            if (points.Count == 0) return NotFound();

            if (string.IsNullOrWhiteSpace(body?.DisplayName) || body.DisplayName.Trim().Length > 60)
                return BadRequest(new { detail = "displayName is required (max 60 chars)." });
            if (!Enum.TryParse<VatClass>(body.Class, ignoreCase: true, out var cls))
                return BadRequest(new { detail = $"class must be one of: {string.Join(", ", Enum.GetNames<VatClass>())}." });
            foreach (var p in points)
                if (!ClassAllowsRate(cls, p.RateBp, out var clash))
                    return BadRequest(new { detail = clash });

            _db.CurrentUser = Actor.ToString();
            var was = new { displayName = points[0].DisplayName, vatClass = ((VatClass)points[0].Class).ToString() };
            foreach (var p in points)
            {
                p.DisplayName = body.DisplayName.Trim();
                p.Class = (int)cls;
            }
            _db.Audit(_tenant.TenantId, Actor, "vat.band.update", nameof(VatRatePoint), key,
                new { was, now = new { displayName = body.DisplayName.Trim(), vatClass = cls.ToString() } });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Record a rate change: a NEW point on an existing band.
        ///
        /// ⚠ REFUSES A DATE IN THE PAST. Back-dating a rate would retrospectively invalidate sales
        /// already recorded under the old rate — WP2b judges each line against the rates in force at
        /// its `OccurredAtUtc`, so a past-dated point turns settled history into stale-band
        /// quarantine. If a rate really was wrong historically, that is an error correction on the
        /// return (HMRC Notice 700/45), not an edit to the band timeline.
        /// </summary>
        [HttpPost("bands/{key}/rate-changes")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddRateChange([FromRoute] string key, [FromBody] VatRateChangeBody body)
        {
            var points = await _db.VatRatePoints.Where(p => p.Band == key).ToListAsync();
            if (points.Count == 0) return NotFound();

            if (body == null) return BadRequest(new { detail = "A rate change is required." });
            if (body.RateBp < 0 || body.RateBp > MaxRateBp)
                return BadRequest(new { detail = $"rateBp must be between 0 and {MaxRateBp} (2000 = 20%)." });
            if (!ClassAllowsRate((VatClass)points[0].Class, body.RateBp, out var clash))
                return BadRequest(new { detail = clash });
            if (body.Note != null && body.Note.Length > NoteCap)
                return BadRequest(new { detail = $"note too long (max {NoteCap} chars)." });

            var from = DateTime.SpecifyKind(body.EffectiveFromUtc, DateTimeKind.Utc);
            if (from <= DateTime.UtcNow)
                return BadRequest(new
                {
                    detail = "A rate change must be dated in the future. Sales already recorded were rung up "
                           + "under the current rate; back-dating would make them look non-compliant without "
                           + "changing what the customer actually paid. Correct a historical error on the VAT "
                           + "return (HMRC Notice 700/45) instead.",
                });
            if (points.Any(p => p.EffectiveFromUtc == from))
                return Conflict(new { detail = $"Band '{key}' already has a rate change dated {from:yyyy-MM-dd HH:mm} UTC." });

            // A band always has a point at or before now in practice (Create and the legacy seed
            // both start at epoch, and an in-force point can never be cancelled) — but this must
            // not throw for a band seeded oddly by hand, so it degrades to "no current rate".
            var superseded = points.Where(p => p.EffectiveFromUtc <= DateTime.UtcNow)
                .OrderBy(p => p.EffectiveFromUtc).LastOrDefault();
            if (superseded != null && superseded.RateBp == body.RateBp && !points.Any(p => p.EffectiveFromUtc > DateTime.UtcNow))
                return BadRequest(new { detail = $"Band '{key}' is already {body.RateBp}bp — that change would do nothing." });

            _db.CurrentUser = Actor.ToString();
            var point = new VatRatePoint
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId,
                Band = key, DisplayName = points[^1].DisplayName, Class = points[0].Class,
                RateBp = body.RateBp, EffectiveFromUtc = from,
                Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            };
            _db.VatRatePoints.Add(point);
            _db.Audit(_tenant.TenantId, Actor, "vat.rate-change.add", nameof(VatRatePoint), point.Id.ToString(),
                new { band = key, fromBp = superseded.RateBp, toBp = body.RateBp, effectiveFromUtc = from, body.Note });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/vat/bands/{key}", new { id = point.Id, effectiveFromUtc = from });
        }

        /// <summary>
        /// Cancel a rate change that has NOT taken effect yet — the escape hatch for a typo'd date
        /// or rate, which otherwise could only be fixed by out-scheduling it with another change.
        ///
        /// ⚠ Refuses once the point is in force: by then tills have charged it and sales exist that
        /// only it explains. Deleting it would convert them into stale-band quarantine.
        /// </summary>
        [HttpDelete("bands/{key}/rate-changes/{id:guid}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelRateChange([FromRoute] string key, [FromRoute] Guid id)
        {
            var point = await _db.VatRatePoints.FirstOrDefaultAsync(p => p.Band == key && p.Id == id);
            if (point == null) return NotFound();
            if (point.EffectiveFromUtc <= DateTime.UtcNow)
                return BadRequest(new
                {
                    detail = "That rate is already in force — tills have been charging it, and sales exist "
                           + "that only it explains. Schedule a further change instead of removing it.",
                });

            _db.CurrentUser = Actor.ToString();
            _db.VatRatePoints.Remove(point);
            _db.Audit(_tenant.TenantId, Actor, "vat.rate-change.cancel", nameof(VatRatePoint), id.ToString(),
                new { band = key, rateBp = point.RateBp, effectiveFromUtc = point.EffectiveFromUtc });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// WP2c-exempt: legacy tax id → band, for every tax row this tenant has.
        ///
        /// The portal's explicit mapping wins; otherwise the row's RATE picks the band, which is a
        /// complete answer for every band that is unambiguous at its rate. A tax row that is
        /// genuinely ambiguous — the zero-vs-exempt case — resolves to nothing and is reported as
        /// unmapped rather than guessed, because guessing here is the mistake that costs money.
        /// </summary>
        private async Task<Dictionary<int, string>> ResolveTaxBandsAsync(List<VatRatePoint> points)
        {
            var now = DateTime.UtcNow;
            var bands = points
                .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var ordered = g.OrderBy(p => p.EffectiveFromUtc).ToList();
                    var current = ordered.LastOrDefault(p => p.EffectiveFromUtc <= now) ?? ordered[0];
                    return new VatBand(g.Key, ordered[^1].DisplayName ?? g.Key,
                        (VatClass)ordered[^1].Class, current.RateBp, current.EffectiveFromUtc);
                })
                .ToList();

            var maps = await _db.VatBandTaxMaps.AsNoTracking()
                .ToDictionaryAsync(m => m.LegacyTaxId, m => m.Band);
            var taxes = await _db.Taxes.AsNoTracking().Select(t => new { t.IdOne, t.Rate }).ToListAsync();

            var resolved = new Dictionary<int, string>();
            foreach (var t in taxes)
            {
                var bp = (int)Math.Round((t.Rate - 1d) * 10000d);
                var band = VatBandResolution.Resolve(bands, maps.GetValueOrDefault(t.IdOne), bp);
                if (band != null) resolved[t.IdOne] = band;
            }
            return resolved;
        }

        /// <summary>
        /// Say which band a legacy tax row means — the edit that makes EXEMPT usable.
        ///
        /// ⚠ WHY THIS IS NEEDED AT ALL: items are priced against legacy <c>Taxes</c> rows carrying a
        /// name and a multiplier, so a band could only be inferred from the rate — and the rate
        /// cannot tell zero-rated from exempt, since both are 0%. A shop that sells exempt supplies
        /// has to be able to SAY so; the difference decides whether input tax on the related costs
        /// is recoverable (HMRC Notice 706).
        ///
        /// ⚠ Changes only affect sales rung up AFTERWARDS. Lines already recorded carry the band they
        /// were sold under, and rewriting them would restate filed returns behind the owner's back.
        /// </summary>
        [HttpPut("tax-mapping/{legacyTaxId:int}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MapTax([FromRoute] int legacyTaxId, [FromBody] VatTaxMappingBody body)
        {
            if (!await _db.Taxes.AnyAsync(t => t.IdOne == legacyTaxId))
                return NotFound(new { detail = $"No tax row {legacyTaxId} in this business." });

            var existing = await _db.VatBandTaxMaps.FirstOrDefaultAsync(m => m.LegacyTaxId == legacyTaxId);
            _db.CurrentUser = Actor.ToString();

            // An empty band clears the mapping and falls back to rate-derivation.
            if (string.IsNullOrWhiteSpace(body?.Band))
            {
                if (existing != null) _db.VatBandTaxMaps.Remove(existing);
                _db.Audit(_tenant.TenantId, Actor, "vat.tax-mapping.clear", nameof(VatBandTaxMap),
                    legacyTaxId.ToString(), new { was = existing?.Band });
                await _db.SaveChangesAsync();
                return NoContent();
            }

            var band = body.Band.Trim().ToLowerInvariant();
            if (!await _db.VatRatePoints.AnyAsync(p => p.Band == band))
                return BadRequest(new { detail = $"No VAT band '{band}'. Create it first." });

            // The rate has to agree, or the mapping would declare a rate the items are not priced
            // at — an item editor accepts a price pair against its Tax row's multiplier, and a
            // band claiming a different rate would make every one of those items off-band.
            var taxRate = await _db.Taxes.Where(t => t.IdOne == legacyTaxId).Select(t => t.Rate).FirstAsync();
            var taxBp = (int)Math.Round((taxRate - 1d) * 10000d);
            var bandBp = await _db.VatRatePoints.Where(p => p.Band == band && p.EffectiveFromUtc <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveFromUtc).Select(p => p.RateBp).FirstOrDefaultAsync();
            if (Math.Abs(taxBp - bandBp) > VatAccounting.BandSnapToleranceBp)
                return BadRequest(new
                {
                    detail = $"Tax row {legacyTaxId} is {taxBp}bp but band '{band}' is {bandBp}bp. "
                           + "Mapping them would make every item on that tax row off-band. Map it to a "
                           + "band at the same rate, or change the items' prices first.",
                });

            if (existing == null)
                _db.VatBandTaxMaps.Add(new VatBandTaxMap
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId, LegacyTaxId = legacyTaxId, Band = band,
                });
            else
                existing.Band = band;

            _db.Audit(_tenant.TenantId, Actor, "vat.tax-mapping.set", nameof(VatBandTaxMap),
                legacyTaxId.ToString(), new { was = existing?.Band, now = band });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// This tenant's points, seeded from the legacy <c>Taxes</c> rows the first time anyone
        /// asks if <c>VatRatePoints</c> is empty.
        ///
        /// The seed exists so the published contract is never blank: a till that reads no bands has
        /// nothing to apply, and WP2b's guard treats an empty history as "skip", so an unseeded
        /// tenant would silently lose both the contract and the check. `Taxes` carries a multiplier
        /// and a name and NO class, so the class is inferred — the one place in the system that
        /// guesses, and it is a migration step, visible and editable the moment the portal opens.
        /// </summary>
        private async Task<List<VatRatePoint>> PointsAsync()
        {
            var points = await _db.VatRatePoints.AsNoTracking().ToListAsync();
            if (points.Count > 0) return points;

            var legacy = await _db.Taxes.AsNoTracking().Select(t => new { t.Name, t.Rate }).ToListAsync();
            if (legacy.Count == 0) return points;

            var seeded = legacy
                .Select(t => new { t.Name, Bp = (int)Math.Round((t.Rate - 1d) * 10000d) })
                // A multiplier below 1.0 is not a VAT rate; seeding it would create a band the
                // editor can never save (a 0% class with a negative rate). Leave it behind.
                .Where(t => t.Bp >= 0)
                .GroupBy(t => t.Bp).Select(g => g.First())
                .Select(t => new VatRatePoint
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId,
                    Band = KeyFromLegacy(t.Name, t.Bp),
                    DisplayName = t.Name ?? $"{t.Bp / 100m:0.##}%",
                    Class = (int)ClassFromLegacy(t.Name, t.Bp),
                    RateBp = t.Bp, EffectiveFromUtc = DateTime.UnixEpoch,
                    Note = "Seeded from the legacy Taxes table (WP2c). Check the class before filing.",
                })
                .ToList();
            if (seeded.Count == 0) return points;

            _db.CurrentUser = Actor == Guid.Empty ? "vat-band-seed" : Actor.ToString();
            _db.VatRatePoints.AddRange(seeded);
            _db.Audit(_tenant.TenantId, Actor, "vat.bands.seed", nameof(VatRatePoint), "legacy-taxes",
                new { count = seeded.Count, bands = seeded.Select(s => new { s.Band, s.RateBp, s.Class }) });
            try
            {
                await _db.SaveChangesAsync();
                return seeded;
            }
            catch (DbUpdateException)
            {
                // ⚠ Two tills polling this endpoint at the same moment on an unseeded tenant both
                // read empty and both insert; the (TenantId, Band, EffectiveFromUtc) unique index
                // fails one of them. Losing that race is fine — the other one seeded the same rows
                // from the same source — so drop our copies and read back what won. Without this,
                // one till gets a 500 on the busiest possible morning: the first one after a deploy.
                foreach (var e in _db.ChangeTracker.Entries<VatRatePoint>().ToList()) e.State = EntityState.Detached;
                return await _db.VatRatePoints.AsNoTracking().ToListAsync();
            }
        }

        private static string KeyFromLegacy(string name, int bp) =>
            bp >= 1000 ? VatRateHistory.Standard
            : bp > 0 ? VatRateHistory.Reduced
            : Mentions(name, "exempt") ? "exempt"
            : VatRateHistory.Zero;

        /// <summary>⚠ A 0% legacy row named "Exempt" is taken at its word here — but that is a
        /// LABEL, not a legal fact, and Kapow's was wrong (books are zero-rated, Notice 701/10).
        /// The seed note says to check it; the portal editor is where it gets corrected.</summary>
        private static VatClass ClassFromLegacy(string name, int bp) =>
            bp >= 1000 ? VatClass.Standard
            : bp > 0 ? VatClass.Reduced
            : Mentions(name, "exempt") ? VatClass.Exempt
            : VatClass.Zero;

        private static bool Mentions(string name, string word) =>
            name != null && name.Contains(word, StringComparison.OrdinalIgnoreCase);

        /// <summary>The one rule that IS derivable: a class with a legally fixed rate cannot carry a
        /// different one. Zero/Exempt/OutsideScope are 0% by definition — a "zero-rated" band at 20%
        /// is a data-entry error that would misreport every sale in it.</summary>
        private static bool ClassAllowsRate(VatClass cls, int rateBp, out string detail)
        {
            detail = null;
            var mustBeZero = cls is VatClass.Zero or VatClass.Exempt or VatClass.OutsideScope;
            if (mustBeZero && rateBp != 0)
            {
                detail = $"A {cls} band charges no VAT, so its rate must be 0 (given {rateBp}bp).";
                return false;
            }
            if (!mustBeZero && rateBp == 0)
            {
                detail = $"A {cls} band at 0% is a contradiction. Use Zero (a taxable supply at 0%, "
                       + "input tax recoverable) or Exempt (not a taxable supply, input tax NOT recoverable).";
                return false;
            }
            return true;
        }

        private static (string Key, VatClass Class, IActionResult Problem) ValidateBand(VatBandBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Key) || body.Key.Trim().Length > 40)
                return (null, default, new BadRequestObjectResult(new { detail = "key is required (max 40 chars)." }));
            var key = body.Key.Trim().ToLowerInvariant();
            if (!key.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                return (null, default, new BadRequestObjectResult(new { detail = "key may contain letters, digits, '-' and '_' only." }));
            if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Trim().Length > 60)
                return (null, default, new BadRequestObjectResult(new { detail = "displayName is required (max 60 chars)." }));
            if (!Enum.TryParse<VatClass>(body.Class, ignoreCase: true, out var cls))
                return (null, default, new BadRequestObjectResult(
                    new { detail = $"class must be one of: {string.Join(", ", Enum.GetNames<VatClass>())}." }));
            return (key, cls, null);
        }
    }
}
