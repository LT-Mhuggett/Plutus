import { useState } from "react";
import { login, BUSINESS_NAME } from "./api.ts";
import type { Session } from "./session.ts";
import { PlutusMark } from "./PlutusMark.tsx";

interface Props {
  onLogin: (session: Session) => void;
}

export default function LoginPage({ onLogin }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      onLogin(await login(email.trim(), password));
    } catch (err) {
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

        <p className="muted small centre">Test environment</p>
      </form>
    </div>
  );
}
