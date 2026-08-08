import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// THIS TILL's version — versions/till-web.txt, its own file.
// ⚠ Deliberately NOT shared with the MAUI till. They deploy separately and will diverge (a
// Windows-only printer fix belongs to one of them), so a shared number would force this till to
// claim releases it had no changes in and make "broken on 1.4.2" unanswerable.
// See versions/README.md for what X.Y.Z means.
// Falls back to 0.0.0 rather than throwing: a missing file must not break the build, and an
// obviously-wrong number reads as "did not come from the release process" where a blank reads as
// "the label is broken".
function appVersion(): string {
  try {
    const path = fileURLToPath(new URL("../../../versions/till-web.txt", import.meta.url));
    return readFileSync(path, "utf8").trim() || "0.0.0";
  } catch {
    return "0.0.0";
  }
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
