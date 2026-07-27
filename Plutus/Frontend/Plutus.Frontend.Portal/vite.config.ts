import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
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
