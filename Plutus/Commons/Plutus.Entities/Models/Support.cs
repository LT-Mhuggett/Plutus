using System;

namespace Plutus.Entities.Models
{
    public enum SupportStatus : byte { Open = 0, WaitingOnClient = 1, Closed = 2 }
    public enum SupportSeverity : byte { Question = 0, Problem = 1, Urgent = 2 }

    /// <summary>
    /// OP4 support ticket — a client's "Ask for help". TENANT-OWNED (real TenantId), so a client
    /// only ever sees their own; the operator reads across tenants (Guid.Empty context). One thread
    /// of <see cref="SupportMessage"/> per ticket.
    /// </summary>
    public class SupportTicket
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Subject { get; set; }
        public byte Status { get; set; }          // SupportStatus
        public byte Severity { get; set; }        // SupportSeverity
        public Guid RaisedByUserId { get; set; }
        public string RaisedByName { get; set; }
        public string AssignedTo { get; set; }    // operator display name (nullable)
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        // ── WP-TICKETS, 2026-08-21 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// When somebody on the CLIENT side last opened this thread.
        ///
        /// ⚠⚠ MATT: *"When I reply to a live ticket, how is the user informed?"* They were not. An
        /// operator answered, the ticket flipped to `WaitingOnClient`, and **nothing anywhere told
        /// the shop** — the reply sat there until somebody happened to open Help and scroll.
        ///
        /// ⚠ THIS IS THE WHOLE UNREAD SIGNAL: a ticket is unread when its last message is from the
        /// operator and this is null or older than that message. Null means never opened, which for
        /// a ticket with an operator reply is exactly "unread".
        ///
        /// ⚠ PER TICKET, NOT PER USER, and deliberately. The client side of a ticket is a SHOP, not
        /// a person: whoever is on shift reads the reply and acts on it, and a badge that stayed lit
        /// for the manager because the supervisor read it would be noise nobody could clear.
        /// </summary>
        public DateTime? ClientLastReadAtUtc { get; set; }

        /// <summary>
        /// Who has asked for this to be closed — <c>true</c> the operator, <c>false</c> the client,
        /// <c>null</c> nobody.
        ///
        /// ⚠⚠ MATT: *"There is no 'Request ticket to be closed' from either person."* Correct — the
        /// only way a ticket ended was the operator's `Close` button, so a shop whose problem was
        /// solved had no way to say so, and an operator who thought it was done had no way to check.
        ///
        /// ⚠⚠ **A REQUEST, NOT A COMMAND, FROM THE CLIENT'S SIDE.** A shop closing its own open
        /// incident is how a fault gets lost — the operator confirms. The operator may still close
        /// unilaterally, because support sometimes has to.
        /// </summary>
        public bool? ClosureRequestedByOperator { get; set; }

        /// <summary>When the closure was asked for. ⚠ Cleared with the request, so a stale timestamp
        /// cannot outlive it and make a withdrawn request look live.</summary>
        public DateTime? ClosureRequestedAtUtc { get; set; }

        /// <summary>
        /// When it was actually closed.
        ///
        /// ⚠ MATT: *"the button 'Close' I assume closes the ticket, but there is nothing visual
        /// within the ticket itself?"* It did close it, and the thread said nothing at all. A closed
        /// ticket now carries the date it closed so the thread can show it as an event rather than
        /// as a status word in a list the reader is not looking at.
        /// </summary>
        public DateTime? ClosedAtUtc { get; set; }
    }

    /// <summary>OP4 one message in a ticket thread. TENANT-OWNED. <see cref="FromOperator"/>
    /// distinguishes the client's and the operator's turns.</summary>
    public class SupportMessage
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TicketId { get; set; }
        public bool FromOperator { get; set; }
        public string AuthorName { get; set; }
        public string Body { get; set; }
        public DateTime AtUtc { get; set; }
    }
}
