import { useEffect, useState } from "react";
import {
  fetchMyTickets, createTicket, fetchMyThread, clientReply,
  SUPPORT_STATUS, SUPPORT_SEVERITY, type TicketRow, type TicketMessage,
} from "./api.ts";
import DataTable from "./DataTable.tsx";
import { apiDateTime } from "./apiTime.ts";
import { markTicketRead, requestTicketClose, acceptTicketClose, keepTicketOpen } from "./api.ts";

/** OP4: the client's "Ask for help" — raise a support ticket to the Plutus operator and follow the
 *  reply thread. Tenant-isolated server-side (you only ever see your own tickets). */
export default function HelpPage() {
  const [tickets, setTickets] = useState<TicketRow[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const refresh = () => fetchMyTickets().then((t) => { setTickets(t); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  if (openId) return <Thread id={openId} ticket={tickets.find((t) => t.id === openId)} onBack={() => { setOpenId(null); void refresh(); }} />;

  return (
    <section className="panel">
      <h2>Help &amp; support</h2>
      <p className="muted small">Raise a ticket and the Plutus team will reply here. For anything urgent that stops you trading, mark it Urgent.</p>
      {error && <p className="error">{error}</p>}
      <NewTicket onCreated={refresh} onError={setError} />
      <h4>Your tickets</h4>
      <DataTable<TicketRow>
        columns={[
          { key: "subject", label: "Subject" },
          { key: "severity", label: "Severity", render: (t) => SUPPORT_SEVERITY[t.severity] ?? String(t.severity) },
          { key: "status", label: "Status", render: (t) => SUPPORT_STATUS[t.status] ?? String(t.status) },
          { key: "updatedAtUtc", label: "Updated", render: (t) => <span className="small">{apiDateTime(t.updatedAtUtc)}</span> },
        ]}
        rows={tickets} getKey={(t) => t.id} initialSortKey="updatedAtUtc" initialSortDir="desc"
        search={(t) => `${t.subject} ${SUPPORT_SEVERITY[t.severity] ?? ""} ${SUPPORT_STATUS[t.status] ?? ""}`}
        searchPlaceholder="Search subject / status…"
        rowActions={(t) => <button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button>}
        emptyText="No tickets yet."
      />
    </section>
  );
}

function NewTicket({ onCreated, onError }: { onCreated: () => void; onError: (e: string) => void }) {
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [severity, setSeverity] = useState(1);
  const [busy, setBusy] = useState(false);
  function submit() {
    setBusy(true);
    void createTicket({ subject: subject.trim(), body: body.trim(), severity })
      .then(() => { setSubject(""); setBody(""); setSeverity(1); onCreated(); })
      .catch((e) => onError(String(e))).finally(() => setBusy(false));
  }
  return (
    <div className="panel" style={{ background: "#f8fafc" }}>
      <h4>New ticket</h4>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Subject <input value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} /></label>
        <label>Severity
          <select value={severity} onChange={(e) => setSeverity(Number(e.target.value))}>
            {SUPPORT_SEVERITY.map((s, i) => <option key={i} value={i}>{s}</option>)}
          </select>
        </label>
      </div>
      <textarea style={{ width: "100%", minHeight: 80 }} placeholder="Describe the problem…" value={body} maxLength={4000} onChange={(e) => setBody(e.target.value)} />
      <div className="toolbar"><button className="primary small" disabled={busy || !subject.trim() || !body.trim()} onClick={submit}>Raise ticket</button></div>
    </div>
  );
}

/**
 * One ticket's thread, client side.
 *
 * ⚠⚠ THREE OF THE FOUR WP-TICKETS GAPS LAND HERE (2026-08-21):
 *
 * 1. **It never refreshed.** Matt: *"The help screen also needs to check if there has been an update
 *    as it never updates without a navigation."* It loaded once on mount and then sat there — a shop
 *    watching this screen for an answer watched a static page.
 * 2. **A closed ticket did not look closed.** Now it says so, with the date, and the reply box goes.
 * 3. **Neither side could ask to close.** The shop can now ask, and can accept when support asks.
 *
 * ⚠ OPENING THE THREAD MARKS IT READ — which is what clears the ❓ badge, everywhere, including on
 * the other till in the shop. The badge is a consequence of the state, never the owner of it.
 */
function Thread({ id, ticket, onBack }: { id: string; ticket?: TicketRow; onBack: () => void }) {
  const [msgs, setMsgs] = useState<TicketMessage[]>([]);
  const [reply, setReply] = useState("");
  const [error, setError] = useState("");
  const load = () => fetchMyThread(id).then(setMsgs).catch((e) => setError(String(e)));

  useEffect(() => {
    void load();
    // ⚠ MARK READ ON OPEN, and never mind the outcome: failing to record a read must not stop
    // somebody reading. The worst case is a badge that clears a minute later.
    void markTicketRead(id).catch(() => undefined);
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  /**
   * ⚠⚠ IT POLLS WHILE IT IS OPEN. `till-design.md` **D5** — the live-data contract — already
   * required this and had not been applied here: a screen showing a number the outside world can
   * change must go and look, not wait to be re-navigated.
   *
   * ⚠ 20 SECONDS, not one. This is a conversation, not a till reading; a person waiting for support
   * will accept twenty seconds and an estate of shops polling every second would not be free.
   *
   * ⚠ AND IT RE-MARKS READ on each poll, because a reply arriving while the thread is open on screen
   * HAS been seen — leaving it unread would light a badge for a message the reader is looking at.
   */
  useEffect(() => {
    const t = window.setInterval(() => {
      void load();
      void markTicketRead(id).catch(() => undefined);
    }, 20_000);
    return () => window.clearInterval(t);
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  function send() {
    void clientReply(id, reply.trim()).then(() => { setReply(""); return load(); }).catch((e) => setError(String(e)));
  }

  const act = (p: Promise<unknown>) => void p.then(onBack).catch((e) => setError(String(e)));

  const closed = ticket?.status === 2;
  const asked = ticket?.closureRequestedByOperator;

  return (
    <section className="panel">
      <div className="toolbar"><button className="ghost small" onClick={onBack}>← Back</button><h2 className="grow">{ticket?.subject ?? "Ticket"}</h2></div>
      {error && <p className="error">{error}</p>}

      {closed && (
        <p className="muted small">
          🔒 <strong>This ticket is closed</strong>
          {ticket?.closedAtUtc && <> — closed on {apiDateTime(ticket.closedAtUtc)}</>}. Raise a new
          one if you need anything else.
        </p>
      )}
      {!closed && asked === true && (
        <p className="muted small">
          <strong>Plutus support has asked to close this.</strong> If it is sorted, agree below —
          otherwise say so and it stays open.
        </p>
      )}
      {!closed && asked === false && (
        <p className="muted small">You have asked to close this — waiting for Plutus support.</p>
      )}

      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {msgs.map((m, i) => (
          <div key={i} style={{ alignSelf: m.fromOperator ? "flex-start" : "flex-end", maxWidth: "75%", background: m.fromOperator ? "var(--panel-alt, #eef1f5)" : "var(--accent)", color: m.fromOperator ? "inherit" : "var(--accent-ink)", borderRadius: 8, padding: "6px 10px" }}>
            <div className="muted small">{m.authorName} · {apiDateTime(m.atUtc)}</div>
            <div>{m.body}</div>
          </div>
        ))}
        {msgs.length === 0 && <p className="muted">No messages.</p>}
      </div>

      {/* ⚠ NO REPLY BOX ON A CLOSED TICKET — the server refuses one, and an input that looks usable
          and is not is worse than one that is absent. */}
      {!closed && (
        <div className="toolbar" style={{ marginTop: 12 }}>
          <input className="grow" placeholder="Type a reply…" value={reply} maxLength={4000} onChange={(e) => setReply(e.target.value)} />
          <button className="primary small" disabled={!reply.trim()} onClick={send}>Send</button>

          {/* ⚠⚠ THE SHOP ASKS; IT DOES NOT CLOSE. A shop closing its own open incident is how a fault
              gets lost — unless support asked first, in which case agreeing IS the close. */}
          {asked === true
            ? <>
                <button className="primary small" onClick={() => act(acceptTicketClose(id))}>Yes, close it</button>
                <button className="ghost small" onClick={() => act(keepTicketOpen(id))}>No, keep it open</button>
              </>
            : asked === false
              ? <button className="ghost small" onClick={() => act(keepTicketOpen(id))}>Cancel my close request</button>
              : <button className="ghost small" onClick={() => act(requestTicketClose(id))}>Ask to close</button>}
        </div>
      )}
    </section>
  );
}
