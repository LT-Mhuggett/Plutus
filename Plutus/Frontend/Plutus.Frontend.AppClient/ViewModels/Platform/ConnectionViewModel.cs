using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Connectivity;
using Plutus.Frontend.AppClient.Services.Storage;
using Plutus.Client.Storage;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.ViewModels.Platform
{
    /// <summary>
    /// The Plutus tab: connect this till to the platform, and prove it.
    ///
    /// ⚠ WHY THIS SCREEN EXISTS. The MAUI retrofit built a transport spine — enrolment, device
    /// tokens, sale ingest, heartbeat, catalogue sync — that until now had no way in from the app.
    /// Everything was provable in tests and invisible on a device. This is the surface that makes
    /// it testable by a person: point it at a server, enrol, and watch each layer answer.
    ///
    /// It is diagnostics, not a sales screen. Nothing here blocks selling and nothing here is on a
    /// timer — every action is a button, because a screen that fires background work while someone
    /// is reading it cannot be reasoned about when it misbehaves.
    /// </summary>
    internal class ConnectionViewModel : BaseViewModel
    {
        private SecureDeviceCredentialStore? _credentials;

        private string _serverUrl;
        private string _enrolmentCode;
        private string _connectionSummary = "Not checked yet.";
        private string _connectionDetail;
        private Color _connectionColour = Colors.Gray;
        private string _deviceSummary = "Not enrolled.";
        private string _lastAction;
        private bool _busy;

        public ConnectionViewModel(bool firstRun = false)
        {
            Title = firstRun ? "Connect to Plutus" : "Plutus";
            Icon = "md-cloud";
            ShowContinue = firstRun;
            _serverUrl = ServerUrlSetting;
            TillVersionText = $"MAUI till v{PlutusVersion.Of(typeof(App).Assembly)}";
            LogPath = Services.Analytics.CrashLog.TodaysFile;
            _ = InitialiseAsync();
        }

        /// <summary>Only on first run: the route on to sign-in. Inside the shell the operator is
        /// already signed in and the button would be nonsense.</summary>
        public bool ShowContinue { get; }

        /// <summary>Where the local crash log is, so nobody has to guess when reporting a fault.</summary>
        public string LogPath { get; }

        #region Bound state

        /// <summary>⚠ MAUI needs this and the web till never did — a browser till is served from
        /// the origin it talks to, so its address is implicit. An installed app has to be told.</summary>
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (!SetProperty(ref _serverUrl, value)) return;
                ServerUrlSetting = value;
            }
        }

        public string EnrolmentCode
        {
            get => _enrolmentCode;
            set => SetProperty(ref _enrolmentCode, value);
        }

        public string ConnectionSummary
        {
            get => _connectionSummary;
            private set => SetProperty(ref _connectionSummary, value);
        }

        public string ConnectionDetail
        {
            get => _connectionDetail;
            private set => SetProperty(ref _connectionDetail, value);
        }

        public Color ConnectionColour
        {
            get => _connectionColour;
            private set => SetProperty(ref _connectionColour, value);
        }

        /// <summary>Device id + server-side status, or why there is none.</summary>
        public string DeviceSummary
        {
            get => _deviceSummary;
            private set => SetProperty(ref _deviceSummary, value);
        }

        /// <summary>The result of the last button pressed — the screen's whole feedback channel.</summary>
        public string LastAction
        {
            get => _lastAction;
            private set => SetProperty(ref _lastAction, value);
        }

        public bool Busy
        {
            get => _busy;
            private set
            {
                if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(NotBusy));
            }
        }

        /// <summary>Bound directly by the buttons' IsEnabled.
        /// ⚠ Deliberately a property rather than <c>{Binding Busy, Converter={StaticResource
        /// InverseBoolConverter}}</c>: that converter EXISTS in this app but was never registered in
        /// App.xaml, so the binding would throw a XamlParseException the moment this tab opened.</summary>
        public bool NotBusy => !_busy;

        public string TillVersionText { get; }

        #endregion

        #region Commands

        private Command _checkCommand;
        public Command CheckCommand => _checkCommand ??= new Command(async () => await CheckAsync());

        private Command _enrolCommand;
        public Command EnrolCommand => _enrolCommand ??= new Command(async () => await EnrolAsync());

        private Command _beatCommand;
        public Command BeatCommand => _beatCommand ??= new Command(async () => await BeatAsync());

        private Command _catalogueCommand;
        public Command CatalogueCommand => _catalogueCommand ??= new Command(async () => await CatalogueAsync());

        private Command _syncStaffCommand;
        public Command SyncStaffCommand => _syncStaffCommand ??= new Command(async () => await SyncStaffAsync());

        private Command _sendSalesCommand;
        public Command SendSalesCommand => _sendSalesCommand ??= new Command(async () => await SendSalesAsync());

        private Command _forgetCommand;
        public Command ForgetCommand => _forgetCommand ??= new Command(async () => await ForgetAsync());

        private Command _continueCommand;
        public Command ContinueCommand => _continueCommand ??= new Command(Continue);

        #endregion

        /// <summary>
        /// On to sign-in.
        ///
        /// ⚠ Deliberately does NOT require enrolment. A till with no connection still has to be
        /// usable — that is the whole offline principle — and blocking here would strand anyone
        /// whose broadband is down on the one screen that cannot help them.
        /// </summary>
        private void Continue()
        {
            try
            {
                App.Current.MainPage = new Views.LoginView();
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("ConnectionViewModel.Continue", ex);
                LastAction = $"Couldn't open the sign-in screen: {ex.Message}";
            }
        }

        private async Task InitialiseAsync()
        {
            try
            {
                _credentials = await SecureDeviceCredentialStore.LoadAsync();
                DescribeDevice();
                await CheckAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                LastAction = $"Couldn't read this till's identity: {ex.Message}";
            }
        }

        /// <summary>
        /// Turn an exception into something worth showing on a shop floor, and put the real one in
        /// the crash log where it is useful.
        ///
        /// ⚠ This exists because a raw <c>ex.Message</c> reached an operator as *"This instance has
        /// already started one or more requests. Properties can only be modified before sending the
        /// first request."* — true, and completely unactionable. .NET plumbing messages describe the
        /// framework's problem, never the person's.
        /// </summary>
        private string Friendly(string what, Exception ex)
        {
            Logger.LogError(ex);
            Services.Analytics.CrashLog.Write($"ConnectionViewModel: {what}", ex);

            var because = ex switch
            {
                HttpRequestException => "the server couldn't be reached. Check the address above, and that this machine is online.",
                TaskCanceledException => "the server didn't answer in time.",
                UriFormatException => "that server address isn't valid.",
                _ => "something unexpected went wrong. The details are in the log file shown at the bottom of this screen.",
            };
            return $"{what} — {because}";
        }

        private void DescribeDevice() =>
            DeviceSummary = _credentials?.DeviceId is Guid id
                ? $"Device {id}"
                : "Not enrolled — enter an enrolment code from the portal.";

        /// <summary>Build the API client for the address currently in the box, with the token
        /// provider attached so authenticated calls work.</summary>
        private PlutusApiClient? Api(out string error)
        {
            error = null;

            // ⚠ Never mutate BaseAddress — see PlutusHttp. Doing so is what made every call after
            // the first connection check fail with "This instance has already started one or more
            // requests", which is a sentence no operator should ever be shown.
            var http = PlutusHttp.TryFor(ServerUrl);
            if (http is null)
            {
                error = "That server address doesn't look right. It should be like https://plutus.yourcompany.com";
                return null;
            }

            var bootstrap = new PlutusApiClient(http);
            var tokens = _credentials is null ? null : new DeviceTokenProvider(bootstrap, _credentials);
            return new PlutusApiClient(http, tokens);
        }

        private async Task CheckAsync()
        {
            if (Busy) return;
            Busy = true;
            ConnectionSummary = "Checking…";
            ConnectionColour = Colors.Gray;
            try
            {
                var api = Api(out var error);
                if (api is null)
                {
                    ConnectionSummary = error;
                    ConnectionColour = Colors.OrangeRed;
                    return;
                }

                var probe = new ConnectivityProbe(api, _credentials, new MauiNetworkAvailability());
                var status = await probe.CheckAsync();

                ConnectionSummary = status.Summary;
                ConnectionColour = TillConnectionCheck.ColourFor(status);
                ConnectionDetail = status.ClockSuspect
                    ? $"⚠ This till's clock is out by {status.ClockSkew:hh\\:mm\\:ss}. {status.Detail}".Trim()
                    : status.Detail;

                if (status.DeviceStatus is not null && _credentials?.DeviceId is Guid id)
                    DeviceSummary = $"Device {id} — {status.DeviceStatus}";
            }
            catch (Exception ex)
            {
                ConnectionSummary = Friendly("Couldn't check the connection", ex);
                ConnectionColour = Colors.Gray;
            }
            finally { Busy = false; }
        }

        private async Task EnrolAsync()
        {
            if (Busy) return;
            if (string.IsNullOrWhiteSpace(EnrolmentCode))
            {
                LastAction = "Enter the enrolment code shown in the portal.";
                return;
            }

            Busy = true;
            try
            {
                var api = Api(out var error);
                if (api is null) { LastAction = error; return; }

                _credentials ??= await SecureDeviceCredentialStore.LoadAsync();

                // ⚠ THROUGH EnrolmentFlow, not straight at the API. Calling api.EnrolAsync
                // directly — which this did until 2026-08-09 — saved the credential and told the
                // v2 store NOTHING: no TillId, no StoreId, no BusinessId, no ServerUrl. Every
                // screen that needs to know which store it is had nothing to read, and the failure
                // was silent because enrolment itself succeeded.
                //
                // The flow also enforces the archive gate (binding default 9.3): a till holding an
                // un-archived legacy database refuses to enrol, because that file is the shop's
                // history and the translation agent's only input.
                // ⚠ legacyDatabasePath is deliberately NULL until cutover step 21. Passing the real
                // path switches on the archive gate, and nothing in this build can archive yet —
                // step 21 turns Settings' "backup" into "Archive legacy database" and stamps
                // MetaKeys.LegacyArchivedAtUtc. Enabling the gate first would refuse enrolment with
                // no way through it, on every till that has ever opened its legacy file — which is
                // all of them, because the Database constructor CREATES one on first touch.
                var deviceId = await TillStoreAccess.UseAsync(store =>
                    new EnrolmentFlow(store, api, _credentials)
                        .EnrolAsync(ServerUrl, EnrolmentCode.Trim(), legacyDatabasePath: null));

                // Placement is a second call on purpose: enrolment says WHICH TILL, the platform
                // says which store that till currently sits in — and a till can be moved later.
                await TillPlacement.RefreshAsync(api);

                var tillId = await TillPlacement.TillIdAsync(api);
                // Mirrored into Preferences for the readers that still look there.
                if (tillId is Guid t) _credentials.SaveTillId(t);

                EnrolmentCode = string.Empty; // one-time code — leaving it on screen invites a retry
                                              // that can only ever fail with 410 Gone.
                var storeId = await TillPlacement.StoreIdAsync();
                LastAction = storeId is int s
                    ? $"Enrolled. Till {tillId}, store {s}."
                    : $"Enrolled device {deviceId}. ⚠ Couldn't read this till's store yet — check the connection and press Check again.";
                DescribeDevice();
                await CheckAsync();
            }
            catch (EnrolmentBlockedException ex)
            {
                // The archive gate, or a missing server address. Already written for a person.
                LastAction = ex.Message;
            }
            catch (EnrolmentFailedException ex)
            {
                // 410 Gone = reused/expired/unknown. Already written for a person to read.
                LastAction = ex.Message;
            }
            catch (Exception ex)
            {
                LastAction = Friendly("Couldn't enrol this till", ex);
            }
            finally { Busy = false; }
        }

        private async Task BeatAsync()
        {
            if (Busy) return;
            if (_credentials?.DeviceId is not Guid deviceId)
            {
                LastAction = "Enrol this till first.";
                return;
            }

            Busy = true;
            try
            {
                var api = Api(out var error);
                if (api is null) { LastAction = error; return; }

                // A real till beats on a 60s timer with its own outbox depth. This is the same call
                // with a stub store, so the round trip is testable before the local store is wired.
                var sync = new SyncClient(api, new NullSyncStore());
                var outcome = await sync.BeatAsync(deviceId, PlutusVersion.Of(typeof(App).Assembly));

                LastAction = outcome.Delivered
                    ? $"Heartbeat OK. syncNow={outcome.SyncNow}, locked={outcome.Locked}" +
                      (outcome.LockReason is null ? "" : $" ({outcome.LockReason})")
                    : "Heartbeat didn't reach the server. ⚠ That is deliberately harmless — a till " +
                      "keeps selling and keeps queueing when the heartbeat fails.";
            }
            catch (Exception ex)
            {
                LastAction = Friendly("Heartbeat failed", ex);
            }
            finally { Busy = false; }
        }

        private async Task CatalogueAsync()
        {
            if (Busy) return;
            Busy = true;
            try
            {
                // ⚠ THIS USED TO BE A DIAGNOSTIC ONLY. It fetched five items, reported them, and
                // stored NOTHING — so a till could truthfully say "catalogue reachable" while
                // holding an empty catalogue, and anyone searching it found nothing. That is
                // exactly what happened on the 2026-08-09 screen test, and the button's wording
                // gave no hint that it had not saved a thing.
                //
                // It now runs the REAL sync (WP5), applies every page, and reports what landed.
                var result = await Services.Storage.CatalogueSyncService.SyncAsync();
                LastAction = result.Message;
            }
            catch (Exception ex)
            {
                LastAction = Friendly("Couldn't read the catalogue", ex);
            }
            finally { Busy = false; }
        }

        /// <summary>
        /// Send whatever this till has queued, now.
        ///
        /// ⚠ THE ONLY PLACE A STUCK QUEUE IS VISIBLE. The cadence drains every 60s and says nothing
        /// when it works, which is right — but a till that has been offline for a day, or that is
        /// holding sales Plutus REFUSED, looks exactly like a healthy one from the sales screen.
        /// This is what somebody presses when the portal's figures do not match the drawer.
        /// </summary>
        private async Task SendSalesAsync()
        {
            if (Busy) return;
            Busy = true;
            try
            {
                var result = await Services.Storage.OutboxPushService.PushAsync();
                LastAction = result.Message;
            }
            catch (Exception ex)
            {
                LastAction = Friendly("Couldn't send this till's sales", ex);
            }
            finally { Busy = false; }
        }

        /// <summary>
        /// WP8 — pull this till's staff down so they can sign in offline.
        ///
        /// ⚠ Needs the TILL id, which enrolment returns and which is not the device id. Held in
        /// Preferences by <see cref="SecureDeviceCredentialStore"/> at enrolment.
        /// </summary>
        private async Task SyncStaffAsync()
        {
            if (Busy) return;
            // ⚠ A DEVICE ID is what "enrolled" means. The till id is a separate fact that a device
            // paired before 2026-08-08 18:27 never stored, and refusing those as un-enrolled sent
            // someone to re-pair a machine that was already correctly paired.
            if (_credentials?.DeviceId is not Guid deviceId)
            {
                LastAction = "Enrol this till first — staff are synced per till.";
                return;
            }

            Busy = true;
            try
            {
                var api = Api(out var error);
                if (api is null) { LastAction = error; return; }

                // One resolver, Meta-first, with the legacy fallbacks inside it (see TillPlacement).
                var tillId = await TillPlacement.TillIdAsync(api);
                if (tillId is not Guid till)
                {
                    LastAction = "This till is enrolled, but Plutus hasn't said which till it is yet. "
                               + "Check the connection above and try again.";
                    return;
                }

                var count = await new OperatorSync(api, new FileOperatorStore()).RefreshAsync(till);
                LastAction = count is int n
                    ? n == 0
                        ? "Synced, but no staff are assigned to this till yet. Check their roles in the portal."
                        : $"Synced {n} staff account(s). They can now sign in on this till, online or off."
                    // ⚠ The existing roster is deliberately kept on failure: replacing a good one
                    // with nothing because the wifi dropped would lock a shop out of its own till.
                    : "Couldn't reach the staff list. Anything already synced is still usable.";
            }
            catch (Exception ex)
            {
                LastAction = Friendly("Couldn't sync staff", ex);
            }
            finally { Busy = false; }
        }

        private async Task ForgetAsync()
        {
            if (Busy) return;
            var confirmed = await App.Current.MainPage.DisplayAlert(
                "Forget this till?",
                "This device will stop being enrolled and will need a new code from the portal. " +
                "Sales already queued on this machine are NOT deleted.",
                "Forget", "Cancel");
            if (!confirmed) return;

            _credentials?.Clear();
            DescribeDevice();
            LastAction = "Local device identity cleared.";
            await CheckAsync();
        }

        /// <summary>
        /// A stand-in store for the diagnostics heartbeat.
        ///
        /// ⚠ The REAL store is <c>Plutus.Client.Storage.TillStore</c>, which is not referenced by
        /// this app yet: it carries EF Core 9's SQLite provider and AppClient is still on EF Core
        /// 3.1.17, so wiring it is part of WP2's cutover rather than something to slip in here. Until
        /// then the heartbeat reports an empty outbox — true today, because nothing in MAUI writes to
        /// the v2 outbox yet.
        /// </summary>
        private sealed class NullSyncStore : ISyncStore
        {
            public Task<string> GetCatalogueCursorAsync(System.Threading.CancellationToken ct = default) =>
                Task.FromResult<string>(null);

            public Task ApplyCatalogueAsync(
                System.Collections.Generic.IReadOnlyList<Contracts.Client.CatalogueItemDto> items,
                string cursor, System.Threading.CancellationToken ct = default) => Task.CompletedTask;

            public Task<int> OutboxDepthAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(0);

            public Task<TimeSpan?> OldestPendingAgeAsync(System.Threading.CancellationToken ct = default) =>
                Task.FromResult<TimeSpan?>(null);
        }
    }
}
