// FE10 till theming — the portal decides the colours, the till wears them.
//
// HOW IT WORKS: index.css defines every colour as a CSS custom property on :root, each with a
// light-dark() pair, and the whole stylesheet already keys off `color-scheme`. So a theme is
// just (1) forcing color-scheme to light/dark (or leaving it "light dark" = follow the device)
// and (2) overriding a handful of slot variables with single values. No second stylesheet.
//
// The EFFECTIVE theme comes from GET /api/v1/themes/effective (resolved server-side:
// till > group > store > tenant > default) and is cached in localStorage so an offline reload
// keeps its colours; main.tsx applies the cache synchronously before first paint so the till
// never flashes the default scheme.

import { effectiveStoreId, headers } from "./api.ts";
import { getDeviceCredential } from "./pipeline.ts";

export interface ThemeColors {
  accent?: string;    // the band + primary buttons (the "blue bar")
  accentInk?: string; // text on the band/buttons
  surface?: string;   // page background
  surface2?: string;  // cards, dialogs, menus
  ink?: string;       // main text
  inkMuted?: string;  // secondary text (table headers, hints)
  line?: string;      // borders and rules
}

export interface EffectiveTheme {
  source: string; // till | group | store | tenant | default
  themeKey: string | null;
  name: string | null;
  baseMode: "system" | "light" | "dark";
  colorsJson: string | null;
}

const KEY = "plutus.till.theme";
const DEFAULT_ACCENT = "#2c698d";

const SLOT_VARS: Record<keyof ThemeColors, string> = {
  accent: "--accent",
  accentInk: "--accent-ink",
  surface: "--surface",
  surface2: "--surface-2",
  ink: "--ink",
  inkMuted: "--ink-muted",
  line: "--line",
};

/** What Settings shows: "Plutus Dark — set for this store in the portal". */
export function currentThemeLabel(): { name: string; source: string } {
  const t = getCached();
  if (!t || !t.themeKey) return { name: "Plutus (light & dark follow this device)", source: "default" };
  return { name: t.name ?? "Custom", source: t.source };
}

function getCached(): EffectiveTheme | null {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as EffectiveTheme) : null;
  } catch {
    return null;
  }
}

/** Set (or clear) the theme on :root. Only ever touches the slot variables + color-scheme, so
 *  clearing restores the stylesheet's own pastel defaults — the "always switchable back" rule. */
export function applyTheme(theme: EffectiveTheme | null): void {
  const root = document.documentElement.style;
  root.colorScheme = theme == null || theme.baseMode === "system" ? "light dark" : theme.baseMode;

  let colors: ThemeColors = {};
  if (theme?.colorsJson) {
    try { colors = JSON.parse(theme.colorsJson) as ThemeColors; } catch { /* opaque blob was bad — base mode still applies */ }
  }
  for (const slot of Object.keys(SLOT_VARS) as (keyof ThemeColors)[]) {
    const v = colors[slot];
    // hex only — this is the till's last line of defence against a bad value in the blob
    if (v && /^#[0-9a-fA-F]{6}$/.test(v)) root.setProperty(SLOT_VARS[slot], v);
    else root.removeProperty(SLOT_VARS[slot]);
  }

  // keep the browser-chrome colour on the band (PWA title bar etc.)
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", colors.accent ?? DEFAULT_ACCENT);
}

/** Called from main.tsx before React mounts — no flash of the wrong scheme. */
export function applyCachedTheme(): void {
  applyTheme(getCached());
}

/** Fetch the resolved theme for this till and apply it. Errors keep the last-known theme —
 *  colours must never depend on the network being up. */
export async function refreshTheme(): Promise<void> {
  try {
    const cred = getDeviceCredential();
    const q = cred?.tillId ? `tillId=${cred.tillId}` : `storeId=${effectiveStoreId()}`;
    const res = await fetch(`/api/v1/themes/effective?${q}`, { headers: headers() });
    if (!res.ok) return;
    const theme = (await res.json()) as EffectiveTheme;
    localStorage.setItem(KEY, JSON.stringify(theme));
    applyTheme(theme);
  } catch { /* offline — keep last-known */ }
}
