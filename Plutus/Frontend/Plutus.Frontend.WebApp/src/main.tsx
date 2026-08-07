import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App.tsx";
import { applyCachedTheme } from "./theme.ts";

// FE10: apply the last-known theme BEFORE React mounts — a dark-themed till must not flash
// the light scheme on every reload (and an offline reload keeps its colours entirely).
applyCachedTheme();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

// Offline support (plan §3.6): app-shell service worker.
if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch(() => undefined);
  });
}
