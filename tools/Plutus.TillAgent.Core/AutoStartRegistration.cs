using System;

namespace Plutus.TillAgent.Core
{
    /// <summary>What the agent should do about its auto-start registration on launch.</summary>
    public enum AutoStartAction
    {
        /// <summary>The registration is absent and should stay absent — the operator turned it off.</summary>
        LeaveOff = 0,

        /// <summary>Registered, and already pointing at this exe. Nothing to do.</summary>
        LeaveAsIs = 1,

        /// <summary>⚠ Registered but pointing SOMEWHERE ELSE — rewrite it. See the class header.</summary>
        Rewrite = 2,
    }

    /// <summary>
    /// Whether the agent's "start automatically" registration is actually pointing at the agent
    /// that is running.
    ///
    /// ⚠⚠ WHY THIS EXISTS — a real fault, found 2026-08-17. The tray checkbox asked only whether a
    /// `Run` value EXISTED, never whether it pointed anywhere real, and the value was written once
    /// when the box was ticked and never revisited. So a till that had the agent ticked while it ran
    /// out of `Downloads` kept a registration reading
    /// <c>"C:\Users\admin\Downloads\PlutusTillAgent (1).exe"</c> long after that file was gone:
    /// Windows silently launched nothing on every boot, and the checkbox went on showing **ticked**
    /// because the value was still there. The UI asserted the opposite of the truth, which is worse
    /// than an obviously broken setting — nobody investigates a tick.
    ///
    /// ⚠ THE FIX IS TO RE-REGISTER ON LAUNCH, not to validate harder. Presence of the value is the
    /// operator's INTENT ("start me automatically"); the path is an implementation detail that goes
    /// stale the moment the exe is moved or upgraded. Rewriting it every time the agent starts makes
    /// the stale case impossible rather than merely detectable — which matters, because the agent
    /// will soon be shipped inside the till package and therefore replaced on a schedule.
    ///
    /// ⚠ NO WINDOWS TYPES HERE, on purpose. This project is deliberately free of Windows
    /// dependencies (see the csproj) so it can be tested on CI and on the Mac. The registry read and
    /// write stay in the tray app; only the DECISION lives here, which is the half that was wrong.
    /// </summary>
    public static class AutoStartRegistration
    {
        /// <summary>
        /// The exact string to store.
        ///
        /// ⚠ QUOTED. Every till PC path that matters contains a space (`C:\Program Files\…`,
        /// `C:\Users\First Last\…`), and an unquoted `Run` value is split at the first one — Windows
        /// then tries to launch `C:\Users\First` and reports nothing at all.
        /// </summary>
        public static string ValueFor(string exePath) =>
            $"\"{(exePath ?? string.Empty).Trim().Trim('"')}\"";

        /// <summary>
        /// Does <paramref name="storedValue"/> refer to <paramref name="exePath"/>?
        ///
        /// ⚠ Quote- and case-insensitive: the value is stored quoted, and Windows paths are
        /// case-insensitive, so a comparison that cared about either would report a correct
        /// registration as broken and rewrite it on every single launch.
        /// </summary>
        public static bool PointsAt(string? storedValue, string exePath)
        {
            var stored = Normalise(storedValue);
            var wanted = Normalise(exePath);

            return stored.Length > 0
                && wanted.Length > 0
                && string.Equals(stored, wanted, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What to do on launch, given whatever is currently stored.
        /// </summary>
        /// <param name="storedValue">The `Run` value as read, or null when there is none.</param>
        /// <param name="exePath">The agent that is running — <c>Environment.ProcessPath</c>.</param>
        public static AutoStartAction Reconcile(string? storedValue, string exePath)
        {
            // ⚠ NO VALUE MEANS LEAVE IT ALONE. Absence is the operator having turned auto-start off
            // (or never turned it on), and an agent that registered itself on first run would be
            // installing itself without being asked.
            if (string.IsNullOrWhiteSpace(storedValue)) return AutoStartAction.LeaveOff;

            // ⚠ A value that cannot be resolved to a path at all still means "on" — rewrite it
            // rather than deleting it, or a single malformed value silently turns auto-start off.
            if (string.IsNullOrWhiteSpace(exePath)) return AutoStartAction.LeaveAsIs;

            return PointsAt(storedValue, exePath) ? AutoStartAction.LeaveAsIs : AutoStartAction.Rewrite;
        }

        /// <summary>
        /// Should the tray checkbox show ticked?
        ///
        /// ⚠ IT REQUIRES A REGISTRATION THAT POINTS HERE, not merely one that exists. With
        /// <see cref="Reconcile"/> running at launch this is nearly always the same answer — but if
        /// the rewrite could not be made (a locked hive, a policy-managed key), the operator must see
        /// an UNTICKED box they can act on rather than a ticked one that does nothing.
        /// </summary>
        public static bool ShowsAsEnabled(string? storedValue, string exePath) =>
            PointsAt(storedValue, exePath);

        private static string Normalise(string? path) =>
            (path ?? string.Empty).Trim().Trim('"').Trim();
    }
}
