// Portal login session (same CompactToken scheme as the till; separate storage key so a
// browser can hold both apps' sessions independently).

export interface Session {
  token: string;
  employeeId: string;
  name: string;
  expiresAt: string;
}

const KEY = "plutus.portal.session";

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

export const setSession = (s: Session) => localStorage.setItem(KEY, JSON.stringify(s));
export const clearSession = () => localStorage.removeItem(KEY);
