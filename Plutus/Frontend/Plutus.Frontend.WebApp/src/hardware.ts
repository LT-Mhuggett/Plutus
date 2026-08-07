import { getDeviceCredential } from "./pipeline.ts";
import { headers } from "./api.ts";

// ─────────────────────────────────────────────────────────────────────────────
// FE3.0 hardware-agent telemetry (the first slice of FE3, buildable before the
// agent exists).
//
// The "Plutus Till Agent" is a small tray app on the till PC that bridges the
// browser to the receipt printer and cash drawer over http://127.0.0.1:9123
// (loopback only — an HTTPS page may fetch loopback HTTP; browsers treat it as
// potentially trustworthy). This module polls its /status and forwards what it
// finds to the server, so the portal's Locations page can see which till PCs
// run which agent version and whether the printer is up.
//
// ⚠ Graceful degradation is the core FE3 rule and it starts here: no agent →
// report "none found" and change NOTHING about how the till behaves. The
// reporter must never throw into the app shell.
// ─────────────────────────────────────────────────────────────────────────────

export const AGENT_URL = "http://127.0.0.1:9123";

/** The agent's pairing token, typed into Settings → Hardware once per till PC. Per device, like the
 *  device credential — localStorage, never sent to the Plutus server. */
const TOKEN_KEY = "plutus.agentToken";
export const getAgentToken = (): string => localStorage.getItem(TOKEN_KEY) ?? "";
export const setAgentToken = (t: string): void => {
  if (t.trim()) localStorage.setItem(TOKEN_KEY, t.trim().toUpperCase());
  else localStorage.removeItem(TOKEN_KEY);
};

export interface AgentStatus {
  agentVersion: string;
  printer?: { name?: string; online?: boolean };
  drawerSupported?: boolean;
  paired?: boolean;
  columns?: number;
}

/** What the agent said, or null when there is no (healthy) agent. Fast: a till
 *  PC without an agent refuses the connection immediately; the timeout only
 *  guards a wedged agent. */
export async function fetchAgentStatus(): Promise<AgentStatus | null> {
  try {
    const ctl = new AbortController();
    const t = setTimeout(() => ctl.abort(), 1500);
    const res = await fetch(`${AGENT_URL}/status`, { signal: ctl.signal });
    clearTimeout(t);
    if (!res.ok) return null;
    const body = (await res.json()) as AgentStatus;
    return body?.agentVersion ? body : null;
  } catch {
    return null; // absent, refused, timed out, mixed-content blocked — all mean "no agent"
  }
}

/**
 * Chrome 142+/Edge 143+ gate HTTPS-page → loopback fetches behind a one-time permission
 * prompt ("Apps on device" since Chrome 145; "Local network access" before that; Firefox 144+
 * has an equivalent). If the operator clicked BLOCK, every agent probe fails exactly like "no
 * agent installed" — and no web page can re-raise the prompt; the operator must re-allow it in
 * the browser's site settings. This reads the decision so Settings → Hardware can say which
 * problem it actually is instead of a generic "no agent found".
 *
 * Permission names vary by browser generation, so try newest-first; a browser that predates
 * the prompt (or Safari, which has none) throws on unknown names → "unknown", meaning the
 * browser isn't the blocker.
 */
export type AgentAccessState = "granted" | "denied" | "prompt" | "unknown";
export async function agentAccessState(): Promise<AgentAccessState> {
  for (const name of ["loopback-network-access", "local-network-access"]) {
    try {
      const q = await navigator.permissions.query({ name: name as PermissionName });
      return q.state;
    } catch { /* this browser doesn't know the name — try the older one */ }
  }
  return "unknown";
}

// ── the print/drawer facade (FE3.3) ──────────────────────────────────────────
// ⚠ GRACEFUL DEGRADATION IS THE RULE. Every function here returns a boolean and swallows its own
// failures: no agent, wrong token, printer off, agent wedged — the till falls back to exactly
// today's behaviour (the on-screen/PDF receipt) and NEVER blocks a sale. A shop must be able to
// keep trading with a broken printer.

/** Cached health so the checkout path doesn't wait on a poll. Refreshed by the reporter. */
let cachedStatus: { at: number; status: AgentStatus | null } = { at: 0, status: null };
const CACHE_MS = 10_000;

/** Is there a usable agent right now? Cached ~10s; never throws. */
export async function agentAvailable(): Promise<AgentStatus | null> {
  if (Date.now() - cachedStatus.at < CACHE_MS) return cachedStatus.status;
  const status = await fetchAgentStatus();
  cachedStatus = { at: Date.now(), status };
  return status;
}

/** The last known status without going near the network — for rendering an indicator. */
export const lastKnownAgent = (): AgentStatus | null => cachedStatus.status;

async function agentPost(path: string, body?: unknown): Promise<{ ok: boolean; detail?: string }> {
  try {
    const token = getAgentToken();
    const res = await fetch(`${AGENT_URL}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...(token ? { "X-Agent-Token": token } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (res.ok) return { ok: true };
    let detail = `Agent returned ${res.status}.`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    return { ok: false, detail };
  } catch (e) {
    return { ok: false, detail: String(e instanceof Error ? e.message : e) };
  }
}

/** Print a rendered document. Returns false when the till should fall back to the PDF receipt. */
export async function printDocument(doc: unknown): Promise<boolean> {
  if (!(await agentAvailable())) return false;
  const r = await agentPost("/print", doc);
  if (!r.ok) {
    cachedStatus = { at: 0, status: null };   // force a re-probe; something changed
    console.warn("[hardware] print failed:", r.detail);
  }
  return r.ok;
}

/** Kick the cash drawer. Silent no-op without an agent — the drawer is opened by hand today. */
export async function openDrawer(): Promise<boolean> {
  if (!(await agentAvailable())) return false;
  const r = await agentPost("/drawer/open");
  if (!r.ok) console.warn("[hardware] drawer failed:", r.detail);
  return r.ok;
}

/** Settings-window test print, surfaced in the till so a manager can check the printer without
 *  walking to the PC's tray icon. Returns the agent's own message on failure. */
export async function testPrint(): Promise<{ ok: boolean; detail?: string }> {
  return agentPost("/print/test");
}

/** The last snapshot we told the server about, so a stable situation (usually
 *  "still no agent") isn't re-posted every poll. Keyed per device credential. */
const SNAPSHOT_KEY = "plutus.agentReport";
const REPORT_EVERY_MS = 6 * 60 * 60 * 1000; // even unchanged, re-confirm a few times a day

/**
 * Poll the local agent and report to the server when something changed (or the
 * last confirmation has gone stale). Safe to call often; never throws.
 */
export async function reportAgentStatus(): Promise<void> {
  try {
    const cred = getDeviceCredential();
    if (!cred) return; // not an enrolled till — nothing to report against

    const status = await fetchAgentStatus();
    const snapshot = JSON.stringify({
      d: cred.deviceId,
      v: status?.agentVersion ?? null,
      p: status?.printer?.name ?? null,
      o: status?.printer?.online ?? null,
    });

    const last = localStorage.getItem(SNAPSHOT_KEY);
    const lastAt = Number(localStorage.getItem(SNAPSHOT_KEY + ".at") ?? 0);
    if (last === snapshot && Date.now() - lastAt < REPORT_EVERY_MS) return;

    const res = await fetch("/api/v1/tills/agent-status", {
      method: "POST",
      headers: { ...headers(), "Content-Type": "application/json" },
      body: JSON.stringify({
        deviceId: cred.deviceId,
        agentVersion: status?.agentVersion ?? null,
        printerName: status?.printer?.name ?? null,
        printerOnline: status?.printer?.online ?? null,
      }),
    });
    if (res.ok) {
      localStorage.setItem(SNAPSHOT_KEY, snapshot);
      localStorage.setItem(SNAPSHOT_KEY + ".at", String(Date.now()));
    }
  } catch {
    // offline / server down — the next poll will try again; the till never cares
  }
}

/** Start the background reporter: once at boot (delayed so login/boot traffic
 *  settles), then every few minutes. Returns a stop function. */
export function startAgentReporter(): () => void {
  const first = window.setTimeout(() => void reportAgentStatus(), 10_000);
  const timer = window.setInterval(() => void reportAgentStatus(), 5 * 60_000);
  return () => { window.clearTimeout(first); window.clearInterval(timer); };
}
