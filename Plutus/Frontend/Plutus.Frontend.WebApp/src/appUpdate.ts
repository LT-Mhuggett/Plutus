// ─────────────────────────────────────────────────────────────────────────────
// "A new till build is deployed" — the till is a single-page app, so a tab that
// has been open since before a deploy keeps running the OLD code indefinitely.
// Shop tills sit open for days, which made every deploy look like "the fix didn't
// work" until someone pressed F5 (hit repeatedly on 2026-08-07).
//
// Detection compares the hashed bundle filename this tab is RUNNING (import.meta.url)
// against the one the server currently serves in index.html. Vite hashes that name
// per build, so a difference means "a newer build is live" with no version endpoint
// to keep in step.
//
// ⚠ It NEVER reloads by itself. A reload mid-sale would drop the basket; the app
// shows a banner and the operator chooses when. Silence on any failure — offline is
// the normal state for a till, not an error.
// ─────────────────────────────────────────────────────────────────────────────

/** The bundle filename this tab is running, e.g. "index-Cm68zq7D.js". */
function runningBundle(): string | null {
  try {
    return new URL(import.meta.url).pathname.split("/").pop() ?? null;
  } catch {
    return null;
  }
}

/** The bundle filename the SERVER is serving right now, from a no-cache index.html. */
async function deployedBundle(): Promise<string | null> {
  try {
    const res = await fetch("/", { cache: "no-store" });
    if (!res.ok) return null;
    const html = await res.text();
    return html.match(/assets\/(index-[A-Za-z0-9_-]+\.js)/)?.[1] ?? null;
  } catch {
    return null; // offline / server down — not an update signal
  }
}

/**
 * Polls for a newer build and calls back once when one appears. Returns a stop function.
 * First check is delayed — a tab that just loaded is by definition current, and the
 * service worker may still be settling.
 */
export function startUpdateWatcher(onAvailable: () => void, everyMs = 10 * 60_000): () => void {
  const running = runningBundle();
  if (!running) return () => undefined; // dev server / unhashed build — nothing to compare
  let stopped = false;

  const check = async () => {
    if (stopped) return;
    const deployed = await deployedBundle();
    if (!stopped && deployed && deployed !== running) {
      stopped = true; // one shot: the banner stays until the operator reloads
      onAvailable();
    }
  };

  const first = window.setTimeout(check, 60_000);
  const timer = window.setInterval(check, everyMs);
  return () => {
    stopped = true;
    window.clearTimeout(first);
    window.clearInterval(timer);
  };
}
