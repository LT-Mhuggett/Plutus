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

export interface AgentStatus {
  agentVersion: string;
  printer?: { name?: string; online?: boolean };
  drawerSupported?: boolean;
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
