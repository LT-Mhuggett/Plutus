using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    /// <summary>
    /// A plain text crash log on the machine.
    ///
    /// ⚠ WHY THIS EXISTS. Everything else in this app logs through OpenTelemetry to an OTLP
    /// endpoint, plus a console exporter in Debug. Both are useless in the situation that matters:
    /// someone double-clicks the exe, clicks around, it disappears, and there is **nothing on the
    /// machine to send anyone**. A till in a shop has no console attached and may have no route to
    /// a telemetry endpoint at all — which is exactly when it is most likely to be misbehaving.
    ///
    /// So: a file, in a known place, that survives the process dying. It does not replace telemetry;
    /// it is the copy you can attach to an email.
    ///
    /// ⚠ Every method here swallows its own failures. A logger that throws while recording a crash
    /// turns a diagnosable fault into an undiagnosable one.
    /// </summary>
    internal static class CrashLog
    {
        private static readonly object Gate = new();

        /// <summary>
        /// Whether we are inside a real MAUI app host.
        ///
        /// ⚠ `FileSystem.AppDataDirectory` reaches through to WinRT and THROWS outside one — a unit
        /// test host, a design-time load — with `COMException: ClassFactory cannot supply requested
        /// class`. That is not a fault; it is how you can tell where you are.
        /// </summary>
        private static bool InAppHost
        {
            get
            {
                try { _ = FileSystem.AppDataDirectory; return true; }
                catch { return false; }
            }
        }

        /// <summary>Where the logs live. Surfaced in the Plutus tab so nobody has to guess.</summary>
        public static string Directory
        {
            get
            {
                try { return Path.Combine(FileSystem.AppDataDirectory, "logs"); }
                catch { return Path.GetTempPath(); }
            }
        }

        /// <summary>
        /// Today's log.
        ///
        /// ⚠ THE NAME SAYS WHERE IT CAME FROM, and that is not cosmetic — it cost a wrong diagnosis
        /// on 2026-08-10. Running the test suite writes here too: `CrashLog` falls back to the
        /// system temp directory when there is no app host, and it used the SAME filename, so
        /// `%TEMP%\plutus-till-2026-08-10.log` looked exactly like a till's own log. It held 60
        /// `ParkedBasket.FromJson` JSON errors — every one of them a test
        /// (`An_unreadable_blob_returns_an_empty_basket_rather_than_throwing` feeds it bad JSON on
        /// purpose and the guard logs when it catches). They were read as evidence that parked
        /// baskets were broken on a real till, reported as such, and queued as the next fix. The
        /// real till's log had none.
        ///
        /// A log a person cannot attribute at a glance is worse than no log, because it is believed.
        /// </summary>
        public static string TodaysFile => Path.Combine(
            Directory,
            InAppHost
                ? $"plutus-till-{DateTime.Now:yyyy-MM-dd}.log"
                : $"plutus-NOT-A-TILL-testhost-{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>
        /// Install global handlers. ⚠ Call this as EARLY as possible — the interesting crashes are
        /// the ones during start-up, and a handler registered after them records nothing.
        /// </summary>
        public static void Install()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    Write("UnhandledException", e.ExceptionObject as Exception);

                // ⚠ Fire-and-forget async is everywhere in this app (every `async void` command), so
                // a faulted task that nobody awaited is a REALISTIC way for this app to die. Without
                // this those exceptions vanish entirely.
                TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    Write("UnobservedTaskException", e.Exception);
                    e.SetObserved(); // recorded — do not also take the process down
                };

                Write("Startup", null, $"Plutus MAUI till v{Plutus.SharedKernel.PlutusVersion.Of(typeof(App).Assembly)}");
            }
            catch
            {
                // If the logger cannot install itself, the app must still start.
            }
        }

        /// <summary>Record something. <paramref name="context"/> says where we were, which is
        /// usually more useful than the stack when the fault is a binding or a missing resource.</summary>
        public static void Write(string context, Exception? ex, string? note = null)
        {
            try
            {
                var sb = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append("  [").Append(context).Append("]  ");

                if (note != null) sb.Append(note);
                if (ex != null)
                {
                    sb.AppendLine(ex.GetType().FullName + ": " + ex.Message);
                    sb.AppendLine(ex.StackTrace);
                    for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    {
                        sb.AppendLine("  --- inner: " + inner.GetType().FullName + ": " + inner.Message);
                        sb.AppendLine(inner.StackTrace);
                    }
                }

                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    File.AppendAllText(TodaysFile, sb.AppendLine().ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // ⚠ Never throw from the crash logger. See the class note.
            }
        }
    }
}
