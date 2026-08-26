using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.TillAgent
{
    /// <summary>
    /// Keep the agent up to date without somebody walking to the till.
    ///
    /// ⚠⚠ WHY THIS EXISTS. On 2026-08-25 the till moved hostname and every agent refused it, because
    /// `AllowedOrigin` is an exact match. The code fix shipped the same day — and reached **nobody**,
    /// because the only way to update an agent was: exit the tray app, download 67 MB by hand,
    /// replace the file, run it. Two tills, two visits. Matt: *"Is there a way the agent could be made
    /// to auto update?"*
    ///
    /// ⚠⚠ THE RULE THAT OUTRANKS BEING UP TO DATE: **never restart mid-shift.** An agent that
    /// swaps itself out while a receipt is printing is worse than one a version behind — the customer
    /// is standing there and the money has already moved. So this updates only when the agent is
    /// demonstrably idle, and gives up quietly otherwise. There will be another chance in an hour.
    ///
    /// ⚠ It downloads and hands off; it never replaces its own file. Windows will not overwrite a
    /// running exe, so `PlutusTillAgentUpdater.exe` does the swap after this process exits.
    ///
    /// ⚠⚠ AND IT WILL NOT INSTALL AN UNVERIFIED BINARY. The agent exe is not code-signed yet
    /// (Platform Gaps §4), so `latest.json` carries a SHA-256 and this refuses anything that does not
    /// match. **No hash in the manifest means no update** — a channel that executes whatever the
    /// server serves is a channel that installs whatever anybody who reaches the server serves.
    /// </summary>
    public sealed class AgentUpdater
    {
        /// <summary>⚠ A long first delay on purpose: a till that has just been switched on is about to
        /// be used, and the shop opening is the worst moment to spend bandwidth and restart.</summary>
        private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan Every = TimeSpan.FromHours(1);
        /// <summary>How long the agent must have gone without printing to count as idle.</summary>
        private static readonly TimeSpan IdleFor = TimeSpan.FromMinutes(10);

        private readonly AgentState _state;
        private readonly Func<string> _currentVersion;
        private bool _handedOff;

        public AgentUpdater(AgentState state, Func<string>? currentVersion = null)
        {
            _state = state;
            _currentVersion = currentVersion ?? (() => Program.AgentVersion);
        }

        /// <summary>Fire and forget from start-up. ⚠ Never awaited — an update check must not be able
        /// to delay or prevent the agent serving the till.</summary>
        public void Start(CancellationToken ct = default) => _ = Task.Run(() => LoopAsync(ct), ct);

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(FirstCheck, ct);
                while (!ct.IsCancellationRequested && !_handedOff)
                {
                    try { await CheckOnceAsync(ct); }
                    catch (Exception ex) { Log("check failed: " + ex.Message); }
                    if (_handedOff) return;
                    await Task.Delay(Every, ct);
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }

        /// <summary>Exposed so the tray's "Check for updates now" can force one.</summary>
        public async Task<string> CheckOnceAsync(CancellationToken ct = default)
        {
            if (!_state.Config.AutoUpdate) return "Automatic updates are switched off.";

            var origin = FirstOrigin(_state.Config.AllowedOrigin);
            if (origin is null) return "No till address configured, so there is nowhere to look.";

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            var manifestJson = await http.GetStringAsync($"{origin}/agent/latest.json", ct);
            // ⚠ The till serves this with a BOM (PowerShell wrote it); JsonDocument rejects a BOM, so
            // trim it rather than "fixing" the publisher and breaking the existing download button.
            var manifest = JsonDocument.Parse(manifestJson.TrimStart('﻿'));
            var root = manifest.RootElement;

            var latest = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var sha = root.TryGetProperty("sha256", out var s) ? s.GetString() : null;

            if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(file))
                return "The update manifest is not readable.";

            if (!IsNewer(latest!, _currentVersion()))
                return $"Up to date (v{_currentVersion()}).";

            // ⚠⚠ THE HASH GATE. An older publisher that emits no sha256 is not an excuse to install
            // blind — it means this agent stays where it is and says so.
            if (string.IsNullOrWhiteSpace(sha) || sha!.Trim().Length != 64)
                return $"v{latest} is available but the manifest carries no checksum, so it will not be installed automatically. Download it from the till's Settings → Hardware.";

            if (!IsIdle()) return $"v{latest} is ready; waiting until this till is idle.";

            var target = CurrentExePath();
            if (target is null) return "Cannot work out where this agent is installed.";

            var temp = Path.Combine(Path.GetTempPath(), $"PlutusTillAgent-{latest}.exe");
            Log($"downloading v{latest} from {origin}");
            using (var src = await http.GetStreamAsync($"{origin}/agent/{file}", ct))
            using (var dst = File.Create(temp))
                await src.CopyToAsync(dst, ct);

            var actual = Sha256(temp);
            if (!string.Equals(actual, sha!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Log($"REFUSED v{latest}: expected {sha}, got {actual}");
                TryDelete(temp);
                return "The download did not match its checksum and was discarded.";
            }

            // ⚠ Re-checked after the download, not just before it: a 67 MB fetch takes long enough
            // for a customer to walk up, and the whole point is not restarting under one.
            if (!IsIdle()) { TryDelete(temp); return $"v{latest} downloaded; waiting until this till is idle."; }

            // ⚠⚠ FETCH THE UPDATER IF IT IS NOT HERE, rather than asking somebody to walk to the till.
            // The download button hands over ONE exe, so every agent installed before 2026-08-26 has
            // no updater beside it — and "update by hand this once, per PC" is exactly the visit this
            // whole feature exists to abolish. So the first automatic update also installs the thing
            // that performs it.
            //
            // ⚠ Same hash gate as the agent: a helper that swaps executables is a worse thing to
            // fetch unverified than the agent itself.
            var updater = Path.Combine(Path.GetDirectoryName(target)!, "PlutusTillAgentUpdater.exe");
            if (!File.Exists(updater))
            {
                var upFile = root.TryGetProperty("updater", out var uf) ? uf.GetString() : null;
                var upSha = root.TryGetProperty("updaterSha256", out var us) ? us.GetString() : null;
                if (string.IsNullOrWhiteSpace(upFile) || string.IsNullOrWhiteSpace(upSha) || upSha!.Trim().Length != 64)
                {
                    TryDelete(temp);
                    return $"v{latest} is available but this install has no updater and the manifest does not offer one. Download it from Settings → Hardware.";
                }

                var upTemp = updater + ".new";
                Log("fetching the updater helper");
                using (var src = await http.GetStreamAsync($"{origin}/agent/{upFile}", ct))
                using (var dst = File.Create(upTemp))
                    await src.CopyToAsync(dst, ct);

                if (!string.Equals(Sha256(upTemp), upSha!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Log("REFUSED the updater: checksum mismatch");
                    TryDelete(upTemp); TryDelete(temp);
                    return "The updater helper did not match its checksum and was discarded.";
                }

                // ⚠ May fail if the agent lives somewhere unwritable (Program Files under a
                // non-admin user). That is the same constraint the swap itself has, and it fails
                // safely here: no updater, no update, agent keeps running.
                try { File.Move(upTemp, updater); }
                catch (Exception ex)
                {
                    Log("could not place the updater: " + ex.Message);
                    TryDelete(upTemp); TryDelete(temp);
                    return "This agent is installed somewhere it cannot update itself. Move it to a writable folder, or update by hand.";
                }
            }

            Log($"handing v{latest} to the updater");
            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--pid", Environment.ProcessId.ToString(),
                    "--target", target,
                    "--source", temp,
                    "--sha256", sha!.Trim(),
                },
            });

            _handedOff = true;
            return $"Updating to v{latest} — the agent will restart in a moment.";
        }

        /// <summary>
        /// ⚠ Idle means BOTH: nothing printing right now, and nothing printed recently. The first
        /// stops a restart mid-receipt; the second stops one between two receipts of the same sale.
        /// </summary>
        private bool IsIdle()
        {
            if (_state.PrintsInFlight > 0) return false;
            var last = _state.LastPrintUtc;
            return last is null || DateTime.UtcNow - last.Value > IdleFor;
        }

        /// <summary>⚠ `AllowedOrigin` may be a comma-separated list since 2026-08-25. The first entry
        /// is the till this agent belongs to and is where the manifest is fetched from.</summary>
        internal static string? FirstOrigin(string? allowed)
        {
            if (string.IsNullOrWhiteSpace(allowed)) return null;
            foreach (var part in allowed.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim().TrimEnd('/');
                if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        /// <summary>
        /// ⚠ COMPARED PART BY PART AS NUMBERS, never as strings. "1.10.0" is newer than "1.9.0" and
        /// sorts before it — the classic version bug, and the agent version is exactly the shape that
        /// hits it.
        /// </summary>
        internal static bool IsNewer(string candidate, string current)
        {
            if (!Version.TryParse(Normalise(candidate), out var a)) return false;
            if (!Version.TryParse(Normalise(current), out var b)) return false;
            return a > b;
        }

        private static string Normalise(string v)
        {
            var core = (v ?? "").Trim();
            var plus = core.IndexOf('+');            // strip "+gitsha" if it ever appears
            if (plus > 0) core = core.Substring(0, plus);
            return core;
        }

        /// <summary>⚠ `MainModule.FileName`, not the assembly location: a single-file app's assembly
        /// reports a path inside its extraction directory, which is not the exe to replace.</summary>
        private static string? CurrentExePath()
        {
            try { return Process.GetCurrentProcess().MainModule?.FileName; }
            catch { return null; }
        }

        /// <summary>Clear the previous update's leftover, once this build has proved it starts.</summary>
        public static void CleanUpAfterUpdate()
        {
            try
            {
                var exe = CurrentExePath();
                if (exe is null) return;
                var old = exe + ".old";
                if (File.Exists(old)) { File.Delete(old); Log("removed the previous version"); }
            }
            catch { /* best effort — a leftover file is harmless */ }
        }

        private static string Sha256(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlutusTillAgent");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "update.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
