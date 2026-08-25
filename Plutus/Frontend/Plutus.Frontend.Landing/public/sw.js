// ⚠⚠ A TOMBSTONE, NOT A SERVICE WORKER. The landing site deliberately has none.
//
// WHY IT EXISTS. Until 2026-08-25 `plutus.huggett.dscloud.me/` served the WEB TILL, which registers
// a service worker at `/sw.js` with scope `/`. When that host became the landing page, every browser
// that had ever opened the till still had that worker installed — and it does not remove itself.
// Left alone it would keep intercepting navigations on this origin and, because it is network-first
// and re-caches whatever `/` returns, would quietly adopt this marketing page as the till's offline
// shell.
//
// ⚠⚠ AND THE OBVIOUS FIX DOES NOT WORK. A browser retires a worker whose script 404s — but this site
// is served with an SPA fallback (`try_files {path} /index.html`), so a missing `/sw.js` would answer
// **200 with HTML**. The browser would fail to parse it as a script, treat the update as failed, and
// KEEP the old worker registered indefinitely. A real file that unregisters itself is the only
// reliable route.
//
// ⚠ Safe to keep for ever. On a browser that never had the till, this installs, unregisters and
// leaves nothing behind.

self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      // ⚠ Drop the till's caches too ("plutus-shell-v2" and anything older), or the storage stays
      // allocated on the customer's machine with a stale app shell in it.
      const keys = await caches.keys();
      await Promise.all(keys.map((k) => caches.delete(k)));

      await self.registration.unregister();

      // ⚠ Reload any open tab so it stops being controlled by a worker that no longer exists —
      // otherwise the page in front of the user is still being served by the thing we just retired.
      const clients = await self.clients.matchAll({ type: "window" });
      for (const c of clients) c.navigate(c.url);
    })(),
  );
});

// ⚠ No fetch handler, deliberately: while this worker is briefly alive it must not serve anything.
