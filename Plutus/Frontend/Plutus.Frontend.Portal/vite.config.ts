import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// THE PORTAL's version — versions/portal.txt, its own file, bumped when the portal ships.
// ⚠ Separate from the backend's even though they are usually deployed together: they are two
// artefacts with two rollbacks, and "the portal is on 1.2.0 against backend 1.1.4" is a sentence
// that has to be sayable when a screen misbehaves after a partial deploy.
// See versions/README.md for what X.Y.Z means.
function appVersion(): string {
  try {
    const path = fileURLToPath(new URL("../../../versions/portal.txt", import.meta.url));
    return readFileSync(path, "utf8").trim() || "0.0.0";
  } catch {
    return "0.0.0";
  }
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
