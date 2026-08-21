using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Connectivity;
using Plutus.Frontend.AppClient.Services.Storage;

namespace Plutus.Frontend.AppClient.Services.Sync
{
    /// <summary>
    /// The till's one background clock (cutover step 13): beat, drain, pull, poll — every 60
    /// seconds, the same cadence the web till runs (`App.tsx`).
    ///
    /// ⚠ ONE TIMER, NOT FOUR. Four independent timers drift into each other, and the day they
    /// coincide the till fires four HTTP calls at once behind a rate limit sized for one. Worse,
    /// each would need its own copy of "are we connected?" and they would answer differently.
    ///
    /// ⚠ NOTHING HERE MAY EVER BLOCK SELLING, and nothing here may ever surface a dialog. Every
    /// call inside the tick swallows its own failures by contract; this loop additionally catches,
    /// because a background task that throws on a MAUI dispatcher takes the app with it. A till
    /// whose heartbeat has failed for a week must still ring up the next customer.
    ///
    /// ⚠ A FAILING HEARTBEAT IS SILENT AND NEVER STOPS SELLING. Presence is a convenience for the
    /// portal's fleet list. The outbox is what keeps the till CORRECT, and it retries for ever.
    /// </summary>
    public static class TillCadence
    {
        /// <summary>The web till's interval. ⚠ Changing it changes the fleet list's idea of "late".</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        private static CancellationTokenSource _running;
        private static readonly object Lock = new();

        /// <summary>
        /// ⚠ Set by the till while a basket is open. A catalogue sync mid-basket rewrites the prices
        /// under the operator's hands — a line added before the tick and one added after would come
        /// from different price lists, in one sale, and the receipt would be the only evidence.
        /// The outbox drain and the heartbeat are NOT suppressed: neither touches the catalogue,
        /// and a queued sale should not wait for a customer to finish paying.
        /// </summary>
        public static Func<bool> BasketIsOpen { get; set; }

        /// <summary>
        /// Who is signed in right now, or null. ⚠ A delegate rather than a reference, for the same
        /// reason `BasketIsOpen` is one: this class must not take a dependency on the UI layer, and
        /// the answer changes without it being told.
        /// </summary>
        public static Func<Guid?> SignedInOperatorId { get; set; }

        /// <summary>
        /// Called when the signed-in operator is no longer on a roster THE SERVER ANSWERED WITH —
        /// deactivated in the portal, or their last `pos.*` permission removed.
        ///
        /// ⚠ Matt, 2026-08-11: *"If a user is disabled, the user needs immediately logging out with
        /// an information message."* This is that signal. The cadence does not sign anybody out
        /// itself — it has no UI and must never raise a dialog (see the class header); the shell
        /// owns what "log out" means.
        /// </summary>
        public static Action<string> OperatorRevoked { get; set; }

        /// <summary>
        /// Told when the PLATFORM has revoked this DEVICE (WP4, step 21) — not the operator, the
        /// till itself.
        ///
        /// ⚠ Separate from <see cref="OperatorRevoked"/> because the two need different words. An
        /// operator is told their account is disabled and to see their manager; a revoked till must
        /// say it is the TILL, or somebody tries another login, then another, and concludes the
        /// staff accounts are broken.
        ///
        /// ⚠ Wired beside `Start()` in `App.xaml.cs`, never off a screen — the check must keep
        /// working after everybody is signed out, which is exactly when a stolen till is revoked.
        /// </summary>
        public static Action<string> DeviceRevoked { get; set; }

        /// <summary>The last tick's outcome, for the Plutus tab to display. Never null after a tick.</summary>
        public static string LastResult { get; private set; } = "Not started.";

        /// <summary>
        /// The build the platform says this till should be on, when this till is BEHIND it — null
        /// otherwise, which is the normal case.
        ///
        /// ⚠ Set from the heartbeat every tick, so it clears itself the moment the till is updated
        /// or a platform admin changes the expected version. Nothing has to remember to reset it.
        /// ⚠ **ADVISORY.** Nothing may refuse to sell, refuse to sync or interrupt a basket because
        /// of this. Matt, 2026-08-11: *"No self update for MAUI"* — the till cannot fetch its own
        /// replacement, so a gate would leave a shop stuck with no way forward. Somebody copies the
        /// new build over; this is only how they find out they need to.
        /// </summary>
        public static string UpdateAvailable { get; private set; }

        /// <summary>
        /// Raised after every tick, so a screen showing queued state can redraw.
        ///
        /// ⚠ Matt, 2026-08-11: *"The open float was 'Waiting' and never updated. I navigated away
        /// and back onto the cash tab and it had updated."* The DATA was right — the drain worked
        /// and the queue cleared — but the Cash screen only ever redrew in `OnAppearing`, so the
        /// operator stared at "(waiting to send)" against something already sent, and the only way
        /// to find out was to leave the page and come back.
        ///
        /// ⚠ AN EVENT, NOT A SECOND TIMER. This class exists because four timers drift into each
        /// other and fire together behind a rate limit sized for one — a screen adding its own
        /// refresh clock would be the fifth. The tick already knows when something changed; it just
        /// never said so.
        ///
        /// ⚠ RAISED FROM <see cref="TickAsync"/>, so it covers EVERY tick — the timer's, the Plutus
        /// tab's "sync now", and the one the cash screen nudges the instant it records a float. That
        /// last one is the case that matters: recording writes "(waiting to send)", the nudged tick
        /// clears it, and until now nothing told the screen.
        ///
        /// ⚠ HANDLERS MUST NOT THROW AND MUST NOT ASSUME A UI THREAD. This fires from the
        /// background loop, and an escape here would take the app down from a thread with no
        /// handler — so the raise is wrapped, and a subscriber that touches UI marshals itself.
        /// ⚠ Subscribers must UNSUBSCRIBE when their screen goes away, or a static event holds
        /// every page the till has ever shown alive for the life of the process.
        /// </summary>
        public static event Action Ticked;

        /// <summary>
        /// Start ticking. ⚠ Idempotent — sign-in, a connectivity change and a resume all call this,
        /// and starting twice would double every call the loop makes.
        /// </summary>
        public static void Start()
        {
            lock (Lock)
            {
                if (_running is not null) return;
                _running = new CancellationTokenSource();
            }

            _ = Task.Run(() => RunAsync(_running.Token));
        }

        /// <summary>
        /// Stop ticking — teardown, and tests.
        ///
        /// ⚠ DELIBERATELY NOT CALLED ON SIGN-OUT. A till sitting on its login screen at the end of a
        /// shift still holds the day's sales in its outbox, and they must keep draining whether or
        /// not anybody is standing at the counter. Stopping the clock when the last operator signs
        /// out would strand a full day's takings until somebody signed back in.
        /// </summary>
        public static void Stop()
        {
            CancellationTokenSource stopping;
            lock (Lock)
            {
                stopping = _running;
                _running = null;
            }

            stopping?.Cancel();
            stopping?.Dispose();
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            // ⚠ Ticks IMMEDIATELY, then waits. A till that has just been opened has sales from
            // yesterday's close in its outbox, and making it wait a minute to send them is a minute
            // of a shift where the platform's numbers are knowingly wrong.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // ⚠ `Ticked` is raised inside `TickAsync`, NOT here — see its header. Raising it
                    // from the loop would redraw on the timer and miss every nudged tick, which
                    // includes the one the cash screen fires the instant it records a float.
                    await TickAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // ⚠ The loop SURVIVES. An unhandled exception here would silently stop every
                    // sync on the till for as long as it stays open, and the only symptom would be
                    // sales quietly not arriving.
                    Analytics.CrashLog.Write("TillCadence.Tick", ex);
                }

                try
                {
                    await Task.Delay(Interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// One tick. Exposed so a test — and the Plutus tab's "sync now" — runs exactly what the
        /// timer runs, rather than a second sequence that can drift from it.
        /// </summary>
        public static async Task<string> TickAsync(CancellationToken ct = default)
        {
            // ⚠ NEVER THROWS. The loop catches too, but this is also what the Plutus tab's "sync
            // now" calls, and a raw exception there is a crash dialog in a shop. Everything inside
            // is best-effort by contract; the failure an operator needs is a sentence, not a stack.
            try
            {
                return await RunTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("TillCadence.TickAsync", ex);
                return LastResult = "Couldn't sync with Plutus. See the Plutus tab's log.";
            }
            finally
            {
                // ⚠ HERE, NOT IN THE LOOP, so EVERY tick redraws — the timer's, the Plutus tab's
                // "sync now", and the one the cash screen nudges the moment it records a float. That
                // last one is the whole point: recording writes `(waiting to send)`, the nudged tick
                // sends it, and nothing then told the screen. Raised from the loop instead, the
                // label would have sat wrong for up to a further minute.
                //
                // ⚠ IN A `finally`, so a tick that FAILED still redraws. The screen's job is to show
                // what is true, and "still waiting to send" is true — leaving the last render up
                // because the network was down is how a stale figure outlives the fault behind it.
                //
                // ⚠ AND IN ITS OWN GUARD. A screen's redraw is not worth the loop: a subscriber that
                // throws must not be able to stop the heartbeat, the drain or the catalogue for
                // every tick after it.
                try { Ticked?.Invoke(); }
                catch (Exception ex) { Analytics.CrashLog.Write("TillCadence.Ticked", ex); }
            }
        }

        private static async Task<string> RunTickAsync(CancellationToken ct)
        {
            var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
            if (credentials?.DeviceId is not Guid deviceId)
                return LastResult = "Not connected to Plutus.";

            // ⚠ THE SHARED client. This tick alone used to build FOUR token providers — here, plus
            // one each inside the outbox drain, the catalogue sync and the gateway-surcharge read —
            // and each provider caches its own token, so each one MINTED. `/api/v1/tokens/device`
            // allows 5 a minute per IP, so a healthy till exhausted its own allowance in the first
            // minute and everything afterwards failed with 429, which reads exactly like being
            // revoked. Seen in the wild 2026-08-09.
            var api = await PlutusApi.GetAsync(ct).ConfigureAwait(false);
            if (api is null) return LastResult = "The server address doesn't look right.";

            // 1. Beat. ⚠ Never by minting a device token — `POST /api/v1/tokens/device` is rate
            // limited to 5/min/IP, so polling it makes a healthy till report itself revoked, and
            // tills sharing one public IP do it to each other.
            //
            // ⚠ TIME-LIMITED, because the beat goes through the SHARED store gate. `TillStoreAccess`
            // serialises every caller behind one semaphore, so a screen holding a slow or wedged
            // read does not merely hang itself — it parks the heartbeat behind it, and the till
            // vanishes from the fleet list while still selling perfectly well. That reads as "the
            // till is off", which is the opposite of the truth. A beat that cannot get to the store
            // within half a minute is one the platform is better off missing.
            using var beatDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            beatDeadline.CancelAfter(TimeSpan.FromSeconds(30));

            var beat = await TillStoreAccess.UseAsync(
                store => new SyncClient(api, store).BeatAsync(deviceId, AppVersion(), beatDeadline.Token),
                beatDeadline.Token).ConfigureAwait(false);

            // ⚠ IS THIS TILL BEHIND? Matt, 2026-08-11: *"Does the heartbeat from the till check for
            // updates? All tills should do this."* It now does — the beat returns the build the
            // platform expects, and `SyncClient` has already compared it against what this till
            // reported. Null unless genuinely older, so no news is the silent case.
            //
            // ⚠ RECORDED, NOT ACTED ON. There is no self-update for MAUI (Matt's decision) — the
            // till is an unpackaged .exe that cannot fetch its own replacement — so the only useful
            // thing it can do is SAY SO. Nothing here interrupts a sale, and nothing refuses to
            // work: a till that stopped selling over a version number would strand a shop with no
            // route out.
            UpdateAvailable = beat.UpdateAvailable;

            // 2. Drain. ⚠ BEFORE the catalogue pull: a sale already rung up is worth more than a
            // price that has not been asked for yet, and on a slow link the catalogue can take the
            // whole interval.
            var push = await OutboxPushService.PushAsync(ct).ConfigureAwait(false);

            // 2b. Drain the CASH queue too (WP9). ⚠ A shop opens before its broadband does, so the
            // opening float, a paid-out and the Z-close are all recorded locally and sent later —
            // the money moved whether or not the platform heard about it, and a float that never
            // banked is a day that cannot be reconciled.
            var cash = await CashPushService.PushAsync(ct).ConfigureAwait(false);

            // 3. Pull the catalogue, when the platform says it has moved and no basket is open.
            var catalogue = "";
            if (beat.CatalogueStale || beat.SyncNow)
            {
                if (BasketIsOpen?.Invoke() == true)
                    catalogue = " Catalogue update held until this basket is finished.";
                else
                    catalogue = " " + (await CatalogueSyncService.SyncAsync(ct).ConfigureAwait(false)).Message;
            }

            // 4. Refresh the published VAT bands and their WHOLE dated timeline.
            //
            // ⚠ NOTHING HAS EVER CALLED THIS, on any till, and the visible symptom is the blank Tax
            // column. `VatBandCache.RefreshAsync` is the only thing that writes `MetaKeys.VatBands`;
            // every reader — `VatBands.BandKeyForItemAsync`, `DisplayNameForItemAsync`,
            // `RateBpAtAsync` — loads from that key. With it never written they all correctly return
            // null for ever, so the Tax column renders blank on every basket line on every MAUI till
            // in the field. The cache was built and tested at WP2c; it just had no caller.
            //
            // ⚠ EVERY TICK, matching the web till (binding default 10: when in doubt, match it —
            // `till-design` C1 records it caching "the bands and their whole effective-dated
            // timeline at boot + every 60 s"). Two tills on different cadences would classify the
            // same sale differently on the day a rate changes.
            //
            // ⚠ THE TIMELINE, NOT TODAY'S RATE. Caching only the current rate is a bug the C1 row
            // calls out by name: the timeline is what lets an offline till apply a future-dated
            // change ON THE DAY, without hearing from anyone.
            //
            // ⚠ Never blocks the tick. A VAT refresh that fails is a Tax column that stays as it
            // was; a till must keep selling regardless, and the outbox has already drained above.
            try
            {
                await TillStoreAccess.UseAsync(
                    store => new VatBandCache(api, new Plutus.Client.Storage.MetaVatBandStore(store))
                        .RefreshAsync(ct),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Analytics.CrashLog.Write("TillCadence.VatBands", ex);
            }

            // 4a. The shop's SCHEDULED DISCOUNT RULES — "Wednesday Warhammer".
            //
            // ⚠⚠ THE WHOLE RULE, SCHEDULE INCLUDED, never "the discounts that apply today". The till
            // decides whether it is Wednesday against its own clock at the moment of the sale, which
            // is what makes a Wednesday rule work on a till that has been offline since Monday. A
            // pre-evaluated answer cached here would apply Monday's discounts all week.
            //
            // ⚠ Beside the VAT bands on purpose: both are "the portal decides, the till obeys" feeds
            // whose whole timeline has to reach the till, and putting them on different cadences is
            // how two tills come to disagree about the same basket.
            //
            // ⚠ Never blocks the tick. A rules refresh that fails leaves the last-known-good in place;
            // the worst case is a customer charged the shelf price, which the operator can fix at the
            // counter, and the alternative — a till that stops selling over a promotions feed — is not
            // a trade anybody would make.
            try
            {
                await TillStoreAccess.UseAsync(
                    store => new DiscountRuleCache(api, new Plutus.Client.Storage.MetaDiscountRuleStore(store))
                        .RefreshAsync(ct),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Analytics.CrashLog.Write("TillCadence.DiscountRules", ex);
            }

            // 4b. The store's RECEIPT LAYOUT, set in the portal (step 26).
            //
            // ⚠ REFRESHED ON THE CADENCE, NEVER AT PRINT TIME. A printer job must not wait on the
            // network — a receipt is the one thing an operator is standing there watching for — so
            // the template is fetched here and read from the cache when the paper is built.
            //
            // ⚠ Never blocks the tick, and a failure keeps the LAST-KNOWN template rather than
            // reverting a shop's receipt mid-day.
            await Printing.ReceiptBranding.RefreshAsync(ct).ConfigureAwait(false);

            // 4b2. The PORTAL'S THEME (FE10 / step 22).
            //
            // ⚠ Matt, 2026-08-17: *"Changing the theme in the portal needs to be consistent across the
            // web and MAUI till."* Same endpoint, same 60s cadence and the same seven slots as the web
            // till — the server resolves till > group > store > tenant > default, so neither till
            // decides anything about colour.
            //
            // ⚠ A failed fetch keeps the LAST-KNOWN theme. Reverting a shop's branding mid-day because
            // a poll failed would be a visible fault caused by an invisible one.
            await Theming.Theming.RefreshAsync(ct).ConfigureAwait(false);

            // 4b3. The CARRIER BAGS the portal set (ruling 2026-08-19).
            //
            // ⚠ Matt: *"That creates the 5p and 20p bags at the back and that pushes down to the
            // tills."* This is the push. It replaces a per-device `Preferences` value, so a shop that
            // changes its bag charge changes it once and every till follows within the minute.
            //
            // ⚠ A failed fetch keeps the LAST-KNOWN bags. It never invents one: see
            // `Services.Sales.CarrierBags` for why this is the one cache that fails towards NOTHING
            // rather than towards a default — a guessed price charges a customer money the shop never
            // set.
            await Sales.CarrierBags.RefreshAsync(ct).ConfigureAwait(false);

            // 4c. The NOTICEBOARD — pick-from-floor notes and platform announcements (WP5b).
            //
            // ⚠ `Plutus.Client.Core.NoticesClient` shipped 2026-08-09 with 23 tests and was
            // referenced exactly once in this app, in a comment. This is the caller it was waiting
            // for — the rules were never the missing part.
            //
            // ⚠ EVERY TICK, matching the web till, which polls both feeds on the same 60s beat.
            //
            // ⚠ A FAILED POLL DOES NOT CLEAR THE BOARD — see `Noticeboard.Apply`. An empty answer
            // and no answer are different things, and confusing them takes a live incident banner
            // off the screen the first time the shop's broadband blinks.
            await Notices.Noticeboard.RefreshAsync(ct).ConfigureAwait(false);

            // 5. Re-read WHO MAY USE THIS TILL, and what they may do.
            //
            // ⚠ NOTHING DID THIS ON A CADENCE UNTIL 2026-08-11, and Matt spotted it: *"MAUI getting
            // updates from the portal should happen all the time. E.g. if an account is disabled,
            // or new permissions added, should refresh immediately."* The roster was refreshed at
            // the LOGIN SCREEN and by a manual button on the Plutus tab — and nowhere else. So a
            // till left signed in through a shift never re-read permissions at all: a role change
            // made in the portal reached it at the next sign-in, which on a quiet day is tomorrow.
            //
            // ⚠ IT ALSO CARRIES THE REVOCATION CHECK. A deactivated employee DROPS OUT of the
            // roster server-side (`TillOperatorsController` filters on `e.Active`, and again on
            // holding at least one `pos.*` permission), so "gone from the roster" is the signal for
            // both "disabled" and "no longer allowed at a counter".
            //
            // ⚠⚠ ONLY FROM A ROSTER THE SERVER ACTUALLY SENT. `RefreshRosterAsync` returns null
            // when it could not ask, and `OperatorRevocation.Check` treats null as "carry on" —
            // because treating it as "not on the list" would sign the whole shop out, mid-sale,
            // every time the broadband hiccuped, with a message accusing the operator of being
            // disabled. That is a worse outage than the one this prevents.
            //
            // ⚠ Never blocks the tick, and never throws: a roster refresh that fails leaves the
            // cached one in place, which is what makes offline sign-in work.
            try
            {
                if (await TillPlacement.TillIdAsync(api).ConfigureAwait(false) is Guid rosterTill)
                {
                    var roster = await new Plutus.Client.Core.OperatorSync(api, new DbOperatorStore())
                        .RefreshRosterAsync(rosterTill, ct).ConfigureAwait(false);

                    if (Plutus.Client.Core.OperatorRevocation.Check(SignedInOperatorId?.Invoke(), roster)
                        != Plutus.Client.Core.RevocationReason.None)
                    {
                        OperatorRevoked?.Invoke(Plutus.Client.Core.OperatorRevocation.DisabledMessage);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Analytics.CrashLog.Write("TillCadence.Roster", ex);
            }

            // 6. ⚠⚠ IS THIS TILL STILL A TILL? (WP4, step 21.)
            //
            // Device tokens are HMAC bearer tokens with **no server-side denylist**, so revoking a
            // device in the portal does NOT invalidate the token it already holds: a lost or stolen
            // till keeps selling for up to its **12h TTL** unless it asks. This poll is the only
            // revocation signal that reaches a till, and until now nothing on the beat asked —
            // `ConnectivityProbe` knew how, but only ran at sign-in and on the Plutus tab.
            //
            // ⚠ ASKED VIA DEVICE STATUS, NOT BY MINTING A TOKEN. The token endpoint is rate-limited
            // to 5/min per IP, so polling it would make a healthy till report itself revoked (429) —
            // and tills sharing one shop's public IP would do it to each other.
            //
            // ⚠⚠ ONLY AN EXPLICIT `Revoked` STOPS ANYTHING — see `DeviceRevocation`. A failed poll,
            // a 401/403, a 404 and an unknown status all keep trading, because stopping a shop on a
            // network blip is a worse outage than the one this prevents. `PendingRemoval` trades on
            // purpose too: halting a till the moment somebody *requests* it back would make
            // un-enrolment a way to take a shop down.
            try
            {
                var credential = await Connectivity.SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);

                // 6b. FE3.0 — tell the platform what hardware agent this till has.
                //
                // ⚠ The WEB TILL has done this since FE3.0 and MAUI never did, so every MAUI till read
                // "agent unknown" on the portal's fleet list beside browser tills that reported
                // properly — which looks like a missing agent rather than missing reporting.
                //
                // ⚠ NOT EVERY TICK. `AgentReporting.ShouldSend` — shared with the web till — sends on
                // a CHANGE or every 6 hours, because this is telemetry the server overwrites in place
                // and the beat is 60s. It is also cheap when it declines: the agent poll is loopback.
                if (credential?.DeviceId is Guid agentDeviceId)
                    await Hardware.AgentReporter.ReportAsync(api, agentDeviceId, ct).ConfigureAwait(false);

                // ⚠ A till with no device credential has nothing to be revoked — it has never been
                // enrolled, and the login screen already handles that case.
                if (credential?.DeviceId is Guid standingDeviceId)
                {
                    var (code, status) = await api.GetDeviceStatusAsync(standingDeviceId, ct).ConfigureAwait(false);

                    if (Plutus.Client.Core.DeviceRevocation.MustStop(
                            Plutus.Client.Core.DeviceRevocation.Check(code, status)))
                    {
                        DeviceRevoked?.Invoke(Plutus.Client.Core.DeviceRevocation.RevokedMessage);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // ⚠ A poll that threw is not a revocation. Never blocks the tick.
                Analytics.CrashLog.Write("TillCadence.DeviceStanding", ex);
            }

            var locked = beat.Locked ? $" ⚠ This till has been locked: {beat.LockReason}" : "";

            return LastResult = beat.Delivered
                ? $"{push.Message}{cash}{catalogue}{locked}"
                : $"Can't reach Plutus. {push.Message}{cash}";
        }

        /// <summary>
        /// Which BUILD this till is running, for the heartbeat and the portal's fleet list.
        ///
        /// ⚠ NOT `AppInfo.VersionString`. On Windows that reads the PACKAGE manifest whenever the
        /// app is packaged — and `Package.appxmanifest` carries a hardcoded `1.0.0.0` that the MAUI
        /// build does not override. So an MSIX install reported **1.0.0.0** while the very same
        /// source, run unpackaged, reported 1.12.0: the number in the portal would have depended on
        /// how the till was installed, which is worse than no number at all.
        ///
        /// The ASSEMBLY's informational version is stamped from `versions/till-maui.txt` by
        /// `Directory.Build.targets` and is identical either way. It also carries the commit —
        /// `1.12.0+e1c0ef2…` — and the short hash is kept, because "which build is this exactly" is
        /// the question the fleet list exists to answer and a three-part version cannot answer it
        /// between two builds of the same version.
        /// </summary>
        private static string AppVersion()
        {
            try
            {
                var informational = typeof(TillCadence).Assembly
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (string.IsNullOrWhiteSpace(informational))
                    return typeof(TillCadence).Assembly.GetName().Version?.ToString();

                // "1.12.0+e1c0ef2096de…" → "1.12.0+e1c0ef2". A full 40-char hash in a fleet-list
                // chip is unreadable; seven is what every git UI shows and is enough to identify.
                var plus = informational.IndexOf('+');
                return plus < 0
                    ? informational
                    : informational[..Math.Min(informational.Length, plus + 8)];
            }
            catch
            {
                return null;
            }
        }
    }
}
