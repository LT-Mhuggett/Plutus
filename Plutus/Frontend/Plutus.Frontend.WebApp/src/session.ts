// Login session for the test-token scheme (backend AuthController). Kept in
// localStorage so a till reload doesn't log the operator out; tokens expire
// server-side after 12h regardless.

export interface Session {
  token: string;
  employeeId: string;
  name: string;
  expiresAt: string;
}

const KEY = "plutus.session";

export function getSession(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const s = JSON.parse(raw) as Session;
    if (new Date(s.expiresAt).getTime() < Date.now()) {
      localStorage.removeItem(KEY);
      return null;
    }
    return s;
  } catch {
    return null;
  }
}

export function setSession(s: Session): void {
  localStorage.setItem(KEY, JSON.stringify(s));
}

export function clearSession(): void {
  localStorage.removeItem(KEY);
}
