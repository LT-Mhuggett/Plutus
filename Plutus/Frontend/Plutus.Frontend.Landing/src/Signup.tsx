import { useEffect, useState } from "react";
import { acceptDpa, apply, verifyEmail, type DoorState } from "./api.ts";

/**
 * The signup flow: apply → confirm the email → read and accept the agreement → done.
 *
 * ⚠⚠ IT HAS TO DEGRADE HONESTLY, AND THAT IS A DoD LINE RATHER THAN POLISH. Two of the three states
 * below are "you cannot sign up right now", and both are real today:
 *
 *   • `closed`        — the `signup.public` flag is off, so the API answers 404. The form is not
 *                       shown at all. Offering a form over a 404 is worse than offering nothing.
 *   • `no-agreement`  — signup is open but no DPA is published, so it answers 409. ⚠ This is the
 *                       LIVE state as at 2026-08-24 and it is deliberate: recording acceptance of
 *                       unpublished wording "manufactures evidence that a client agreed to something
 *                       nobody wrote" (WP-signup §4.3). The supplied document has its parties
 *                       reversed and 18 unfilled placeholders — see WP-landing §0b.
 *
 * ⚠ So this component was built and can be demonstrated against the 409. What it cannot do until the
 * agreement lands is let a real customer finish.
 */
export default function Signup({ door, verifyToken }: { door: DoorState | null; verifyToken: string | null }) {
  // ⚠ The verification link is handled even when the door is shut, because a token in somebody's
  // inbox outlives a flag being toggled — and the endpoint's own answer is the honest one.
  if (verifyToken) return <VerifyStep token={verifyToken} door={door} />;

  if (door === null) return <p className="muted">Checking…</p>;

  if (door.state === "closed") {
    return (
      <p className="notice">
        We're not taking new shops on just yet. If you'd like to be told when we are, drop us a line.
      </p>
    );
  }

  if (door.state === "no-agreement") {
    return (
      <p className="notice">
        We're nearly ready to take new shops on — our data processing agreement is with the
        solicitors. Check back shortly.
      </p>
    );
  }

  return <ApplyStep dpaVersion={door.dpa.version} />;
}

/** Step 1 — apply. ⚠ Creates a `TenantApplication` and nothing else; no tenant exists yet. */
function ApplyStep({ dpaVersion }: { dpaVersion: string }) {
  const [form, setForm] = useState({ businessName: "", contactName: "", contactEmail: "", phone: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [sent, setSent] = useState(false);

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [k]: e.target.value });

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      await apply({ ...form, region: "UK" });
      setSent(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setBusy(false);
    }
  };

  if (sent) {
    return (
      <div className="notice ok">
        <h3>Check your email</h3>
        <p>
          We've sent a link to <strong>{form.contactEmail}</strong>. Click it to confirm the address,
          then you'll be asked to read and accept our data processing agreement.
        </p>
        {/* ⚠ Says plainly that nothing is created yet. A signup that implies an account exists sets
            up the wrong expectation for the operator review that follows. */}
        <p className="muted small">
          Confirming your address doesn't create an account — we look at every application and email
          you either way.
        </p>
      </div>
    );
  }

  return (
    <form className="signup" onSubmit={submit}>
      <p className="muted small">
        Applying takes a minute. We'll confirm your email, ask you to accept our data processing
        agreement (version {dpaVersion}), and then set your shop up in a sandbox so you can try it
        before anything goes live.
      </p>

      <label>Business name
        <input value={form.businessName} onChange={set("businessName")} required minLength={2}
               disabled={busy} autoComplete="organization" />
      </label>
      <label>Your name
        <input value={form.contactName} onChange={set("contactName")} required minLength={2}
               disabled={busy} autoComplete="name" />
      </label>
      <label>Email
        <input type="email" value={form.contactEmail} onChange={set("contactEmail")} required
               disabled={busy} autoComplete="email" />
        {/* ⚠ The API refuses disposable domains with its own sentence; this sets the expectation
            before the round trip rather than duplicating the list client-side. */}
        <span className="hint">We send your password reset here, so use an address you'll keep.</span>
      </label>
      <label>Phone <span className="muted small">(optional)</span>
        <input value={form.phone} onChange={set("phone")} disabled={busy} autoComplete="tel" />
      </label>

      {error && <p className="error">{error}</p>}

      <button className="btn primary" type="submit"
              disabled={busy || !form.businessName.trim() || !form.contactName.trim() || !form.contactEmail.trim()}>
        {busy ? "Sending…" : "Apply"}
      </button>
    </form>
  );
}

/** Step 2 — the emailed link lands here. ⚠ Single-use and 48h; the endpoint owns both rules. */
function VerifyStep({ token, door }: { token: string; door: DoorState | null }) {
  const [state, setState] = useState<"working" | "ok" | "failed">("working");
  const [message, setMessage] = useState("");
  const [applicationId, setApplicationId] = useState("");

  useEffect(() => {
    let live = true;
    void verifyEmail(token)
      .then((r) => { if (live) { setApplicationId(r.applicationId); setState("ok"); } })
      .catch((e: unknown) => {
        if (live) { setMessage(e instanceof Error ? e.message : String(e)); setState("failed"); }
      });
    return () => { live = false; };
  }, [token]);

  if (state === "working") return <p className="muted">Confirming your email…</p>;
  if (state === "failed") return <p className="error">{message}</p>;

  // ⚠ Confirmed — but the agreement may still not be published, so the next step is conditional.
  if (door?.state === "open") {
    return <DpaStep applicationId={applicationId} version={door.dpa.version}
                    title={door.dpa.title} body={door.dpa.body} />;
  }

  return (
    <div className="notice ok">
      <h3>Email confirmed</h3>
      <p>Thanks. We'll be in touch once we've looked at your application.</p>
    </div>
  );
}

/**
 * Step 3 — read and accept.
 *
 * ⚠⚠ THE BODY IS RENDERED AS TEXT, NEVER AS HTML. The agreement is operator-supplied content
 * arriving over the wire; rendering it as markup would be a stored XSS with a legal document as the
 * payload. `<pre>` also preserves the clause numbering, which matters for something people may
 * later need to cite.
 *
 * ⚠ The version is echoed back to the server, which refuses if it has moved on — so nobody can
 * accept wording they were not shown.
 */
function DpaStep({ applicationId, version, title, body }:
                 { applicationId: string; version: string; title: string; body: string }) {
  const [read, setRead] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);

  const accept = async () => {
    setBusy(true); setError("");
    try { await acceptDpa(applicationId, version); setDone(true); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); setBusy(false); }
  };

  if (done) {
    return (
      <div className="notice ok">
        <h3>All done</h3>
        <p>
          Thanks — that's everything we need. We'll review your application and email you. Your shop
          starts in a sandbox, so you can try it out before anything goes live.
        </p>
      </div>
    );
  }

  return (
    <div className="dpa">
      <h3>{title}</h3>
      <p className="muted small">Version {version}</p>
      <pre className="dpa-body">{body}</pre>

      <label className="check">
        <input type="checkbox" checked={read} onChange={(e) => setRead(e.target.checked)} disabled={busy} />
        {" "}I have read the agreement and I accept it on behalf of this business.
      </label>

      {error && <p className="error">{error}</p>}

      <button className="btn primary" onClick={() => void accept()} disabled={busy || !read}>
        {busy ? "Recording…" : "Accept and finish"}
      </button>
      {/* ⚠ Said out loud, because it is recorded as evidence — WP-signup §4.2: "a dispute about
          whether a DPA was accepted is settled by who, when, and from where." */}
      <p className="muted small">
        We record your email address, the time, and the address you accept from as evidence of the
        agreement.
      </p>
    </div>
  );
}
