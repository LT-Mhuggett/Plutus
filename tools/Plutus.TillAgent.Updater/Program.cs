using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Plutus.TillAgent.Updater
{
    /// <summary>
    /// Swap the Plutus Till Agent's executable and start the new one.
    ///
    /// ⚠⚠ WHY A SEPARATE PROCESS AT ALL. Windows will not let a running `.exe` be overwritten. Some
    /// process has to outlive the agent long enough to replace the file and relaunch it, and that
    /// process cannot be the agent. (Matt chose this over the in-process rename trick on 2026-08-26:
    /// one more binary to sign, in exchange for never having to reason about a process rewriting
    /// itself.)
    ///
    /// Usage — the agent builds this command line, nobody types it:
    ///   PlutusTillAgentUpdater.exe --pid 1234 --target "C:\…\PlutusTillAgent.exe"
    ///                             --source "C:\…\Temp\agent-1.5.1.exe" --sha256 abc…
    ///
    /// ⚠ IT VERIFIES THE HASH BEFORE IT TOUCHES ANYTHING. The agent verifies it too, on download —
    /// this is the second check, and it is the one that matters, because between the download and
    /// this swap the file has been sitting in a world-writable temp directory. The agent's exe is
    /// **not code-signed yet** (Platform Gaps §9), so this hash is the only thing standing between an
    /// update channel and running an arbitrary binary as the logged-in shop user.
    /// ⚠ No hash supplied = refuse. A missing hash must never mean "skip the check".
    ///
    /// ⚠ ITS FAILURE MODE IS "LEAVE EVERYTHING ALONE". Every abort path exits without having moved
    /// the live exe, so the shop keeps the agent it had. The one irreducible window is between the
    /// move-aside and the move-in, which is why the old file is *renamed* rather than deleted and is
    /// put back if the new one cannot be placed.
    /// </summary>
    public static class Program
    {
        private const int WaitForExitSeconds = 60;
        private const int SwapAttempts = 20;
        private static readonly TimeSpan SwapGap = TimeSpan.FromMilliseconds(500);

        public static int Main(string[] args)
        {
            try
            {
                var a = Parse(args);
                if (a is null) { Log("bad arguments; doing nothing"); return 2; }

                if (!File.Exists(a.Source)) { Log($"source missing: {a.Source}"); return 3; }
                if (!File.Exists(a.Target)) { Log($"target missing: {a.Target}"); return 4; }

                // ⚠ Hash first. Before the agent is stopped, before anything is moved.
                var actual = Sha256(a.Source);
                if (!string.Equals(actual, a.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"REFUSING: hash mismatch. expected {a.Sha256}, got {actual}");
                    TryDelete(a.Source);
                    return 5;
                }

                WaitForExit(a.Pid);

                var aside = a.Target + ".old";
                TryDelete(aside);   // a leftover from a previous update

                if (!Swap(a.Source, a.Target, aside)) { Log("swap failed; the original agent is untouched"); return 6; }

                Log($"replaced {Path.GetFileName(a.Target)}; starting it");
                Start(a.Target);

                // ⚠ Left on disk on purpose: if the new agent will not start, somebody can rename
                // `.old` back by hand. The agent deletes it on its next clean start.
                return 0;
            }
            catch (Exception ex)
            {
                Log("unhandled: " + ex);
                return 1;   // ⚠ never throw out of an updater — a stack trace on a till is noise
            }
        }

        private sealed class Args
        {
            public int Pid;
            public string Target = "";
            public string Source = "";
            public string Sha256 = "";
        }

        private static Args? Parse(string[] args)
        {
            var a = new Args();
            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                var v = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "--pid": if (!int.TryParse(v, out a.Pid)) return null; break;
                    case "--target": a.Target = v; break;
                    case "--source": a.Source = v; break;
                    case "--sha256": a.Sha256 = v.Trim(); break;
                }
            }
            // ⚠ Every field required, hash included. See the class comment.
            if (a.Pid <= 0 || a.Target.Length == 0 || a.Source.Length == 0 || a.Sha256.Length != 64) return null;
            return a;
        }

        /// <summary>
        /// ⚠ Waits for the agent to go, but does NOT kill it. A tray app mid-print is holding a
        /// printer handle; taking it down is how you get half a receipt and a jammed queue. If it is
        /// still alive after a minute we carry on anyway — the swap simply fails on the file lock and
        /// leaves everything as it was, which is the right outcome.
        /// </summary>
        private static void WaitForExit(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.WaitForExit(WaitForExitSeconds * 1000))
                    Log($"agent pid {pid} still running after {WaitForExitSeconds}s; attempting the swap anyway");
            }
            catch (ArgumentException)
            {
                // Already gone — the normal case.
            }
        }

        /// <summary>
        /// Move the old exe aside, move the new one in. ⚠ RETRIES, because the loser here is usually
        /// an antivirus scanner holding the file for a second after the process exits, and on a shop
        /// PC that is the common case rather than the exotic one.
        /// </summary>
        private static bool Swap(string source, string target, string aside)
        {
            for (var attempt = 1; attempt <= SwapAttempts; attempt++)
            {
                try
                {
                    File.Move(target, aside);
                    try
                    {
                        File.Move(source, target);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // ⚠ THE ONE DANGEROUS WINDOW, CLOSED. The live exe is aside and the new one
                        // would not go in — put the original back rather than leaving a till with no
                        // agent at all.
                        Log("could not place the new exe: " + ex.Message + " — restoring the original");
                        try { File.Move(aside, target); } catch (Exception restore) { Log("RESTORE FAILED: " + restore.Message); }
                        return false;
                    }
                }
                catch (IOException) { Thread.Sleep(SwapGap); }
                catch (UnauthorizedAccessException) { Thread.Sleep(SwapGap); }
            }
            return false;
        }

        private static void Start(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
                });
            }
            catch (Exception ex)
            {
                // ⚠ Worth saying loudly in the log: the swap worked, so the next manual start or
                // logon gets the new version. Nothing is broken, but nothing is running either.
                Log("the new agent did not start: " + ex.Message + " — start it from the Start menu");
            }
        }

        private static string Sha256(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        /// <summary>
        /// ⚠ Beside the agent's own config, not in the install directory — Program Files is not
        /// writable by a shop user, and a log nobody can write is a log nobody can read afterwards.
        /// </summary>
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
            catch { /* a failed log must not fail an update */ }
        }
    }
}
