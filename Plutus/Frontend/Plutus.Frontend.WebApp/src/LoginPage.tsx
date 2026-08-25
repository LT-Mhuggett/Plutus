import { useCallback, useEffect, useState } from "react";
import { login, serverAnswered, BUSINESS_NAME } from "./api.ts";
import { setSession, type Session } from "./session.ts";
import { PlutusMark } from "./PlutusMark.tsx";
import { sessionExpiresAt, signInOffline } from "./offlineLogin.ts";
import { checkConnection, clockIsSuspect, toneFor, type ConnectionStatus } from "./connectionCheck.ts";
import { portalUrl } from "./sibling.ts";

interface Props {
  onLogin: (session: Session) => void;
}

export default function LoginPage({ onLogin }: Props) {
  // ⚠ Read once at render, not in state: it is a build-time constant, not something that changes.
  const portal = portalUrl();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  /** W-P4: a stale-but-usable till says so after signing in offline. */

  // ── WP16a: can this till see Plutus? ──────────────────────────────────────
  //
  // ⚠⚠ MAUI'S LOGIN SCREEN HAS HAD THIS SINCE WP16a AND THIS ONE HAD NOTHING, so a shop whose
  // backend was down met a failed sign-in it could not tell from a wrong password. Matt, 2026-08-19:
  // *"I need the functionality and look and feel to be the same across both tills."* Same three
  // states, same sentences, same dot, same tap-to-refresh — `LoginView.xaml` lines 72–97.
  //
  // ⚠ IT NEVER GATES SIGN-IN. A broken connection check must not be the reason nobody can sign in,
  // and offline sign-in (W-P4) is a supported path — this only tells the operator which fault they
  // are looking at.
  const [conn, setConn] = useState<ConnectionStatus | null>(null);
  const [checking, setChecking] = useState(false);

  const refreshConnection = useCallback(async () => {
    setChecking(true);
    try {
      setConn(await checkConnection());
    } catch {
      // ⚠ `checkConnection` is documented never to throw; if that ever changes, a login screen must
      // still render. Show nothing rather than a stuck spinner.
      setConn(null);
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => {
    void refreshConnection();
  }, [refreshConnection]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      onLogin(await login(email.trim(), password));
    } catch (err) {
      // ⚠⚠ W-P4 — OFFLINE SIGN-IN, AND ONLY ON A TRANSPORT FAILURE. `serverAnswered` is true for a
      // 401 and for any other HTTP answer; falling back after one would let a disabled operator sign
      // in offline PAST THEIR OWN REFUSAL, because `AuthController` is where a deactivated employee
      // is turned away. A dead network rejects `fetch` with a TypeError and sets no such flag.
      //
      // ⚠ Never `navigator.onLine` for this decision — it reports the network INTERFACE, and says
      // "online" in a shop whose broadband is down.
      if (!serverAnswered(err)) {
        const offline = await signInOffline(email.trim(), password);

        if (offline.ok && offline.operator) {
          // ⚠ The session is minted LOCALLY and carries no platform token — so `perm:*` endpoints
          // stay unreachable while offline, exactly as on MAUI. Queued sales still flow: they go on
          // the DEVICE token, which is a different credential.
          setSession({
            token: "",
            employeeId: offline.operator.userId,
            name: offline.operator.displayName,
            // ⚠ The earliest of the 12h cap and the business-day rollover — a session spanning two
            // business days leaks yesterday's operator into today's X/Z breakdown.
            expiresAt: sessionExpiresAt(new Date()).toISOString(),
          });

          // ⚠ Shown BEFORE handing over, so a stale till's warning is not lost behind the shell.
          if (offline.message) window.alert(offline.message);

          onLogin({
            token: "",
            employeeId: offline.operator.userId,
            name: offline.operator.displayName,
            expiresAt: sessionExpiresAt(new Date()).toISOString(),
          });
          return;
        }

        setError(offline.message);
        setBusy(false);
        return;
      }

      setError(err instanceof Error ? err.message : String(err));
      setBusy(false);
    }
  }

  return (
    <div className="login-screen">
      <form className="login-card" onSubmit={submit}>
        <h1><PlutusMark size={34} />Plutus</h1>
        <p className="tagline">{BUSINESS_NAME} — Point of Sale</p>

        <label>
          Email
          <input
            type="email"
            autoFocus
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={busy}
            required
          />
        </label>
        <label>
          Password
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={busy}
            required
          />
        </label>

        {error && <p className="error small">{error}</p>}

        <button className="primary" type="submit" disabled={busy || !email.trim() || !password}>
          {busy ? "Signing in…" : "Sign in"}
        </button>

        {/* WP16a — the connection badge, in MAUI's own words and order.
            ⚠ A BUTTON, not a div: it is tappable (MAUI has a TapGestureRecognizer on the same row)
            and a keyboard user must be able to reach it. `type="button"` because it sits inside the
            login form and must never submit it.
            ⚠ Below the sign-in button, so it can never push the fields around while it resolves. */}
        <button
          type="button"
          className={`login-conn ${conn ? toneFor(conn.state) : "checking"}`}
          onClick={() => void refreshConnection()}
          disabled={checking}
          title="Check the connection again"
        >
          <span className="dot" aria-hidden="true" />
          <span className="summary">
            {checking || !conn ? "Checking connection…" : conn.summary}
          </span>
          {/* ⚠ The diagnostic line is for whoever the operator RINGS, not for the sales floor —
              which is why it is smaller and separate rather than folded into the summary. */}
          {!checking && conn && (clockIsSuspect(conn) || conn.detail) && (
            <span className="detail">
              {clockIsSuspect(conn)
                // ⚠ Worth saying out loud: device tokens expire and VAT bands are effective-dated,
                // so a till an hour out can have a day's takings judged against a different instant
                // — and nothing else in the app would ever mention it.
                ? `⚠ This till's clock is out by ${Math.round(Math.abs(conn.clockSkewMs ?? 0) / 1000)}s.`
                  + (conn.detail ? ` ${conn.detail}` : "")
                : conn.detail}
            </span>
          )}
        </button>

        {/* ⚠ "Switch to portal" — the till and the portal are separate sites on separate hosts since
            2026-08-25, so somebody who wants the back office has no way there from here otherwise.
            ⚠ An `<a>`, not a button: it LEAVES this app, and a keyboard or middle-click user should
            get the browser's own behaviour for that.
            ⚠ Hidden when `VITE_PORTAL_URL` is unset. A dead link on a login screen is worse than no
            link, because it is offered to somebody who is already stuck. */}
        {portal && (
          <p className="small centre">
            <a className="linklike" href={portal}>Switch to portal</a>
          </p>
        )}

        <p className="muted small centre">Test environment</p>
      </form>
    </div>
  );
}
