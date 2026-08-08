using System;
using Microsoft.Maui.Storage;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// Where this till keeps its device identity.
    ///
    /// ⚠ THE SPLIT IS THE POINT (retrofit WP4). The device id is ordinary configuration and lives in
    /// <see cref="Preferences"/>; the CLIENT SECRET goes to platform <see cref="SecureStorage"/> —
    /// DPAPI on Windows, the Keychain on iOS, the Keystore on Android — and never into the SQLite
    /// file. Till databases get copied off machines during support: a secret in the .db3 is a
    /// secret that has been emailed.
    ///
    /// ⚠ SecureStorage is async-only, so this reads through a small cache primed at construction.
    /// <see cref="IDeviceCredentialStore"/> is deliberately synchronous because the outbox pusher
    /// touches it on every drain and an await there would spread through the whole engine.
    /// </summary>
    internal sealed class SecureDeviceCredentialStore : IDeviceCredentialStore
    {
        private const string DeviceIdKey = "PlutusDeviceId";
        private const string SecretKey = "PlutusClientSecret";

        private Guid? _deviceId;
        private string? _secret;

        public Guid? DeviceId => _deviceId;
        public string? ClientSecret => _secret;

        private SecureDeviceCredentialStore() { }

        /// <summary>
        /// Has this machine been enrolled? Synchronous and cheap, because start-up routing needs the
        /// answer before anything is awaited.
        ///
        /// ⚠ Checks only the device ID in <see cref="Preferences"/>, not the secret in
        /// <see cref="SecureStorage"/> — deliberately. The secret needs an async read, and reading
        /// it here would either block the UI thread at launch or force the whole start-up path to
        /// become async. A device id with an unreadable secret is a broken enrolment, and the right
        /// place to discover that is the Plutus tab, which says so and offers to re-enrol — not a
        /// silent bounce back to first-run setup.
        /// </summary>
        public static bool IsEnrolled() =>
            Guid.TryParse(Preferences.Get(DeviceIdKey, null), out var id) && id != Guid.Empty;

        /// <summary>Load what is already on this machine. A first run finds nothing, which is a
        /// state — "not enrolled" — and not a failure.</summary>
        public static async System.Threading.Tasks.Task<SecureDeviceCredentialStore> LoadAsync()
        {
            var store = new SecureDeviceCredentialStore();
            var id = Preferences.Get(DeviceIdKey, null);
            if (Guid.TryParse(id, out var deviceId)) store._deviceId = deviceId;

            try
            {
                store._secret = await SecureStorage.Default.GetAsync(SecretKey);
            }
            catch
            {
                // SecureStorage throws on some Windows configurations rather than returning null.
                // An unreadable secret is "not enrolled" — recoverable by enrolling again, which is
                // better than a crash on the first screen anyone opens.
                store._secret = null;
            }
            return store;
        }

        public void Save(Guid deviceId, string clientSecret)
        {
            _deviceId = deviceId;
            _secret = clientSecret;
            Preferences.Set(DeviceIdKey, deviceId.ToString());
            // Fire-and-forget is wrong for a credential — if this loses the race with an app close,
            // the till silently un-enrols itself. Block until it is written.
            SecureStorage.Default.SetAsync(SecretKey, clientSecret).GetAwaiter().GetResult();
        }

        public void Clear()
        {
            _deviceId = null;
            _secret = null;
            Preferences.Remove(DeviceIdKey);
            SecureStorage.Default.Remove(SecretKey);
        }
    }
}
