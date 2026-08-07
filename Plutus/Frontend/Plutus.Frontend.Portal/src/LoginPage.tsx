import { useEffect, useState } from "react";
import { completePasswordReset, login, fetchAuthMethod, requestPasswordReset } from "./api.ts";
import type { Session } from "./session.ts";
import { beginLogin, oidcConfigured } from "./oidc.ts";
import { PlutusMark } from "./PlutusMark.tsx";

/** FE9.1: the emailed reset link lands on `#reset=<token>` — pull it out (and scrub it from the
 *  address bar so the token isn't left in history or copied out of a shared screen). */
function takeResetToken(): string | null {
  const m = /(?:^|[#&])reset=([^&]+)/.exec(window.location.hash);
  if (!m) return null;
  window.history.replaceState(null, "", window.location.pathname + window.location.search);
  return decodeURIComponent(m[1]);
}

/** Email-first sign-in. Everyone lands here; entering an email asks the server how that email
 *  should authenticate. MFA/SSO users (operators, or any tenant that has turned MFA on) go to
 *  Keycloak with the email pre-filled; everyone else gets the classic password step and lands in
 *  the client portal. */
export default function LoginPage({ onLogin }: { onLogin: (s: Session) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [stage, setStage] = useState<"email" | "password">("email");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // FE9.1 self-service reset: "forgot" asks for a link, "reset" completes one from an email.
  const [resetToken, setResetToken] = useState<string | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [notice, setNotice] = useState("");
  const [forgot, setForgot] = useState(false);

  useEffect(() => { setResetToken(takeResetToken()); }, []);

  async function submitForgot(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError("");
    // Always the same confirmation, whether or not the address has an account — telling the
    // difference would let anyone test which emails are registered.
    await requestPasswordReset(email.trim()).catch(() => undefined);
    setNotice(`If ${email.trim()} has an account, a reset link is on its way. It's valid for 48 hours.`);
    setForgot(false);
    setBusy(false);
  }

  async function submitReset(e: React.FormEvent) {
    e.preventDefault();
    if (newPassword !== confirm) { setError("The two passwords don't match."); return; }
    setBusy(true); setError("");
    try {
      await completePasswordReset(resetToken!, newPassword);
      setResetToken(null);
      setNewPassword(""); setConfirm("");
      setNotice("Password changed — sign in with it below.");
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    } finally {
      setBusy(false);
    }
  }

  // Completing an emailed link takes priority over the normal sign-in flow.
  if (resetToken) {
    return (
      <main className="login-shell">
        <form className="login-card" onSubmit={submitReset}>
          <h1>Choose a password</h1>
          <p className="muted small">This link can be used once. Pick something at least 8 characters long.</p>
          <label>
            New password
            <input type="password" autoComplete="new-password" minLength={8} value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)} required autoFocus />
          </label>
          <label>
            Confirm password
            <input type="password" autoComplete="new-password" minLength={8} value={confirm}
              onChange={(e) => setConfirm(e.target.value)} required />
          </label>
          {error && <p className="error small">{error}</p>}
          <button className="primary" disabled={busy || newPassword.length < 8}>{busy ? "Saving…" : "Set password"}</button>
          <button type="button" className="ghost small" onClick={() => { setResetToken(null); setError(""); }}>
            Cancel
          </button>
        </form>
      </main>
    );
  }

  if (forgot) {
    return (
      <main className="login-shell">
        <form className="login-card" onSubmit={submitForgot}>
          <h1>Reset your password</h1>
          <p className="muted small">We'll email you a single-use link.</p>
          <label>
            Email
            <input type="email" autoComplete="username" value={email}
              onChange={(e) => setEmail(e.target.value)} required autoFocus />
          </label>
          {error && <p className="error small">{error}</p>}
          <button className="primary" disabled={busy}>{busy ? "Sending…" : "Send reset link"}</button>
          <button type="button" className="ghost small" onClick={() => { setForgot(false); setError(""); }}>
            Back to sign in
          </button>
        </form>
      </main>
    );
  }

  async function submitEmail(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const { method, loginHint } = await fetchAuthMethod(email.trim());
      if (method === "oidc") {
        if (!oidcConfigured) {
          setError("Single sign-on isn't available on this server. Contact your administrator.");
          setBusy(false);
          return;
        }
        await beginLogin(loginHint || email.trim()); // redirects to Keycloak; stay busy meanwhile
        return;
      }
      setStage("password");
      setBusy(false);
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  }

  async function submitPassword(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      onLogin(await login(email.trim(), password));
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  }

  if (stage === "password") {
    return (
      <main className="login-shell">
        <form className="login-card" onSubmit={submitPassword}>
          <h1><PlutusMark size={30} />Plutus Portal</h1>
          <p className="muted small">Signing in as <strong>{email}</strong>.</p>
          <label>
            Password
            <input type="password" autoComplete="current-password" value={password}
              onChange={(e) => setPassword(e.target.value)} required autoFocus />
          </label>
          {notice && <p className="callout small">{notice}</p>}
          {error && <p className="error small">{error}</p>}
          <button className="primary" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
          <button type="button" className="ghost small"
            onClick={() => { setForgot(true); setError(""); setNotice(""); }}>
            Forgot password?
          </button>
          <button type="button" className="ghost small"
            onClick={() => { setStage("email"); setPassword(""); setError(""); }}>
            Use a different email
          </button>
        </form>
      </main>
    );
  }

  return (
    <main className="login-shell">
      <form className="login-card" onSubmit={submitEmail}>
        <h1><PlutusMark size={30} />Plutus Portal</h1>
        <p className="muted small">Management back office — enter your email to continue.</p>
        <label>
          Email
          <input type="email" autoComplete="username" value={email}
            onChange={(e) => setEmail(e.target.value)} required autoFocus />
        </label>
        {notice && <p className="callout small">{notice}</p>}
        {error && <p className="error small">{error}</p>}
        <button className="primary" disabled={busy}>{busy ? "Checking…" : "Continue"}</button>
        <button type="button" className="ghost small"
          onClick={() => { setForgot(true); setError(""); setNotice(""); }}>
          Forgot password?
        </button>
      </form>
    </main>
  );
}
