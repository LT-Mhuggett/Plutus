import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// THE LANDING SITE's version — versions/landing.txt, its own file. Same rule as the till, the web
// till and the portal: one version file per deployable (Matt, 2026-08-08: "Each till needs a
// specific version as they will end up diverging").
//
// ⚠⚠ THE FALLBACK MUST STAY. The repo path resolves only inside a checkout, and this is built ON THE
// MAC from a FLAT COPY with no `versions/` above it — so the read throws, the catch swallows it, and
// the build ships claiming 0.0.0. That happened to BOTH other frontends on 2026-08-11. The fix is
// `PLUTUS_APP_VERSION` set explicitly at build time, and the runbook's artefact grep is what proves
// somebody did.
function appVersion(): string {
  const fromEnv = process.env.PLUTUS_APP_VERSION?.trim();
  if (fromEnv) return fromEnv;

  for (const rel of ["../../../versions/landing.txt", "./versions/landing.txt"]) {
    try {
      const found = readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8").trim();
      if (found) return found;
    } catch {
      // try the next candidate
    }
  }

  console.warn(
    "\n⚠ PLUTUS: could not resolve the landing version — building as 0.0.0.\n" +
    "  Set PLUTUS_APP_VERSION=$(cat versions/landing.txt) before `npm run build`.\n");
  return "0.0.0";
}

export default defineConfig({
  plugins: [react()],
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
    __APP_VERSION__: JSON.stringify(appVersion()),
  },
  server: {
    // ⚠ 5275. NOT 5173 (ETRIE — never touch it), NOT 5273 (web till), NOT 5274 (portal).
    port: 5275,
    // dev + `vite preview` proxy — in production Caddy reverse-proxies /api/* instead.
    proxy: {
      "/api": "http://127.0.0.1:5100",
    },
  },
  preview: {
    port: 5275,
    proxy: {
      "/api": "http://127.0.0.1:5100",
    },
  },
});
