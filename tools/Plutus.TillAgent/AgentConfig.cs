using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Plutus.TillAgent
{
    /// <summary>
    /// Per-machine agent settings: the pairing token, the chosen printer and the paper width.
    /// Stored under %LOCALAPPDATA% (per user, no admin rights needed — the agent runs in the
    /// cashier's auto-logged-in session, and a per-machine location under ProgramData would need
    /// elevation to write on first run).
    /// </summary>
    public sealed class AgentConfig
    {
        /// <summary>Shared secret the till must send as X-Agent-Token. Generated on first run.
        /// Without it, any web page the cashier visits could kick the cash drawer.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Windows printer name to send RAW ESC/POS to. Empty = not configured yet.</summary>
        public string PrinterName { get; set; } = string.Empty;

        /// <summary>FE3.2: a Windows.Devices.PointOfService printer id. When set it WINS over
        /// PrinterName — the POS route bypasses the spooler queue entirely (the futurePRNT queue
        /// was seen text-rendering even RAW jobs) and is the NatApp-proven path for the TSP143.</summary>
        public string PosDeviceId { get; set; } = string.Empty;

        /// <summary>Friendly name for the POS device, for display only.</summary>
        public string PosDeviceName { get; set; } = string.Empty;

        /// <summary>Characters per line: 42 for 80mm paper, 32 for 58mm.</summary>
        public int Columns { get; set; } = 42;

        /// <summary>Printer language: "auto" (recommended — recognises TSP100-family printers,
        /// which are raster-only, from the queue name), "escpos", or "star-raster".</summary>
        public string Emulation { get; set; } = "auto";

        /// <summary>
        /// The till origin(s) allowed to call in (CORS). The agent is loopback-bound, so this stops
        /// OTHER pages in the same browser, not other machines.
        ///
        /// ⚠⚠ A LIST SINCE 2026-08-25, comma- or whitespace-separated, and that change has a story.
        /// The web till moved from `plutus.huggett.dscloud.me` to `till.plutus.huggett.dscloud.me`,
        /// and because this was matched with a single `string.Equals`, **every agent on the estate
        /// would have refused the till the moment it moved** — no receipt printing and no cash
        /// drawer, on a shop counter, with nothing on screen naming the cause. It was found by
        /// grepping for the old hostname rather than by anything failing.
        ///
        /// ⚠ The property NAME stays `AllowedOrigin` (singular) on purpose: it is a key in every
        /// installed `agent.json`, and renaming it would silently reset every till to the default.
        ///
        /// ⚠ Empty still means "allow anything", unchanged — that is the escape hatch for a shop on
        /// a hostname nobody predicted.
        /// </summary>
        public string AllowedOrigin { get; set; } = "https://till.plutus.huggett.dscloud.me";

        /// <summary>
        /// Update this agent from the till without somebody walking to the PC.
        ///
        /// ⚠⚠ DEFAULT ON, and the 2026-08-25 hostname move is the argument. The fix for it shipped
        /// the same day and reached nobody: updating an agent meant exiting the tray app, downloading
        /// 67 MB, replacing the file and running it — per till. Two tills, two visits, and printing
        /// silently dead in between. An estate that cannot be updated is an estate that stays broken.
        ///
        /// ⚠ It is still safe to leave on, because <see cref="AgentUpdater"/> only ever acts when the
        /// agent is idle and only ever installs a binary whose SHA-256 matches the manifest. A shop
        /// that would rather control the timing can turn it off in Settings.
        /// </summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>
        /// Is this browser origin allowed to drive the hardware?
        ///
        /// ⚠ Exact match per entry, case-insensitive, trailing slashes ignored — an Origin header
        /// never carries a path, but people type one into the tray box.
        /// ⚠ NOT a prefix or suffix match. "endsWith(plutus.huggett.dscloud.me)" would admit
        /// `evil-plutus.huggett.dscloud.me`, and the whole point of this check is which PAGE in the
        /// browser may open the cash drawer.
        /// </summary>
        public bool IsOriginAllowed(string? origin)
        {
            if (string.IsNullOrWhiteSpace(AllowedOrigin)) return true;   // unchanged escape hatch
            if (string.IsNullOrWhiteSpace(origin)) return false;

            var candidate = origin.TrimEnd('/');
            foreach (var allowed in AllowedOrigin.Split(
                         new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(candidate, allowed.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlutusTillAgent");
        private static string FilePath => Path.Combine(Dir, "agent.json");

        public static AgentConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(FilePath));
                    if (cfg != null)
                    {
                        if (string.IsNullOrWhiteSpace(cfg.Token)) { cfg.Token = NewToken(); cfg.Save(); }
                        return cfg;
                    }
                }
            }
            catch (Exception) { /* unreadable/corrupt → fall through to a fresh config */ }

            var fresh = new AgentConfig { Token = NewToken() };
            fresh.Save();
            return fresh;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>A 20-character pairing token from the same human-keyable alphabet the rest of
        /// Plutus uses (no I/L/O/U) — it gets typed into the till's Settings by hand.</summary>
        public static string NewToken()
        {
            const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            var bytes = RandomNumberGenerator.GetBytes(20);
            var chars = new char[20];
            for (var i = 0; i < 20; i++) chars[i] = alphabet[bytes[i] & 0x1f];
            return new string(chars);
        }
    }
}
