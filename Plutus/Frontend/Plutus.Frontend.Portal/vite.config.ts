import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// THE PORTAL's version — versions/portal.txt, its own file, bumped when the portal ships.
// ⚠ Separate from the backend's even though they are usually deployed together: they are two
// artefacts with two rollbacks, and "the portal is on 1.2.0 against backend 1.1.4" is a sentence
// that has to be sayable when a screen misbehaves after a partial deploy.
// See versions/README.md for what X.Y.Z means.
// ⚠⚠ SAME BUG AS THE WEB TILL, found the same day (2026-08-11): the repo path resolves only inside
// a checkout, and the portal is built ON THE MAC from a FLAT COPY with no `versions/` above it — so
// the read threw, the catch swallowed it, and every portal build has shipped claiming 0.0.0. See the
// long note in `Plutus.Frontend.WebApp/vite.config.ts`; both are fixed the same way and must stay
// the same, because the next person to build one will build the other.
function appVersion(): string {
  const fromEnv = process.env.PLUTUS_APP_VERSION?.trim();
  if (fromEnv) return fromEnv;

  for (const rel of ["../../../versions/portal.txt", "./versions/portal.txt"]) {
    try {
      const found = readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8").trim();
      if (found) return found;
    } catch {
      // try the next candidate
    }
  }

  console.warn(
    "\n⚠ PLUTUS: could not resolve the portal version — building as 0.0.0.\n" +
    "  Set PLUTUS_APP_VERSION=$(cat versions/portal.txt) before `npm run build`.\n");
  return "0.0.0";
}

export default defineConfig({
  plugins: [react()],
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
    __APP_VERSION__: JSON.stringify(appVersion()),
  },
  server: {
    port: 5274, // NOT 5173 (ETRIE) and NOT 5273 (web POS)
    // dev + `vite preview` proxy — in production Caddy reverse-proxies /api/* instead
    proxy: {
      "/api": "http://127.0.0.1:5100",
    },
  },
  preview: {
    port: 5274,
    proxy: {
      "/api": "http://127.0.0.1:5100",
    },
  },
});
