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

        /// <summary>Characters per line: 42 for 80mm paper, 32 for 58mm.</summary>
        public int Columns { get; set; } = 42;

        /// <summary>Printer language: "auto" (recommended — recognises TSP100-family printers,
        /// which are raster-only, from the queue name), "escpos", or "star-raster".</summary>
        public string Emulation { get; set; } = "auto";

        /// <summary>The till origin allowed to call in (CORS). The agent is loopback-bound, so this
        /// stops OTHER pages in the same browser, not other machines.</summary>
        public string AllowedOrigin { get; set; } = "https://plutus.huggett.dscloud.me";

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
