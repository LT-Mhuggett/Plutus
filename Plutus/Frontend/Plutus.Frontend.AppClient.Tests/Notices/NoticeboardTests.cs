using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Notices;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Notices
{
    /// <summary>
    /// The till noticeboard (WP5b).
    ///
    /// ⚠ The FILTERING rules — which severities reach a till, which store a note belongs to — live in
    /// `Client.Core.NoticesClient` and are tested there (23 tests). What is tested here is the one
    /// rule this layer adds: **what a failed poll does to a board that already has something on it.**
    /// </summary>
    public class NoticeboardTests
    {
        private static PickNoteDto Note(Guid? id = null, int? storeId = 1) =>
            new(id ?? Guid.NewGuid(), "Pick 2× Batman #1 for web order", 5001, storeId,
                new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc), null);

        private static AnnouncementDto Ann(string severity = "Incident") =>
            new(Guid.NewGuid(), severity, "Card processing degraded",
                "Contactless is intermittent — take chip and PIN.",
                new DateTime(2026, 8, 16, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 16, 18, 0, 0, DateTimeKind.Utc));

        private static NoticesOutcome Delivered(
            IReadOnlyList<PickNoteDto> notes = null, IReadOnlyList<AnnouncementDto> anns = null) =>
            new(true, notes ?? Array.Empty<PickNoteDto>(), anns ?? Array.Empty<AnnouncementDto>());

        // ── the rule this layer exists for ────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE ONE THAT MATTERS. A poll that FAILED must not empty the board. `NoticesOutcome`'s
        /// own header warns that a caller treating an empty list as good news "will quietly clear a
        /// live incident banner the moment the network hiccups" — a shop reading an incident notice
        /// does not stop needing it because the broadband blinked.
        /// </summary>
        [Fact]
        public void A_failed_poll_keeps_what_is_already_on_the_board()
        {
            var showing = Noticeboard.Apply(NoticeboardState.Empty,
                Delivered(new[] { Note() }, new[] { Ann() }));

            var after = Noticeboard.Apply(showing, NoticesOutcome.Failed);

            Assert.Single(after.PickNotes);
            Assert.Single(after.Announcements);
        }

        /// <summary>⚠ And it SAYS so, rather than presenting old news as current. The board is marked
        /// stale so the screen can be honest about what the operator is looking at.</summary>
        [Fact]
        public void A_failed_poll_marks_the_board_stale()
        {
            var showing = Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() }));
            Assert.False(showing.Stale);

            Assert.True(Noticeboard.Apply(showing, NoticesOutcome.Failed).Stale);
        }

        /// <summary>
        /// ⚠⚠ AND THE OTHER HALF — a poll that SUCCEEDED and returned nothing DOES clear the board.
        /// Without this the whole thing is a banner that can never be taken down: an announcement
        /// whose window has closed would sit on the till until someone restarted it.
        /// </summary>
        [Fact]
        public void A_successful_empty_poll_clears_the_board()
        {
            var showing = Noticeboard.Apply(NoticeboardState.Empty,
                Delivered(new[] { Note() }, new[] { Ann() }));

            var after = Noticeboard.Apply(showing, Delivered());

            Assert.Empty(after.PickNotes);
            Assert.Empty(after.Announcements);
            Assert.False(after.HasAnything);
        }

        /// <summary>⚠ A good poll after a failed one clears the stale mark — the board is current
        /// again, and leaving the warning up would train people to ignore it.</summary>
        [Fact]
        public void A_good_poll_after_a_failure_is_no_longer_stale()
        {
            var stale = Noticeboard.Apply(
                Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() })),
                NoticesOutcome.Failed);

            Assert.True(stale.Stale);
            Assert.False(Noticeboard.Apply(stale, Delivered(new[] { Note() })).Stale);
        }

        /// <summary>⚠ Repeated failures do not degrade further — the board stays exactly as it was,
        /// however long the line is down.</summary>
        [Fact]
        public void Repeated_failures_keep_the_same_board()
        {
            var board = Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() }, new[] { Ann() }));

            for (var i = 0; i < 5; i++) board = Noticeboard.Apply(board, NoticesOutcome.Failed);

            Assert.Single(board.PickNotes);
            Assert.Single(board.Announcements);
            Assert.True(board.Stale);
        }

        // ── acknowledging ─────────────────────────────────────────────────────────────────────

        /// <summary>⚠ An acked note leaves the screen NOW. Sixty seconds of a note still sitting there
        /// after someone pressed the button reads as "it didn't work" — and the honest response to
        /// that is to press it again.</summary>
        [Fact]
        public void Acking_a_note_removes_that_note_immediately()
        {
            var keep = Guid.NewGuid();
            var go = Guid.NewGuid();

            var board = Noticeboard.Apply(NoticeboardState.Empty,
                Delivered(new[] { Note(keep), Note(go) }));

            var after = Noticeboard.WithoutNote(board, go);

            Assert.Equal(keep, Assert.Single(after.PickNotes).Id);
        }

        /// <summary>
        /// ⚠⚠ ACKING A NOTE MUST NOT TOUCH THE ANNOUNCEMENTS. They are not acknowledgeable at all —
        /// the contract says the server alone decides when one stops being active — so a till that
        /// could clear one would hide a maintenance window from the people it is addressed to.
        /// </summary>
        [Fact]
        public void Acking_a_note_leaves_the_announcements_alone()
        {
            var note = Guid.NewGuid();
            var board = Noticeboard.Apply(NoticeboardState.Empty,
                Delivered(new[] { Note(note) }, new[] { Ann(), Ann("Maintenance") }));

            var after = Noticeboard.WithoutNote(board, note);

            Assert.Empty(after.PickNotes);
            Assert.Equal(2, after.Announcements.Count);
        }

        /// <summary>⚠ Acking something that is not there is harmless — two tills can ack the same
        /// note, and `NoticesClient.AckAsync` already treats the platform's 404 as success.</summary>
        [Fact]
        public void Acking_an_unknown_note_changes_nothing()
        {
            var board = Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() }));

            Assert.Single(Noticeboard.WithoutNote(board, Guid.NewGuid()).PickNotes);
        }

        /// <summary>⚠ Acking does not clear a stale mark. The board is still as old as it was; one
        /// note leaving it does not make the rest current.</summary>
        [Fact]
        public void Acking_does_not_make_a_stale_board_current()
        {
            var stale = Noticeboard.Apply(
                Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() }, new[] { Ann() })),
                NoticesOutcome.Failed);

            Assert.True(Noticeboard.WithoutNote(stale, stale.PickNotes[0].Id).Stale);
        }

        // ── the empty board ───────────────────────────────────────────────────────────────────

        [Fact]
        public void An_empty_board_shows_nothing_and_is_not_stale()
        {
            Assert.False(NoticeboardState.Empty.HasAnything);
            Assert.False(NoticeboardState.Empty.Stale);
        }

        /// <summary>⚠ Either kind of notice on its own is still something worth showing — a board
        /// with only an announcement, or only a pick note, must not read as empty.</summary>
        [Fact]
        public void Either_kind_of_notice_alone_counts_as_something_to_show()
        {
            Assert.True(Noticeboard.Apply(NoticeboardState.Empty, Delivered(new[] { Note() })).HasAnything);
            Assert.True(Noticeboard.Apply(NoticeboardState.Empty, Delivered(anns: new[] { Ann() })).HasAnything);
        }
    }
}
