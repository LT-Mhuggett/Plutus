import { useEffect, useState } from "react";
import DialogX from "./DialogX.tsx";
import {
  clientReply, fetchMyThread, fetchMyTickets, raiseTicket,
  SUPPORT_SEVERITY, SUPPORT_STATUS, type SupportMessage, type SupportTicket,
} from "./api.ts";
import { apiDateTime, apiDay } from "./apiTime.ts";
import { markTicketRead, requestTicketClose, acceptTicketClose, keepTicketOpen } from "./api.ts";

// WP6.3: the till's Help — opened from the appbar (top-right, next to the users button). Raise a
// ticket, see history with status, read/reply to the operator's thread. Endpoints gated on
// support.tickets (seeded to every role). Replaces the fire-and-forget "Ask for help" in Settings.

export default function HelpPanel({ onClose }: { onClose: () => void }) {
  const [tickets, setTickets] = useState<SupportTicket[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [thread, setThread] = useState<SupportMessage[]>([]);
  const [mode, setMode] = useState<"view" | "new">("view");
  const [error, setError] = useState("");

  const loadTickets = () => fetchMyTickets().then(setTickets).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void loadTickets(); }, []);

  // ⚠ D4 rule 2 — Escape cancels. It had the backdrop and a Close button and not this.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  /**
   * ⚠⚠ IT REFRESHES ITSELF NOW (WP-TICKETS, 2026-08-21). Matt: *"The help screen also needs to check
   * if there has been an update as it never updates without a navigation."* It loaded on mount and
   * then sat there — a shop watching this panel for an answer watched a static page.
   *
   * ⚠ `till-design.md` **D5**, the live-data contract, already required this and had not been
   * applied here. 20 seconds: a person waiting on support will accept it, and an estate of shops
   * polling every second would not be free.
   *
   * ⚠ AND OPENING A THREAD MARKS IT READ — which is what clears the ❓ badge, on this till and on
   * every other one in the shop. Re-marked on each poll too: a reply that lands while the thread is
   * on screen HAS been seen, and leaving it unread would badge a message the reader is looking at.
   */
  useEffect(() => {
    if (!open) { setThread([]); return; }

    const pull = () => {
      void fetchMyThread(open).then(setThread).catch((e) => setError(String(e instanceof Error ? e.message : e)));
      // ⚠ Never mind the outcome: failing to record a read must not stop somebody reading.
      void markTicketRead(open).catch(() => undefined);
      // ⚠ The LIST too — the status chip and the closure state live on the ticket row, not on the
      // messages, so polling only the thread would leave "Closed" invisible until a navigation.
      void loadTickets();
    };

    pull();
    const t = window.setInterval(pull, 20_000);
    return () => window.clearInterval(t);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const selected = tickets.find((t) => t.id === open) ?? null;

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide" style={{ maxWidth: 900, width: "92%" }}>
        {/* ⚠ D4 rule 1 — the ✕ joins the Close button and the backdrop click. Escape is wired below. */}
        <DialogX onClose={onClose} />
        <div className="r-row">
          <h2 className="grow">Help &amp; support</h2>
          <button className="ghost small" onClick={onClose}>Close</button>
        </div>
        {error && <p className="error small">{error}</p>}

        <div className="help-grid" style={{ display: "grid", gridTemplateColumns: "260px 1fr", gap: 16 }}>
          <div>
            <button className="primary" style={{ width: "100%" }} onClick={() => { setMode("new"); setOpen(null); }}>+ New ticket</button>
            <ul className="help-tickets" style={{ listStyle: "none", padding: 0, margin: "8px 0" }}>
              {tickets.map((t) => (
                <li key={t.id}>
                  <button
                    className={`setting-row ${open === t.id ? "active" : ""}`}
                    style={{ width: "100%", textAlign: "left", cursor: "pointer" }}
                    onClick={() => { setOpen(t.id); setMode("view"); }}
                  >
                    <span className="grow">
                      {t.subject}
                      <span className="muted small block">{SUPPORT_SEVERITY[t.severity] ?? "—"} · {apiDay(t.updatedAtUtc)}</span>
                    </span>
                    <span className={`chip ${t.status === 2 ? "" : "ok"}`}>{SUPPORT_STATUS[t.status] ?? t.status}</span>
                  </button>
                </li>
              ))}
              {tickets.length === 0 && <li className="muted small">No tickets yet.</li>}
            </ul>
          </div>

          <div>
            {mode === "new"
              ? <NewTicket onRaised={() => { setMode("view"); void loadTickets(); }} />
              : selected
                ? <Thread ticket={selected} messages={thread} onReplied={() => { if (open) void fetchMyThread(open).then(setThread); void loadTickets(); }} />
                : <p className="muted">Select a ticket, or raise a new one.</p>}
          </div>
        </div>
      </div>
    </div>
  );
}

function NewTicket({ onRaised }: { onRaised: () => void }) {
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [urgent, setUrgent] = useState(false);
  const [state, setState] = useState<"idle" | "sending">("idle");
  const [error, setError] = useState("");

  const send = () => {
    setState("sending"); setError("");
    void raiseTicket(subject.trim(), body.trim(), urgent ? 2 : 1)
      .then(() => { setSubject(""); setBody(""); setUrgent(false); setState("idle"); onRaised(); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("idle"); });
  };

  return (
    <div className="stack" style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <h3 className="settings-h">Raise a ticket</h3>
      <span className="muted small">Something not working? Send the Plutus team a message and track their reply here.</span>
      {error && <span className="error small">{error}</span>}
      <input className="pref-input" placeholder="Subject" value={subject} maxLength={200} onChange={(e) => setSubject(e.target.value)} />
      <textarea className="pref-input" style={{ minHeight: 100 }} placeholder="Describe the problem…" value={body} maxLength={4000} onChange={(e) => setBody(e.target.value)} />
      <label className="muted small"><input type="checkbox" checked={urgent} onChange={(e) => setUrgent(e.target.checked)} /> Urgent — this is stopping us trading</label>
      <button className="primary" disabled={state === "sending" || !subject.trim() || !body.trim()} onClick={send}>{state === "sending" ? "Sending…" : "Send to Plutus"}</button>
    </div>
  );
}

function Thread({ ticket, messages, onReplied }: { ticket: SupportTicket; messages: SupportMessage[]; onReplied: () => void }) {
  const [reply, setReply] = useState("");
  const [state, setState] = useState<"idle" | "sending">("idle");
  const [error, setError] = useState("");
  const closed = ticket.status === 2;
  const asked = ticket.closureRequestedByOperator;

  // ⚠ Every closure action refreshes the LIST as well as the thread — the closed state and the
  // standing request live on the ticket row, not on the messages.
  const act = (p: Promise<unknown>) => {
    setState("sending"); setError("");
    void p.then(() => { setState("idle"); onReplied(); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("idle"); });
  };

  const send = () => {
    setState("sending"); setError("");
    void clientReply(ticket.id, reply.trim())
      .then(() => { setReply(""); setState("idle"); onReplied(); })
      .catch((e) => { setError(String(e instanceof Error ? e.message : e)); setState("idle"); });
  };

  return (
    <div>
      <div className="r-row">
        <h3 className="grow">{ticket.subject}</h3>
        <span className={`chip ${closed ? "" : "ok"}`}>{SUPPORT_STATUS[ticket.status] ?? ticket.status}</span>
      </div>
      <div className="help-thread" style={{ maxHeight: 340, overflowY: "auto", margin: "8px 0" }}>
        {messages.map((m, i) => (
          <div key={i} className="setting-row" style={{ flexDirection: "column", alignItems: "stretch", gap: 2 }}>
            <span className="muted small">{m.fromOperator ? "Plutus" : m.authorName} · {apiDateTime(m.atUtc)}</span>
            <span>{m.body}</span>
          </div>
        ))}
        {messages.length === 0 && <p className="muted small">No messages.</p>}
      </div>
      {error && <p className="error small">{error}</p>}

      {/* ⚠⚠ A CLOSED TICKET SAYS SO **WITH THE DATE** (WP-TICKETS, 2026-08-21). Matt, of the
          operator's Close button: *"there is nothing visual within the ticket itself?"* The chip
          above said "Closed" and the thread said nothing — and a chip is not where somebody reading
          a conversation is looking. */}
      {closed
        ? (
          <p className="muted small">
            🔒 This ticket is closed{ticket.closedAtUtc && <> — {apiDay(ticket.closedAtUtc)}</>}.
            Raise a new one if you still need help.
          </p>
        )
        : (
          <div className="stack" style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {/* ⚠ THE STANDING REQUEST, IN THE THREAD. Neither side could see one before, so an
                operator asking to close was a question nobody was shown. */}
            {asked === true && (
              <p className="muted small">
                <strong>Plutus support has asked to close this.</strong> If it is sorted, say so —
                otherwise keep it open and tell them why.
              </p>
            )}
            {asked === false && (
              <p className="muted small">You have asked to close this — waiting for Plutus support.</p>
            )}

            <textarea className="pref-input" style={{ minHeight: 70 }} placeholder="Reply…" value={reply} maxLength={4000} onChange={(e) => setReply(e.target.value)} />
            <button className="primary" disabled={state === "sending" || !reply.trim()} onClick={send}>{state === "sending" ? "Sending…" : "Send"}</button>

            {/* ⚠⚠ THE SHOP ASKS; IT DOES NOT CLOSE. A shop closing its own open incident is how a
                fault gets lost — unless support asked first, in which case agreeing IS the close.
                The server enforces exactly that; these buttons only offer what it will accept. */}
            {asked === true
              ? (
                <div className="r-row" style={{ gap: 6 }}>
                  <button className="ghost small" disabled={state === "sending"} onClick={() => act(acceptTicketClose(ticket.id))}>Yes, close it</button>
                  <button className="ghost small" disabled={state === "sending"} onClick={() => act(keepTicketOpen(ticket.id))}>No, keep it open</button>
                </div>
              )
              : asked === false
                ? <button className="ghost small" disabled={state === "sending"} onClick={() => act(keepTicketOpen(ticket.id))}>Cancel my close request</button>
                : <button className="ghost small" disabled={state === "sending"} onClick={() => act(requestTicketClose(ticket.id))}>Ask to close this</button>}
          </div>
        )}
    </div>
  );
}
