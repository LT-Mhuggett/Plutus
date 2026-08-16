using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Notices
{
    /// <summary>
    /// What the till's noticeboard is currently showing.
    ///
    /// ⚠ <see cref="Stale"/> means the LAST POLL FAILED and this is the last board we were told
    /// about — not that it is empty. See <see cref="Noticeboard.Apply"/> for why those must never be
    /// the same thing.
    /// </summary>
    public sealed record NoticeboardState(
        IReadOnlyList<PickNoteDto> PickNotes,
        IReadOnlyList<AnnouncementDto> Announcements,
        bool Stale)
    {
        public static NoticeboardState Empty { get; } =
            new(Array.Empty<PickNoteDto>(), Array.Empty<AnnouncementDto>(), Stale: false);

        /// <summary>Is there anything at all for a human to look at?</summary>
        public bool HasAnything => PickNotes.Count > 0 || Announcements.Count > 0;
    }

    /// <summary>
    /// WP5b — the MAUI till's noticeboard: pick-from-floor notes and platform announcements.
    ///
    /// ⚠⚠ THE DECIDING IS IN <see cref="Apply"/>, WHICH IS PURE, and the caching and the screen are
    /// built around it. `Plutus.Client.Core.NoticesClient` already held the two *filtering* rules
    /// (which severities reach a till, which store a note belongs to) with 23 tests, and was
    /// referenced exactly once in this whole app — in a comment. This is the caller it was waiting
    /// for; what is added here is only the rule about what a FAILED poll does.
    ///
    /// ⚠ NOTHING HERE MAY EVER BLOCK SELLING, per <see cref="Services.Sync.TillCadence"/>. It rides
    /// the 60s beat, it swallows everything, and a noticeboard that cannot load is a noticeboard
    /// that does not change.
    /// </summary>
    public static class Noticeboard
    {
        private static NoticeboardState _state = NoticeboardState.Empty;
        private static readonly object Lock = new();

        /// <summary>The board as it stands. Safe to read from any thread.</summary>
        public static NoticeboardState State
        {
            get { lock (Lock) return _state; }
        }

        /// <summary>
        /// Raised when the board CHANGES. ⚠ Handlers must not throw and must not assume a UI thread —
        /// this fires from the cadence's background loop, exactly like `TillCadence.Ticked`.
        /// ⚠ Subscribers must unsubscribe when their screen goes away, or a static event holds the
        /// page alive for the life of the process.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// What the board becomes after a poll.
        ///
        /// ⚠⚠ A FAILED POLL KEEPS WHAT IS ON SCREEN. `NoticesOutcome.Delivered` false means "we could
        /// not ask", which is NOT "there is nothing to show" — and its own header warns that a caller
        /// confusing the two "will quietly clear a live incident banner the moment the network
        /// hiccups". A shop reading an incident notice does not stop needing it because the broadband
        /// blinked. So the board is only ever REPLACED by an answer the platform actually gave.
        ///
        /// ⚠ It is marked <see cref="NoticeboardState.Stale"/> instead, so the screen can say the
        /// board may be out of date rather than silently presenting old news as current.
        ///
        /// ⚠ Pure and static so the rule can be tested without a device, a timer or a network.
        /// </summary>
        public static NoticeboardState Apply(NoticeboardState current, NoticesOutcome poll)
        {
            if (current is null) throw new ArgumentNullException(nameof(current));
            if (poll is null) throw new ArgumentNullException(nameof(poll));

            return poll.Delivered
                ? new NoticeboardState(poll.PickNotes, poll.Announcements, Stale: false)
                : current with { Stale = true };
        }

        /// <summary>
        /// Drop a pick note that has just been acknowledged.
        ///
        /// ⚠ So the row leaves the screen NOW rather than at the next beat. Sixty seconds of a note
        /// still sitting there after someone pressed the button reads as "it didn't work", and the
        /// honest response to that is to press it again.
        ///
        /// ⚠ ANNOUNCEMENTS ARE NOT TOUCHED — they are not acknowledgeable at all. The contract is
        /// explicit that the server decides when one stops being active, and a till that could hide
        /// one would hide a maintenance window from the only people it is addressed to.
        /// </summary>
        public static NoticeboardState WithoutNote(NoticeboardState current, Guid noteId)
        {
            if (current is null) throw new ArgumentNullException(nameof(current));

            return current with { PickNotes = current.PickNotes.Where(n => n.Id != noteId).ToList() };
        }

        /// <summary>
        /// Poll the platform and update the board. Called from the 60s cadence. Never throws.
        /// </summary>
        public static async Task RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);

                // ⚠ NOT a failure, and NOT an empty board either. A till that has never been enrolled
                // has no noticeboard to be wrong about, so leave whatever is there alone.
                if (api is null) return;

                // ⚠ A till that does not know its store yet passes null, and `NoticesClient.IsForStore`
                // deliberately shows it everything: a note it need not act on costs a walk, one it
                // should have seen costs an oversell.
                var storeId = await Storage.TillPlacement.StoreIdAsync(ct: ct).ConfigureAwait(false);

                var outcome = await new NoticesClient(api).PollAsync(storeId, ct: ct).ConfigureAwait(false);

                Set(Apply(State, outcome));
            }
            catch (Exception ex)
            {
                // ⚠ A noticeboard that cannot load is never a till that will not sell.
                Analytics.CrashLog.Write("Noticeboard.RefreshAsync", ex);
            }
        }

        /// <summary>
        /// Acknowledge a pick note — someone has taken the stock off the shelf.
        ///
        /// ⚠ THIS NEEDS THE NETWORK, deliberately. `NoticesClient.AckAsync` explains why: an ack is a
        /// claim that a human picked something, and it has to reach the shop waiting to ship the
        /// order. A failed ack leaves the note in place and it returns on the next beat.
        /// </summary>
        public static async Task<bool> AckAsync(Guid noteId, CancellationToken ct = default)
        {
            try
            {
                var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
                if (api is null) return false;

                if (!await new NoticesClient(api).AckAsync(noteId, ct).ConfigureAwait(false)) return false;

                Set(WithoutNote(State, noteId));
                return true;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("Noticeboard.AckAsync", ex);
                return false;
            }
        }

        /// <summary>⚠ Test seam and sign-out reset. Clearing the board on sign-out matters: the next
        /// operator may be at a different store.</summary>
        public static void Reset() => Set(NoticeboardState.Empty);

        private static void Set(NoticeboardState next)
        {
            lock (Lock) _state = next;

            // ⚠ Outside the lock, and in its own guard: a screen's redraw is never worth taking the
            // cadence down, and a handler that throws must not leave the board locked.
            try { Changed?.Invoke(); }
            catch (Exception ex) { Analytics.CrashLog.Write("Noticeboard.Changed", ex); }
        }
    }
}
