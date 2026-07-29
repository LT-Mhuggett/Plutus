import { useState } from "react";
import { login, fetchAuthMethod } from "./api.ts";
import type { Session } from "./session.ts";
import { beginLogin, oidcConfigured } from "./oidc.ts";

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
          <h1>Plutus Portal</h1>
          <p className="muted small">Signing in as <strong>{email}</strong>.</p>
          <label>
            Password
            <input type="password" autoComplete="current-password" value={password}
              onChange={(e) => setPassword(e.target.value)} required autoFocus />
          </label>
          {error && <p className="error small">{error}</p>}
          <button className="primary" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
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
        <h1>Plutus Portal</h1>
        <p className="muted small">Management back office — enter your email to continue.</p>
        <label>
          Email
          <input type="email" autoComplete="username" value={email}
            onChange={(e) => setEmail(e.target.value)} required autoFocus />
        </label>
        {error && <p className="error small">{error}</p>}
        <button className="primary" disabled={busy}>{busy ? "Checking…" : "Continue"}</button>
      </form>
    </main>
  );
}
