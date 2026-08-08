using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Connectivity;
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
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private SecureDeviceCredentialStore? _credentials;

        private string _serverUrl;
        private string _enrolmentCode;
        private string _connectionSummary = "Not checked yet.";
        private string _connectionDetail;
        private Color _connectionColour = Colors.Gray;
        private string _deviceSummary = "Not enrolled.";
        private string _lastAction;
        private bool _busy;

        public ConnectionViewModel()
        {
            Title = "Plutus";
            Icon = "md-cloud";
            _serverUrl = ServerUrlSetting;
            TillVersionText = $"MAUI till v{PlutusVersion.Of(typeof(App).Assembly)}";
            _ = InitialiseAsync();
        }

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

        private Command _forgetCommand;
        public Command ForgetCommand => _forgetCommand ??= new Command(async () => await ForgetAsync());

        #endregion

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

        private void DescribeDevice() =>
            DeviceSummary = _credentials?.DeviceId is Guid id
                ? $"Device {id}"
                : "Not enrolled — enter an enrolment code from the portal.";

        /// <summary>Build the API client for the address currently in the box, with the token
        /// provider attached so authenticated calls work.</summary>
        private PlutusApiClient? Api(out string error)
        {
            error = null;
            if (!Uri.TryCreate(ServerUrl?.Trim(), UriKind.Absolute, out var baseUri))
            {
                error = "That server address isn't a valid URL.";
                return null;
            }

            // ⚠ One HttpClient for the app; only the base address moves. A client per call leaks
            // sockets, and on a till running all day that is a real exhaustion bug.
            Http.BaseAddress = baseUri;
            var bootstrap = new PlutusApiClient(Http);
            var tokens = _credentials is null ? null : new DeviceTokenProvider(bootstrap, _credentials);
            return new PlutusApiClient(Http, tokens);
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
                Logger.LogError(ex);
                ConnectionSummary = "Couldn't check the connection.";
                ConnectionDetail = ex.Message;
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

                var result = await api.EnrolAsync(EnrolmentCode.Trim());
                _credentials ??= await SecureDeviceCredentialStore.LoadAsync();
                _credentials.Save(result.DeviceId, result.ClientSecret);

                EnrolmentCode = string.Empty; // one-time code — leaving it on screen invites a retry
                                              // that can only ever fail with 410 Gone.
                LastAction = $"Enrolled. Till {result.TillId}.";
                DescribeDevice();
                await CheckAsync();
            }
            catch (EnrolmentFailedException ex)
            {
                // 410 Gone = reused/expired/unknown. Surfaced as the message it is, not a crash.
                LastAction = ex.Message;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                LastAction = $"Enrolment failed: {ex.Message}";
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
                Logger.LogError(ex);
                LastAction = $"Heartbeat failed: {ex.Message}";
            }
            finally { Busy = false; }
        }

        private async Task CatalogueAsync()
        {
            if (Busy) return;
            Busy = true;
            try
            {
                var api = Api(out var error);
                if (api is null) { LastAction = error; return; }

                var page = await api.GetCatalogueChangesAsync(limit: 5);
                if (page is null)
                {
                    LastAction = "The catalogue feed didn't answer. Is this till enrolled, and is the backend up to date?";
                    return;
                }

                var first = page.Items.Length > 0 ? $" First: {page.Items[0].Name} ({page.Items[0].IdOne})." : "";
                LastAction = $"Catalogue reachable — {page.Items.Length} item(s) in this page, hasMore={page.HasMore}.{first}";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                LastAction = $"Catalogue read failed: {ex.Message}";
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
