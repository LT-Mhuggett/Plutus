import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// THIS TILL's version — versions/till-web.txt, its own file.
// ⚠ Deliberately NOT shared with the MAUI till. They deploy separately and will diverge (a
// Windows-only printer fix belongs to one of them), so a shared number would force this till to
// claim releases it had no changes in and make "broken on 1.4.2" unanswerable.
// See versions/README.md for what X.Y.Z means.
// ⚠⚠ THIS SHIPPED 0.0.0 TO PRODUCTION FOR DAYS, and the reason is the BUILD MACHINE, not the code.
// The repo path below resolves only inside a full checkout. The web till and the portal are built
// ON THE MAC, where `~/PLUTUS/Plutus.Frontend.WebApp/` is a FLAT COPY of the app directory with no
// `versions/` anywhere above it — so the read threw, the catch swallowed it, and every build since
// the version scheme landed has claimed 0.0.0. Matt spotted it on 2026-08-11 in the till's own
// Environment panel and in the portal's Locations → Tills list, where BOTH web tills read v0.0.0
// while the MAUI till (built in the repo, on Windows) correctly read v1.45.0.
//
// ⚠ The original note argued 0.0.0 "reads as did-not-come-from-the-release-process". It does — but
// it read that way in a corner of a settings panel for days while nobody acted, so as a signal it
// was too quiet. It now WARNS ON THE BUILD, where the person doing the release will see it.
//
// Three sources, in order, so every way this project is built works:
//   1. PLUTUS_APP_VERSION — what the Mac deploy sets, because it has no repo.
//   2. the repo file — a normal checkout, dev and CI.
//   3. a `versions/` copied in beside the app — the other way to feed a flat copy.
function appVersion(): string {
  const fromEnv = process.env.PLUTUS_APP_VERSION?.trim();
  if (fromEnv) return fromEnv;

  for (const rel of ["../../../versions/till-web.txt", "./versions/till-web.txt"]) {
    try {
      const found = readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8").trim();
      if (found) return found;
    } catch {
      // try the next candidate
    }
  }

  // ⚠ LOUD, and on stderr. A till that cannot state its version turns every "which build broke it?"
  // into guesswork, and this is the last moment anyone can notice before it ships.
  console.warn(
    "\n⚠ PLUTUS: could not resolve the till version — building as 0.0.0.\n" +
    "  Set PLUTUS_APP_VERSION=$(cat versions/till-web.txt) before `npm run build`.\n");
  return "0.0.0";
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
    __APP_VERSION__: JSON.stringify(appVersion()),
  },
  server: {
    port: 5273, // deliberately NOT 5173 (ETRIE's dev port) to avoid any collision when run on the Mac
  },
});
