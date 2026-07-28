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
