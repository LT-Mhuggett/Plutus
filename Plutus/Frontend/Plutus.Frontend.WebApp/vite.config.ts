import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// The till version, read from the SAME file the .NET side reads (Directory.Build.props).
// ⚠ One number across every surface is the entire point — a per-surface version is a timestamp
// with extra steps. Bump `till-version.txt` at the repo root and rebuild; nothing else to touch.
// Falls back to 0.0.0 rather than throwing: a missing version file must not break the build, and
// an obviously-wrong number reads as "this did not come from the release process" where a blank
// reads as "the label is broken".
function tillVersion(): string {
  try {
    const path = fileURLToPath(new URL("../../../till-version.txt", import.meta.url));
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
    __TILL_VERSION__: JSON.stringify(tillVersion()),
  },
  server: {
    port: 5273, // deliberately NOT 5173 (ETRIE's dev port) to avoid any collision when run on the Mac
  },
});
