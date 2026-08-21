import { useEffect, useState } from "react";
import DialogX from "./DialogX.tsx";
import {
  clientReply, fetchMyThread, fetchMyTickets, raiseTicket,
  SUPPORT_SEVERITY, SUPPORT_STATUS, type SupportMessage, type SupportTicket,
} from "./api.ts";
import { apiDateTime, apiDay } from "./apiTime.ts";

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
  useEffect(() => {
    if (!open) { setThread([]); return; }
    void fetchMyThread(open).then(setThread).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, [open]);

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
      {closed
        ? <p className="muted small">This ticket is closed. Raise a new one if you still need help.</p>
        : (
          <div className="stack" style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            <textarea className="pref-input" style={{ minHeight: 70 }} placeholder="Reply…" value={reply} maxLength={4000} onChange={(e) => setReply(e.target.value)} />
            <button className="primary" disabled={state === "sending" || !reply.trim()} onClick={send}>{state === "sending" ? "Sending…" : "Send reply"}</button>
          </div>
        )}
    </div>
  );
}
