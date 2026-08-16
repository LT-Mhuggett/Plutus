using System;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Support;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Support
{
    /// <summary>
    /// The till's help desk (OP4 / WP6.3) — what a ticket line says, and how a thread reads.
    ///
    /// ⚠ The dialog sequence is not testable without a device and is not tested. These are the parts
    /// that decide what a human reads, which is where being wrong is quiet.
    /// </summary>
    public class SupportDeskTests
    {
        private static SupportTicketDto Ticket(
            string subject = "Card machine won't pair", byte status = 0, int daysAgo = 0) =>
            new(Guid.NewGuid(), subject, status, 1, "Ann",
                new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc).AddDays(-daysAgo));

        private static SupportMessageDto Msg(
            bool fromOperator, string body, int hour, string author = "Ann") =>
            new(fromOperator, author, body, new DateTime(2026, 8, 16, hour, 0, 0, DateTimeKind.Utc));

        // ── the ticket picker ─────────────────────────────────────────────────────────────────

        /// <summary>⚠ THE STATUS IS ON THE LINE. "Waiting on you" is the whole reason to open a
        /// ticket, and a list of bare subjects hides the one thing that decides which to read.</summary>
        [Fact]
        public void A_ticket_line_carries_its_status()
        {
            Assert.Equal("Card machine won't pair — Open", SupportDesk.TicketLine(Ticket()));
            Assert.Equal("Card machine won't pair — Waiting on you",
                SupportDesk.TicketLine(Ticket(status: 1)));
            Assert.Equal("Card machine won't pair — Closed",
                SupportDesk.TicketLine(Ticket(status: 2)));
        }

        /// <summary>⚠ A ticket with no subject still needs a line somebody can tap. An empty string
        /// renders as a blank row that looks like a broken list.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void A_ticket_with_no_subject_still_reads_as_something(string subject)
        {
            var line = SupportDesk.TicketLine(Ticket(subject: subject));

            Assert.StartsWith("(no subject)", line);
            Assert.Contains("Open", line);
        }

        /// <summary>⚠ MOST RECENTLY UPDATED FIRST — the server's order is not promised, and the
        /// ticket somebody opened this screen for is almost always the one that just changed.</summary>
        [Fact]
        public void Tickets_read_newest_activity_first()
        {
            var ordered = SupportDesk.InReadingOrder(new[]
            {
                Ticket("oldest", daysAgo: 10),
                Ticket("newest", daysAgo: 0),
                Ticket("middle", daysAgo: 3),
            });

            Assert.Equal(new[] { "newest", "middle", "oldest" }, ordered.Select(t => t.Subject));
        }

        [Fact]
        public void No_tickets_is_an_empty_list_not_a_crash() =>
            Assert.Empty(SupportDesk.InReadingOrder(null));

        // ── the thread ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE TWO SIDES MUST BE TELLABLE APART. "Plutus" versus the author's name is the only
        /// thing separating them, and a thread that renders both the same is unreadable.
        /// </summary>
        [Fact]
        public void A_thread_says_who_said_what()
        {
            var text = SupportDesk.ThreadText(new[]
            {
                Msg(false, "The card reader won't pair.", 9),
                Msg(true, "Try holding the power button for ten seconds.", 10, "Support"),
            });

            Assert.Contains("Plutus", text);
            Assert.Contains("Ann", text);
            Assert.Contains("The card reader won't pair.", text);
            Assert.Contains("Try holding the power button", text);
        }

        /// <summary>⚠ OLDEST FIRST — a conversation read backwards is a different conversation.</summary>
        [Fact]
        public void A_thread_reads_oldest_first()
        {
            var text = SupportDesk.ThreadText(new[]
            {
                Msg(true, "SECOND", 14),
                Msg(false, "FIRST", 9),
            });

            Assert.True(text.IndexOf("FIRST", StringComparison.Ordinal)
                      < text.IndexOf("SECOND", StringComparison.Ordinal));
        }

        /// <summary>
        /// ⚠⚠ IF IT IS TRIMMED IT SAYS SO. This lands in a dialog that does not scroll reliably on
        /// every host, and silently dropping the older half of a conversation would let somebody
        /// answer a question that had already been answered.
        /// </summary>
        [Fact]
        public void A_long_thread_says_how_much_is_missing()
        {
            var many = Enumerable.Range(0, 20).Select(i => Msg(i % 2 == 0, $"line {i}", 1 + i % 20)).ToArray();

            var text = SupportDesk.ThreadText(many, max: 5);

            Assert.Contains("15 earlier message(s) not shown", text);

            // ⚠ And what IS shown is the most recent, not the first five.
            Assert.Contains("line 19", text);
            Assert.DoesNotContain("line 0\n", text);
        }

        /// <summary>⚠ A thread that fits is not annotated — a "0 earlier messages" line would be
        /// noise on every ticket anybody ever reads.</summary>
        [Fact]
        public void A_short_thread_carries_no_trim_notice()
        {
            var text = SupportDesk.ThreadText(new[] { Msg(false, "hello", 9) }, max: 5);

            Assert.DoesNotContain("not shown", text);
        }

        [Fact]
        public void An_empty_thread_says_so()
        {
            Assert.Equal("No messages yet.", SupportDesk.ThreadText(Array.Empty<SupportMessageDto>()));
            Assert.Equal("No messages yet.", SupportDesk.ThreadText(null));
        }

        /// <summary>⚠ A client message with no author name still needs attributing — "You" rather
        /// than a bare timestamp with a floating body under it.</summary>
        [Fact]
        public void A_client_message_with_no_author_is_attributed_to_you()
        {
            var text = SupportDesk.ThreadText(new[] { Msg(false, "hello", 9, author: "") });

            Assert.Contains("You", text);
        }

        // ── raising ───────────────────────────────────────────────────────────────────────────

        /// <summary>⚠ BOTH HALVES REQUIRED. A subject with no body makes somebody at Plutus ask what
        /// the problem is, which costs the shop another day.</summary>
        [Theory]
        [InlineData("Card reader", "It won't pair", true)]
        [InlineData("Card reader", "", false)]
        [InlineData("", "It won't pair", false)]
        [InlineData("   ", "   ", false)]
        [InlineData(null, null, false)]
        public void A_ticket_needs_a_subject_and_a_description(string subject, string body, bool ok) =>
            Assert.Equal(ok, SupportDesk.CanRaise(subject, body));
    }
}
