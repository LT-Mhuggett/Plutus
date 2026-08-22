using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Support
{
    /// <summary>
    /// OP4 / WP6.3 — "ask Plutus for help" from the till: raise a ticket, read the thread, reply.
    ///
    /// ⚠ A DIALOG FLOW, NOT A TAB. Every comparable MAUI flow here works this way (the printer, the
    /// categories, a stock adjustment), and adding a tab would break the parity this till is measured
    /// on — the web till reaches Help from a button, so its tab names are the ones §B7 checks.
    ///
    /// ⚠ THE DISPLAY RULES ARE PURE AND SEPARATE, below. What a ticket line says and how a thread
    /// reads are the parts worth pinning; the dialog sequence is not testable without a device and
    /// does not need to be.
    /// </summary>
    public static class SupportDesk
    {
        /// <summary>
        /// ⚠ MOST RECENTLY UPDATED FIRST. The server's order is not promised, and the ticket somebody
        /// opened the screen for is almost always the one that just changed.
        /// </summary>
        public static IReadOnlyList<SupportTicketDto> InReadingOrder(IEnumerable<SupportTicketDto> tickets) =>
            (tickets ?? Enumerable.Empty<SupportTicketDto>())
                .OrderByDescending(t => t.UpdatedAtUtc)
                .ToList();

        /// <summary>
        /// One line in the ticket picker.
        ///
        /// ⚠ THE STATUS IS ON THE LINE, not behind a tap. "Waiting on you" is the whole reason to
        /// open a ticket, and a list of bare subjects hides the one thing that decides which to read.
        /// ⚠ Words come from `SupportLabels`, never from a positional lookup — see its header.
        /// </summary>
        public static string TicketLine(SupportTicketDto t)
        {
            if (t is null) throw new ArgumentNullException(nameof(t));

            var subject = string.IsNullOrWhiteSpace(t.Subject) ? "(no subject)" : t.Subject.Trim();

            return $"{subject} — {SupportLabels.Status(t.Status)}";
        }

        /// <summary>
        /// The thread, as one readable block.
        ///
        /// ⚠ "Plutus" vs the author's name is the only thing separating the two sides of the
        /// conversation, and a thread that renders both the same is unreadable.
        ///
        /// ⚠⚠ OLDEST FIRST AND CAPPED, with the cap STATED. This lands in a dialog, which does not
        /// scroll reliably on every host; silently dropping the older half of a conversation would
        /// let somebody answer a question that was already answered. If it is trimmed, the text says
        /// so and says how many are missing.
        /// </summary>
        public static string ThreadText(IEnumerable<SupportMessageDto> messages, int max = 12)
        {
            var all = (messages ?? Enumerable.Empty<SupportMessageDto>())
                .OrderBy(m => m.AtUtc)
                .ToList();

            if (all.Count == 0) return "No messages yet.";

            var text = new StringBuilder();

            if (all.Count > max)
                text.AppendLine($"({all.Count - max} earlier message(s) not shown.)").AppendLine();

            foreach (var m in all.Skip(Math.Max(0, all.Count - max)))
            {
                // ⚠ Local time. The wire is UTC and an operator reading "14:05" about a message sent
                // at 15:05 their time will not believe the rest of the screen either.
                var who = m.FromOperator ? "Plutus" : (string.IsNullOrWhiteSpace(m.AuthorName) ? "You" : m.AuthorName);

                text.AppendLine($"{who} · {m.AtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
                text.AppendLine(m.Body ?? "");
                text.AppendLine();
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Is this worth sending? ⚠ Both halves required — a subject with no body makes somebody at
        /// Plutus ask what the problem is, which costs the shop another day.
        /// </summary>
        public static bool CanRaise(string subject, string body) =>
            !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(body);

        // ── the flow ──────────────────────────────────────────────────────────────────────────

        /// <summary>This tenant's tickets, or null when the platform could not be asked.</summary>
        public static async Task<IReadOnlyList<SupportTicketDto>> LoadAsync(CancellationToken ct = default)
        {
            // ⚠⚠ THE OPERATOR'S CLIENT, NOT THE DEVICE'S — fixed 2026-08-23. Every support endpoint
            // is `[Authorize(perm:support.tickets)]`, and a DEVICE token carries no operator
            // permissions at all: the platform answered 403, this returned null, and the till told the
            // operator *"Plutus can't be reached"*. Matt saw that on a till whose app bar was showing
            // a till name it had just fetched from the same server.
            //
            // ⚠ The same trap as the Bin, one day apart: the endpoint needs a person, the caller
            // handed it a machine, and the failure was reported as a network fault.
            var api = await Connectivity.PlutusApi.GetOperatorAsync(ct).ConfigureAwait(false);
            if (api is null) return null;

            var tickets = await api.GetSupportTicketsAsync(ct).ConfigureAwait(false);
            return tickets is null ? null : InReadingOrder(tickets);
        }

        /// <summary>One ticket's thread, or null when the platform could not be asked.</summary>
        public static async Task<IReadOnlyList<SupportMessageDto>> ThreadAsync(
            Guid ticketId, CancellationToken ct = default)
        {
            // ⚠⚠ THE OPERATOR'S CLIENT, NOT THE DEVICE'S — fixed 2026-08-23. Every support endpoint
            // is `[Authorize(perm:support.tickets)]`, and a DEVICE token carries no operator
            // permissions at all: the platform answered 403, this returned null, and the till told the
            // operator *"Plutus can't be reached"*. Matt saw that on a till whose app bar was showing
            // a till name it had just fetched from the same server.
            //
            // ⚠ The same trap as the Bin, one day apart: the endpoint needs a person, the caller
            // handed it a machine, and the failure was reported as a network fault.
            var api = await Connectivity.PlutusApi.GetOperatorAsync(ct).ConfigureAwait(false);
            if (api is null) return null;

            return await api.GetSupportThreadAsync(ticketId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Raise a ticket.
        ///
        /// ⚠⚠ NEVER QUEUED OFFLINE. A ticket is somebody asking for help now; parking it in the
        /// outbox would tell them it had been sent, and a shop that believes it has reached support
        /// is worse off than one that knows it has not.
        /// </summary>
        public static async Task<bool> RaiseAsync(
            string subject, string body, bool urgent, CancellationToken ct = default)
        {
            if (!CanRaise(subject, body)) return false;

            // ⚠⚠ THE OPERATOR'S CLIENT, NOT THE DEVICE'S — fixed 2026-08-23. Every support endpoint
            // is `[Authorize(perm:support.tickets)]`, and a DEVICE token carries no operator
            // permissions at all: the platform answered 403, this returned null, and the till told the
            // operator *"Plutus can't be reached"*. Matt saw that on a till whose app bar was showing
            // a till name it had just fetched from the same server.
            //
            // ⚠ The same trap as the Bin, one day apart: the endpoint needs a person, the caller
            // handed it a machine, and the failure was reported as a network fault.
            var api = await Connectivity.PlutusApi.GetOperatorAsync(ct).ConfigureAwait(false);
            if (api is null) return false;

            return await api.RaiseSupportTicketAsync(
                subject.Trim(), body.Trim(),
                urgent ? SupportLabels.UrgentSeverity : SupportLabels.DefaultSeverity,
                ct).ConfigureAwait(false);
        }

        /// <summary>Reply on a thread. ⚠ Same reasoning as raising one — never queued.</summary>
        public static async Task<bool> ReplyAsync(Guid ticketId, string body, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;

            // ⚠⚠ THE OPERATOR'S CLIENT, NOT THE DEVICE'S — fixed 2026-08-23. Every support endpoint
            // is `[Authorize(perm:support.tickets)]`, and a DEVICE token carries no operator
            // permissions at all: the platform answered 403, this returned null, and the till told the
            // operator *"Plutus can't be reached"*. Matt saw that on a till whose app bar was showing
            // a till name it had just fetched from the same server.
            //
            // ⚠ The same trap as the Bin, one day apart: the endpoint needs a person, the caller
            // handed it a machine, and the failure was reported as a network fault.
            var api = await Connectivity.PlutusApi.GetOperatorAsync(ct).ConfigureAwait(false);
            if (api is null) return false;

            return await api.ReplyToSupportTicketAsync(ticketId, body.Trim(), ct).ConfigureAwait(false);
        }
    }
}
