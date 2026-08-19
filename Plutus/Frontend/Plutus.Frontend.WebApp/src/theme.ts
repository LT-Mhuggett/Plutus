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

/**
 * The ink that can actually be READ on this background — `#000000` or `#ffffff`.
 *
 * ⚠⚠ WCAG relative luminance, and the 0.179 threshold is the point where black and white contrast
 * EQUALLY against a background — so either side of it, the answer is the one with more contrast.
 * Deciding by "is the hex big" gets mid-greens wrong, and a mid-green is exactly what a shop with a
 * brand colour sets.
 *
 * ⚠ Only ever black or white. Interpolating a "nearly readable" ink is how you get 2.94:1 — legible
 * enough to ship and illegible under a shop's lights.
 *
 * ⚠ Null for anything that is not a six-digit hex, so a caller keeps its stylesheet default rather
 * than applying a colour derived from rubbish.
 *
 * ⚠⚠ C2 TWIN of `ThemeSlots.ReadableInkOn`.
 */
export function readableInkOn(background: string | null | undefined): string | null {
  if (!background || !/^#[0-9a-fA-F]{6}$/.test(background.trim())) return null;

  const hex = background.trim().slice(1);
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };

  const luminance = 0.2126 * channel(parseInt(hex.slice(0, 2), 16))
    + 0.7152 * channel(parseInt(hex.slice(2, 4), 16))
    + 0.0722 * channel(parseInt(hex.slice(4, 6), 16));

  return luminance > 0.179 ? "#000000" : "#ffffff";
}

/**
 * Fill in the second half of any slot PAIR the portal only half-set — WP-T1 T1.2, 2026-08-19.
 *
 * ⚠⚠ THE DEFAULT CONFIGURATION, NOT AN EDGE CASE. A theme of `{"accent":"#f5f5c0"}` — one pale brand
 * colour, which is what a shop actually sets — left `--accent-ink` at the stylesheet's WHITE, so every
 * accent button rendered white text on pale yellow.
 *
 * ⚠ TWO PAIRS, NAMED, AND NO OTHERS: `accentInk` follows `accent`, `ink` follows `surface`. Inventing a
 * `surface2` from a `surface` is a design decision this has no business making.
 *
 * ⚠ A slot the portal DID set is never touched, even when it contrasts badly — an owner who set both
 * halves owns the result, and overwriting would make the portal's own preview a lie.
 *
 * ⚠⚠ C2 TWIN of `ThemeSlots.WithDerivedPairs`, with the same vectors on both sides. Two tills that fill
 * the gap differently show one shop two different screens from one theme.
 */
export function withDerivedPairs(colors: ThemeColors): ThemeColors {
  const out: ThemeColors = { ...colors };

  const pair = (background: keyof ThemeColors, ink: keyof ThemeColors) => {
    const bg = out[background];
    if (!bg || out[ink]) return;

    const derived = readableInkOn(bg);
    if (derived) out[ink] = derived;
  };

  pair("accent", "accentInk");
  pair("surface", "ink");

  return out;
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

  // ⚠⚠ WP-T1 T1.2 (2026-08-19): fill the second half of a half-set pair, or a lone pale accent leaves
  // white text on it. ⚠ The PARSE above stays literal — clearing an override must restore the
  // stylesheet's own defaults exactly — so the derivation is this separate step.
  colors = withDerivedPairs(colors);
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
