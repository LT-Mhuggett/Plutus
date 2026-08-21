using System;
using System.Linq;
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
    // ⚠ These records are TWINS of the ones in Plutus.Contracts.Client (SyncContracts.cs), following
    // the convention already used by EnrolResult, DeviceTokenResult, StoreInfoResult and PingResult:
    // the contracts project has NO references and no packages, because it ships onto tills, so the
    // server declares its own copy rather than being referenced by it. Recorded in till-design C2 —
    // if you change one shape, change both.

    public sealed record HeartbeatRequest(
        Guid DeviceId,
        string? AppVersion,
        int OutboxDepth,
        long? OldestUnsyncedAgeSeconds,
        DateTime DeviceClockUtc);

    /// <summary>
    /// ⚠⚠ THIS IS A SECOND COPY OF THE WIRE CONTRACT. `Plutus.Contracts.Client.HeartbeatResult`
    /// declares the same shape, and the CLIENT deserialises against that one while the server
    /// serialises this one. Adding a field to one and not the other produces a field the till
    /// silently never sees, which is the quietest possible failure: no error, no log, just a feature
    /// that does nothing. Discovered on 2026-08-11 while adding the version fields.
    ///
    /// ⚠⚠ **IT IS NO LONGER MATCHED BY LUCK.** `HeartbeatContractTwinTests` compares the two
    /// parameter lists — names, types and order — and fails the build if they drift. WP-TICKETS
    /// (2026-08-21) added `UnreadSupportReplies` and walked straight into the trap this comment
    /// describes: the field went on the contract, the server kept compiling against this one, and
    /// the only reason it surfaced was a named-argument error. A test is cheaper than that luck.
    ///
    /// ⚠ If you touch either, touch both — and see C2 in `till-design.md`.
    /// </summary>
    public sealed record HeartbeatResult(
        string? CatalogueCursor,
        bool SyncNow,
        bool Locked,
        string? LockReason,
        DateTime ServerUtcNow,
        string? ExpectedMauiVersion = null,
        string? ExpectedWebVersion = null,
        int UnreadSupportReplies = 0,

        /// <summary>WP-TZ: the shop's timezone (IANA), or null. ⚠ The till renders its clock in
        /// it and WARNS when this PC disagrees — a PC on the wrong zone files sales under the
        /// wrong trading day, silently. ⚠ It does NOT change `BusinessDay`.</summary>
        string? StoreTimeZoneId = null);

    /// <summary>
    /// WP5 — POST /api/v1/heartbeat. A till says it is alive and collects whatever the platform
    /// wanted to tell it.
    ///
    /// ⚠ THE DIRECTION IS THE DESIGN. Tills sit on shop LANs behind NAT; nothing can reach them.
    /// So every instruction the platform has — resync, lock — waits in a mailbox on the Device row
    /// until the till asks. That is why "push a message to a till" is not a feature anywhere in this
    /// system, and why the beat is 60 seconds: it is also the worst-case latency of every such
    /// instruction.
    ///
    /// ⚠ Presence does NOT go to MySQL. See <see cref="TillPresence"/>.
    /// </summary>
    [ApiController]
    [Route("api/v1/heartbeat")]
    public sealed class HeartbeatController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly TillPresence _presence;

        public HeartbeatController(MySqlDbContext db, ITenantContext tenant, TillPresence presence)
        {
            _db = db;
            _tenant = tenant;
            _presence = presence;
        }

        /// <summary>
        /// ⚠ Gated <c>sales.ingest</c>, so a DEVICE token passes. A heartbeat that needed an
        /// operator token would stop the moment a shop closed for the night — which is exactly when
        /// you want to know a till is still standing.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Beat([FromBody] HeartbeatRequest body)
        {
            if (body == null || body.DeviceId == Guid.Empty) return BadRequest("deviceId is required.");

            var device = await _db.Devices.FirstOrDefaultAsync(
                d => d.Id == body.DeviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });

            _presence.Record(
                device.Id, device.TillId, body.AppVersion,
                body.OutboxDepth, body.OldestUnsyncedAgeSeconds, body.DeviceClockUtc);

            // ⚠ THE VERSION IS ALSO PERSISTED, and only when it CHANGES. Presence is in-memory on
            // purpose (a write per till per minute for data that expires in five), but a version is
            // not that kind of data: it changes on a deploy and is asked about most often for tills
            // that are switched off. Held only in memory, the fleet list forgot every version
            // whenever the backend restarted — which is precisely when somebody is looking.
            //
            // ⚠ Guarded on inequality so the common beat stays a pure read. Writing it every minute
            // would recreate the write path this design exists to avoid.
            //
            // ⚠⚠ THIS NEVER ONCE PERSISTED, from the day it shipped until 2026-08-10. The assignment
            // below was right, the comment above was right, and `SaveChangesAsync` was called ONLY
            // inside the `if (syncNow)` block underneath — so on every ordinary beat the mutation was
            // tracked and then thrown away with the DbContext. Six devices beating for two days, all
            // still reporting `AppVersion NULL`, while their SALES arrived perfectly: the till was
            // fine, the write was missing. A feature that reads as built and has never worked.
            var versionChanged = !string.IsNullOrWhiteSpace(body.AppVersion)
                                 && device.AppVersion != body.AppVersion;
            if (versionChanged)
            {
                device.AppVersion = body.AppVersion;
                device.AppVersionReportedAtUtc = DateTime.UtcNow;
            }

            // ⚠ Read the signals, then CLEAR SyncNow in the same round trip. It is a one-shot
            // instruction: leaving it set would have the till re-sync on every beat for ever, which
            // turns one operator click into a permanent load.
            var syncNow = device.SyncNow;
            if (syncNow) device.SyncNow = false;

            // ⚠⚠ THE DURABLE "LAST ONLINE", AND WHY IT IS THROTTLED. Matt, 2026-08-11: *"I expect
            // the 'Last online' to be updated via the heartbeat? It's showing tills last online days
            // ago?"* It was showing `Till.LastOnline` — written twice in the whole codebase, both at
            // ENROLMENT, updated by nothing — so every date in that column was the day the till was
            // created.
            //
            // ⚠ Presence stays the LIVE answer and stays in-process; this is only for what presence
            // cannot do — survive a restart, and speak for a till that is switched off. Written only
            // once it is older than `TillPresence.PersistEvery`, so a fleet costs one write per till
            // per five minutes rather than one per minute, which is the cost `TillPresence` was
            // built to avoid in the first place.
            var now = DateTime.UtcNow;
            var lastSeenStale = device.LastSeenUtc is null
                                || now - device.LastSeenUtc.Value >= TillPresence.PersistEvery;
            if (lastSeenStale) device.LastSeenUtc = now;

            // ⚠ ONE SAVE, covering EVERY reason to write. Keeping the save inside the `syncNow`
            // branch is what lost the version; keeping it unconditional would put a write on every
            // beat of every till, which is the thing `TillPresence` exists to avoid. Save when
            // something actually changed, and only then.
            if (versionChanged || syncNow || lastSeenStale)
            {
                _db.CurrentUser = "heartbeat"; // pitfall #1 — every save needs this, background paths included
                await _db.SaveChangesAsync();
            }

            // ⚠ THE BEAT IS WHERE "YOU ARE OUT OF DATE" HAS TO TRAVEL, because it is the only
            // channel there is: tills sit on shop LANs behind NAT and nothing can reach in, so every
            // instruction waits in a mailbox until the till asks. Matt, 2026-08-11: *"Does the
            // heartbeat from the till check for updates? All tills should do this."* It carried no
            // version at all until now.
            //
            // ⚠ ONE GLOBAL ROW, AND ITS ABSENCE IS THE DEFAULT. No row, or a blank version, means
            // the till says nothing — so this ships inert and stays inert until a platform admin
            // decides a build is "the one you should be on". The alternative (derive it from the
            // newest version any till has reported) would start nagging an estate the moment one
            // machine ran a test build.
            var release = await _db.TillReleaseSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);

            // ⚠⚠ THE SUPPORT BADGE RIDES THE BEAT (WP-TICKETS, 2026-08-21). Matt: *"When I reply to
            // a live ticket, how is the user informed? Does the heartbeat need to check for an
            // update?"* Yes — this is the only channel that reaches a till with nobody watching a
            // browser tab, and tills sit behind NAT so nothing can reach in.
            //
            // ⚠ COUNTED HERE RATHER THAN FETCHED BY THE TILL, because the till would have to poll a
            // second endpoint on its own timer to do it — a second cadence to get wrong, for a
            // number that is one join away from a request already being made every minute.
            //
            // ⚠⚠ NEVER FAILS THE BEAT. The heartbeat is what keeps a till trading: it renews the
            // token, carries the lock and triggers the drain. A support badge is the least important
            // thing on it, and an exception here would take all of that down with it.
            // ⚠ WP-TZ — one cheap column, read on every beat so a change in the portal reaches the
            // tills within a minute without anybody restarting anything.
            //
            // ⚠ NO TENANT PREDICATE, AND NONE IS POSSIBLE: `Business` carries no `TenantId` and is
            // therefore not in `TenantOwned`, so no query filter applies to it. It is a legacy table
            // in the tenant's own database and the whole codebase treats it as singular —
            // `CompaniesController`, `StoresController` and `LegacySaleBridgeConsumer` all take the
            // first row. Writing a predicate here would not compile, and inventing one would imply a
            // scoping this table does not have.
            var storeZone = await _db.Business.AsNoTracking()
                .Select(b => b.TimeZoneId)
                .FirstOrDefaultAsync();

            var unread = 0;
            try
            {
                unread = await UnreadSupportRepliesAsync(device.TenantId);
            }
            catch
            {
                // ⚠⚠ SWALLOWED, AND THIS IS THE ONE PLACE IT IS RIGHT TO. The heartbeat is what keeps
                // a till trading — it renews the token, carries the lock and triggers the drain — and
                // a support badge is the least important thing riding on it. Letting this throw would
                // stop a shop selling because their ticket count could not be read.
                //
                // ⚠ THE FAILURE MODE IS VISIBLE, WHICH IS WHY SILENCE IS ACCEPTABLE: a persistent
                // fault here shows up as a badge that never appears, and the desk itself still works
                // — `GET /api/v1/support/unread` answers the same question with a real status code
                // when somebody opens Help. There is no logger on this module's controllers to write
                // to, and adding a dependency to one for this would be out of proportion.
                unread = 0;
            }

            return Ok(new HeartbeatResult(
                CatalogueCursor: await CatalogueCursorAsync(),
                SyncNow: syncNow,
                Locked: device.Locked,
                LockReason: device.LockReason,
                ServerUtcNow: DateTime.UtcNow,
                ExpectedMauiVersion: Blank(release?.ExpectedMauiVersion),
                ExpectedWebVersion: Blank(release?.ExpectedWebVersion),
                UnreadSupportReplies: unread,

                // ⚠ WP-TZ (2026-08-22) — the shop's clock, so the till can render in it and NOTICE
                // when this PC's own timezone disagrees. A till on the wrong zone files sales under
                // the wrong trading day, silently, and nothing has ever checked.
                //
                // ⚠ ON THE BEAT because the till holds a DEVICE token: the company settings endpoint
                // is `portal.*`-gated and a device carries no portal grants. The beat is already how
                // the platform tells a till anything.
                StoreTimeZoneId: storeZone));

            // Empty string and null both mean "say nothing"; the wire carries one of them, not two.
            static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }


        /// <summary>
        /// How many of this tenant's tickets have an unread operator reply — WP-TICKETS, 2026-08-21.
        ///
        /// ⚠⚠ `SupportRules.IsUnreadByClient` DECIDES, not this. `SupportController` answers the same
        /// question on `/api/v1/support/unread` and both tills render the same badge; three
        /// implementations of "is this unread" would be three badges that disagree, and the one that
        /// disagreed would be the one nobody trusted.
        ///
        /// ⚠ `IgnoreQueryFilters` WITH AN EXPLICIT TENANT PREDICATE. The heartbeat runs on a DEVICE
        /// token, which carries no tenant context for the query filter to use — so the scope has to
        /// be stated, and stated once, right here.
        /// </summary>
        private async Task<int> UnreadSupportRepliesAsync(Guid tenantId)
        {
            var open = await _db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.Status != (byte)SupportStatus.Closed)
                .Select(t => new { t.Id, t.Status, t.ClientLastReadAtUtc })
                .ToListAsync();

            if (open.Count == 0) return 0;

            var ids = open.Select(t => t.Id).ToList();

            var last = (await _db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => ids.Contains(m.TicketId))
                    .Select(m => new { m.TicketId, m.FromOperator, m.AtUtc })
                    .ToListAsync())
                .GroupBy(m => m.TicketId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.AtUtc).First());

            return open.Count(t =>
                last.TryGetValue(t.Id, out var m)
                && SupportRules.IsUnreadByClient(t.Status, m.FromOperator, m.AtUtc, t.ClientLastReadAtUtc));
        }
        /// <summary>
        /// The newest catalogue change this tenant holds, as the same opaque cursor
        /// <c>/catalogue/changes</c> issues. A till compares it against its own and pulls if they
        /// differ — so the common case (nothing changed) costs one comparison and no second call.
        /// </summary>
        private async Task<string?> CatalogueCursorAsync()
        {
            var newest = await _db.Items.AsNoTracking()
                .OrderByDescending(i => i.ModifiedAt).ThenByDescending(i => i.IdOne)
                .Select(i => new { i.ModifiedAt, i.IdOne })
                .FirstOrDefaultAsync();

            return newest == null ? null : CatalogueCursor.Encode(newest.ModifiedAt, newest.IdOne);
        }
    }
}
