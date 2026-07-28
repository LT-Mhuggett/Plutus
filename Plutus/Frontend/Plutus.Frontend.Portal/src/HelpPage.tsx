import { useEffect, useState } from "react";
import {
  fetchMyTickets, createTicket, fetchMyThread, clientReply,
  SUPPORT_STATUS, SUPPORT_SEVERITY, type TicketRow, type TicketMessage,
} from "./api.ts";

/** OP4: the client's "Ask for help" — raise a support ticket to the Plutus operator and follow the
 *  reply thread. Tenant-isolated server-side (you only ever see your own tickets). */
export default function HelpPage() {
  const [tickets, setTickets] = useState<TicketRow[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const refresh = () => fetchMyTickets().then((t) => { setTickets(t); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  if (openId) return <Thread id={openId} onBack={() => { setOpenId(null); void refresh(); }} />;

  return (
    <section className="panel">
      <h2>Help &amp; support</h2>
      <p className="muted small">Raise a ticket and the Plutus team will reply here. For anything urgent that stops you trading, mark it Urgent.</p>
      {error && <p className="error">{error}</p>}
      <NewTicket onCreated={refresh} onError={setError} />
      <h4>Your tickets</h4>
      <table>
        <thead><tr><th>Subject</th><th>Severity</th><th>Status</th><th>Updated</th><th /></tr></thead>
        <tbody>
          {tickets.map((t) => (
            <tr key={t.id}>
              <td>{t.subject}</td>
              <td>{SUPPORT_SEVERITY[t.severity] ?? t.severity}</td>
              <td>{SUPPORT_STATUS[t.status] ?? t.status}</td>
              <td className="small">{new Date(t.updatedAtUtc + "Z").toLocaleString("en-GB")}</td>
              <td><button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button></td>
            </tr>
          ))}
          {tickets.length === 0 && !error && <tr><td colSpan={5} className="muted">No tickets yet.</td></tr>}
        </tbody>
      </table>
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

function Thread({ id, onBack }: { id: string; onBack: () => void }) {
  const [msgs, setMsgs] = useState<TicketMessage[]>([]);
  const [reply, setReply] = useState("");
  const [error, setError] = useState("");
  const load = () => fetchMyThread(id).then(setMsgs).catch((e) => setError(String(e)));
  useEffect(() => { void load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  function send() {
    void clientReply(id, reply.trim()).then(() => { setReply(""); return load(); }).catch((e) => setError(String(e)));
  }
  return (
    <section className="panel">
      <div className="toolbar"><button className="ghost small" onClick={onBack}>← Back</button><h2 className="grow">Ticket</h2></div>
      {error && <p className="error">{error}</p>}
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {msgs.map((m, i) => (
          <div key={i} style={{ alignSelf: m.fromOperator ? "flex-start" : "flex-end", maxWidth: "75%", background: m.fromOperator ? "#e0e7ff" : "#dcfce7", padding: "8px 12px", borderRadius: 8 }}>
            <div className="muted small">{m.authorName} · {new Date(m.atUtc + "Z").toLocaleString("en-GB")}</div>
            <div>{m.body}</div>
          </div>
        ))}
        {msgs.length === 0 && <p className="muted">No messages.</p>}
      </div>
      <div className="toolbar" style={{ marginTop: 12 }}>
        <input className="grow" placeholder="Type a reply…" value={reply} maxLength={4000} onChange={(e) => setReply(e.target.value)} />
        <button className="primary small" disabled={!reply.trim()} onClick={send}>Send</button>
      </div>
    </section>
  );
}
