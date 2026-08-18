import { useEffect, useMemo, useState } from "react";
import {
  createTheme, createTillGroup, deleteTheme, deleteTillGroup, fetchThemes, putThemeAssignment,
  updateTheme, updateTillGroup,
  type StoreRow, type ThemeColors, type ThemeRow, type ThemesBundle, type TillGroupRow, type TillRow,
} from "./api.ts";
import { ask } from "./Ask.tsx";

/**
 * FE10 — Till themes. Define colour schemes and push them to the whole company, a store, a
 * named group of tills, or one till. Precedence is resolved on the server (till > group >
 * store > tenant); tills poll on their 60-second sync cadence, so a change lands everywhere
 * within a minute without anyone touching the till.
 *
 * The two built-ins are never stored: "Plutus Light" / "Plutus Dark" force a mode with the
 * stock pastel palette, and clearing every assignment always returns a till to the default —
 * the "can always switch back" guarantee.
 */

// Mirrors the till's stock palette (WebApp src/index.css :root) — used for previews and as the
// base under a custom scheme's overrides. If the till's defaults change, change these with them.
const BASE: Record<"light" | "dark", Required<ThemeColors>> = {
  light: { accent: "#2c698d", accentInk: "#ffffff", surface: "#f7fafc", surface2: "#ffffff", ink: "#272643", inkMuted: "#475466", line: "#d8e3ea" },
  dark: { accent: "#2c698d", accentInk: "#ffffff", surface: "#1a1a2e", surface2: "#20203a", ink: "#e3f6f5", inkMuted: "#93a5b5", line: "#2c3a4d" },
};

const SLOTS: { key: keyof ThemeColors; label: string; hint: string }[] = [
  { key: "accent", label: "Band & buttons", hint: "the bar across the top, Checkout, primary buttons" },
  { key: "accentInk", label: "Text on the band", hint: "must stay readable on the band colour" },
  { key: "surface", label: "Page background", hint: "" },
  { key: "surface2", label: "Cards & dialogs", hint: "panels, menus, inputs" },
  { key: "ink", label: "Text", hint: "the main text colour" },
  { key: "inkMuted", label: "Secondary text", hint: "table headers, hints" },
  { key: "line", label: "Lines & borders", hint: "" },
];

function parseColors(json: string | null): ThemeColors {
  if (!json) return {};
  try { return JSON.parse(json) as ThemeColors; } catch { return {}; }
}

// WCAG relative luminance → contrast ratio, for the editor's readability warnings.
function luminance(hex: string): number {
  const c = [1, 3, 5].map((i) => {
    const v = parseInt(hex.slice(i, i + 2), 16) / 255;
    return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}
function contrast(a: string, b: string): number {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
}

/** The colours a theme actually renders with: base mode filled with its overrides. */
function effectiveColors(baseMode: "light" | "dark", overrides: ThemeColors): Required<ThemeColors> {
  return { ...BASE[baseMode], ...Object.fromEntries(Object.entries(overrides).filter(([, v]) => v)) } as Required<ThemeColors>;
}

/** A miniature till — band, tabs, a card, the checkout button — in the scheme's colours. */
function ThemePreview({ colors }: { colors: Required<ThemeColors> }) {
  return (
    <div style={{ border: `1px solid ${colors.line}`, borderRadius: 8, overflow: "hidden", maxWidth: "26rem" }}>
      <div style={{ background: colors.accent, color: colors.accentInk, padding: "0.4rem 0.7rem", display: "flex", gap: "0.7rem", alignItems: "center", fontSize: "0.8rem" }}>
        <strong>Plutus</strong>
        <span style={{ opacity: 0.95, borderBottom: `2px solid ${colors.accentInk}` }}>Till</span>
        <span style={{ opacity: 0.65 }}>Cash</span>
        <span style={{ opacity: 0.65 }}>Reporting</span>
      </div>
      <div style={{ background: colors.surface, padding: "0.7rem" }}>
        <div style={{ background: colors.surface2, border: `1px solid ${colors.line}`, borderRadius: 6, padding: "0.6rem 0.7rem", fontSize: "0.8rem" }}>
          <div style={{ color: colors.ink, fontWeight: 600 }}>Batman: Year One — £14.99</div>
          <div style={{ color: colors.inkMuted, fontSize: "0.72rem" }}>QUANTITY · NAME · PRICE · TAX</div>
          <div style={{ borderTop: `1px solid ${colors.line}`, marginTop: "0.45rem", paddingTop: "0.45rem", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ color: colors.ink }}>Sale Inc. Tax <strong>£14.99</strong></span>
            <span style={{ background: colors.accent, color: colors.accentInk, borderRadius: 5, padding: "0.25rem 0.8rem", fontWeight: 700, fontSize: "0.75rem" }}>Checkout</span>
          </div>
        </div>
      </div>
    </div>
  );
}

function ContrastWarnings({ colors }: { colors: Required<ThemeColors> }) {
  const checks: { pair: string; ratio: number }[] = [
    { pair: "band text on band", ratio: contrast(colors.accent, colors.accentInk) },
    { pair: "text on page", ratio: contrast(colors.surface, colors.ink) },
    { pair: "text on cards", ratio: contrast(colors.surface2, colors.ink) },
    { pair: "secondary text on cards", ratio: contrast(colors.surface2, colors.inkMuted) },
  ];
  const bad = checks.filter((c) => c.ratio < 4.5);
  if (bad.length === 0) return null;
  return (
    <p className="error small">
      ⚠ Hard to read: {bad.map((b) => `${b.pair} (${b.ratio.toFixed(1)}:1)`).join(", ")} — aim for 4.5:1 or better.
    </p>
  );
}

/** Inline editor for one scheme (create or edit) — checkbox per slot keeps “not overridden”
 *  honest: an untouched slot follows the base mode, it isn't silently pinned. */
function ThemeEditor({ theme, onDone }: { theme: ThemeRow | null; onDone: (saved: boolean) => void }) {
  const [name, setName] = useState(theme?.name ?? "");
  const [baseMode, setBaseMode] = useState<"light" | "dark">(theme?.baseMode ?? "light");
  const [overrides, setOverrides] = useState<ThemeColors>(parseColors(theme?.colorsJson ?? null));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const colors = effectiveColors(baseMode, overrides);

  async function save() {
    setSaving(true); setError("");
    try {
      const cleaned = Object.fromEntries(Object.entries(overrides).filter(([, v]) => v && /^#[0-9a-fA-F]{6}$/.test(v)));
      const body = { name: name.trim(), baseMode, colorsJson: Object.keys(cleaned).length ? JSON.stringify(cleaned) : null };
      if (theme) await updateTheme(theme.id, body);
      else await createTheme(body);
      onDone(true);
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally { setSaving(false); }
  }

  return (
    <div className="card" style={{ padding: "0.8rem 1rem" }}>
      <div className="toolbar">
        <label className="small"><strong>{theme ? `Edit “${theme.name}”` : "New scheme"}</strong></label>
      </div>
      <div className="form-grid">
        <label>Name
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Kapow House" maxLength={60} />
        </label>
        <label>Base
          <select value={baseMode} onChange={(e) => setBaseMode(e.target.value as "light" | "dark")}>
            <option value="light">Dark colours on a light background</option>
            <option value="dark">Light colours on a dark background</option>
          </select>
        </label>
      </div>
      <div style={{ display: "flex", gap: "1.2rem", flexWrap: "wrap", alignItems: "flex-start" }}>
        <div>
          {SLOTS.map((s) => {
            const set = overrides[s.key] !== undefined && overrides[s.key] !== "";
            return (
              <label key={s.key} className="small" style={{ display: "flex", alignItems: "center", gap: "0.5rem", padding: "0.15rem 0" }}>
                <input
                  type="checkbox" checked={set}
                  onChange={(e) => setOverrides((o) => e.target.checked
                    ? { ...o, [s.key]: BASE[baseMode][s.key] }
                    : Object.fromEntries(Object.entries(o).filter(([k]) => k !== s.key)))}
                />
                <input
                  type="color" value={overrides[s.key] ?? BASE[baseMode][s.key]} disabled={!set}
                  onChange={(e) => setOverrides((o) => ({ ...o, [s.key]: e.target.value }))}
                />
                <span style={{ minWidth: "9.5rem" }}>{s.label}</span>
                <span className="muted">{set ? overrides[s.key] : "base"}{s.hint && ` — ${s.hint}`}</span>
              </label>
            );
          })}
        </div>
        <div>
          <ThemePreview colors={colors} />
          <ContrastWarnings colors={colors} />
        </div>
      </div>
      {error && <p className="error small">{error}</p>}
      <div className="toolbar" style={{ marginTop: "0.5rem" }}>
        <button className="primary small" disabled={saving || !name.trim()} onClick={() => void save()}>
          {saving ? "Saving…" : "Save scheme"}
        </button>
        <button className="ghost small" onClick={() => onDone(false)}>Cancel</button>
      </div>
    </div>
  );
}

function GroupEditor({ group, tills, onDone }: { group: TillGroupRow | null; tills: TillRow[]; onDone: (saved: boolean) => void }) {
  const [name, setName] = useState(group?.name ?? "");
  const [members, setMembers] = useState<Set<string>>(new Set(group?.tillIds ?? []));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  async function save() {
    setSaving(true); setError("");
    try {
      const body = { name: name.trim(), tillIds: [...members] };
      if (group) await updateTillGroup(group.id, body);
      else await createTillGroup(body);
      onDone(true);
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally { setSaving(false); }
  }

  return (
    <div className="card" style={{ padding: "0.8rem 1rem" }}>
      <label className="small" style={{ display: "flex", flexDirection: "column", gap: "0.25rem", maxWidth: "18rem" }}>Group name
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Counter tills" maxLength={60} />
      </label>
      <p className="muted small" style={{ margin: "0.5rem 0 0.2rem" }}>Tills in this group:</p>
      {tills.filter((t) => !t.isWebstore).map((t) => (
        <label key={t.id} className="small" style={{ display: "flex", gap: "0.5rem", padding: "0.1rem 0" }}>
          <input
            type="checkbox" checked={members.has(t.id)}
            onChange={(e) => setMembers((m) => { const n = new Set(m); if (e.target.checked) n.add(t.id); else n.delete(t.id); return n; })}
          />
          {t.name}
        </label>
      ))}
      {error && <p className="error small">{error}</p>}
      <div className="toolbar" style={{ marginTop: "0.5rem" }}>
        <button className="primary small" disabled={saving || !name.trim()} onClick={() => void save()}>
          {saving ? "Saving…" : "Save group"}
        </button>
        <button className="ghost small" onClick={() => onDone(false)}>Cancel</button>
      </div>
    </div>
  );
}

export default function TillThemesSection({ stores, tills }: { stores: StoreRow[]; tills: TillRow[] }) {
  const [bundle, setBundle] = useState<ThemesBundle | null>(null);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<ThemeRow | null | "new">(null);
  const [editingGroup, setEditingGroup] = useState<TillGroupRow | null | "new">(null);

  const load = () => fetchThemes().then(setBundle).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void load(); }, []);

  const themeName = useMemo(() => {
    const map = new Map<string, string>();
    map.set("builtin:system", "Plutus (follow device)");
    map.set("builtin:light", "Plutus Light");
    map.set("builtin:dark", "Plutus Dark");
    for (const t of bundle?.themes ?? []) map.set(t.id.toLowerCase(), t.name);
    return map;
  }, [bundle]);

  const current = (scope: number, scopeKey: string) =>
    bundle?.assignments.find((a) => a.scope === scope && a.scopeKey.toLowerCase() === scopeKey.toLowerCase())?.themeKey ?? "";

  async function assign(scope: number, scopeKey: string, themeKey: string) {
    setError("");
    try {
      await putThemeAssignment(scope, scopeKey, themeKey === "" ? null : themeKey);
      await load();
    } catch (e) { setError(String(e instanceof Error ? e.message : e)); }
  }

  function ThemeSelect({ scope, scopeKey, inheritLabel }: { scope: number; scopeKey: string; inheritLabel: string }) {
    return (
      <select className="small" value={current(scope, scopeKey)} onChange={(e) => void assign(scope, scopeKey, e.target.value)}>
        <option value="">{inheritLabel}</option>
        <option value="builtin:system">Plutus (follow device)</option>
        <option value="builtin:light">Plutus Light</option>
        <option value="builtin:dark">Plutus Dark</option>
        {(bundle?.themes ?? []).map((t) => <option key={t.id} value={t.id.toLowerCase()}>{t.name}</option>)}
      </select>
    );
  }

  async function removeTheme(t: ThemeRow) {
    const uses = bundle?.assignments.filter((a) => a.themeKey.toLowerCase() === t.id.toLowerCase()).length ?? 0;
    if (!await ask.confirm({
      title: `Delete scheme “${t.name}”?`,
      body: <p className="small">{uses > 0
        ? `It is assigned in ${uses} place${uses === 1 ? "" : "s"} — those tills fall back to the next level up (or the Plutus default) within a minute.`
        : "It isn't assigned anywhere."}</p>,
      confirmLabel: "Delete scheme", danger: true,
    })) return;
    try { await deleteTheme(t.id); await load(); } catch (e) { setError(String(e instanceof Error ? e.message : e)); }
  }

  async function removeGroup(g: TillGroupRow) {
    if (!await ask.confirm({
      title: `Delete group “${g.name}”?`,
      body: <p className="small">Its theme assignment (if any) goes with it; member tills fall back to their store or company scheme.</p>,
      confirmLabel: "Delete group", danger: true,
    })) return;
    try { await deleteTillGroup(g.id); await load(); } catch (e) { setError(String(e instanceof Error ? e.message : e)); }
  }

  const realTills = tills.filter((t) => !t.isWebstore);

  return (
    <>
      <p className="muted small">
        Colour schemes for the web till (and the native till, once retrofitted). Most specific
        wins: a till's own scheme beats its group's, a group's beats the store's, the store's
        beats the company default. Tills apply changes within a minute — no reload.
      </p>
      {error && <p className="error small">{error}</p>}

      {/* schemes */}
      <div className="toolbar">
        <strong className="small">Schemes</strong>
        {editing === null && <button className="ghost small" onClick={() => setEditing("new")}>+ New scheme</button>}
      </div>
      {(bundle?.themes ?? []).length === 0 && editing === null &&
        <p className="muted small">None yet — the built-ins (Plutus, Plutus Light, Plutus Dark) are always available below.</p>}
      {(bundle?.themes ?? []).map((t) => {
        const colors = effectiveColors(t.baseMode, parseColors(t.colorsJson));
        return (
          <div key={t.id} className="toolbar" style={{ alignItems: "center" }}>
            <span style={{ display: "inline-flex", borderRadius: 4, overflow: "hidden", border: `1px solid ${colors.line}` }}>
              {[colors.accent, colors.surface, colors.surface2, colors.ink].map((c, i) =>
                <span key={i} style={{ width: 16, height: 16, background: c }} />)}
            </span>
            <span className="small grow">
              <strong>{t.name}</strong>{" "}
              <span className="muted">({t.baseMode === "dark" ? "dark" : "light"} base)</span>
              {/* ⚠⚠ "NOT APPLIED ANYWHERE" — the trap this closes, 2026-08-18. Matt created a scheme,
                  expected the tills to change, and reported that colour customisation did not work.
                  It did: `TillThemes` held his scheme and `TillThemeAssignments` was EMPTY, so
                  `/themes/effective` correctly resolved to nothing and both tills correctly kept the
                  default. **Creating a scheme and applying it are two separate steps, and only the
                  first one looked finished.**

                  ⚠ The count was already being computed — for the DELETE confirmation, which says
                  "It isn't assigned anywhere." So the till knew, the portal knew, and the only place
                  it was ever said out loud was a dialog you reach by trying to delete the thing.
                  Same shape as the opening-hours Save button: the information existed, in the one
                  place nobody looks. */}
              {(bundle?.assignments.filter((a) => a.themeKey.toLowerCase() === t.id.toLowerCase()).length ?? 0) === 0
                ? <span className="error small"> · not applied anywhere — pick it under <em>Where each scheme applies</em></span>
                : <span className="muted small"> · applied in {bundle?.assignments.filter((a) => a.themeKey.toLowerCase() === t.id.toLowerCase()).length} place(s)</span>}
            </span>
            <button className="ghost small" onClick={() => setEditing(t)}>Edit</button>
            <button className="ghost small" onClick={() => void removeTheme(t)}>Delete</button>
          </div>
        );
      })}
      {editing !== null && (
        <ThemeEditor
          theme={editing === "new" ? null : editing}
          onDone={(saved) => { setEditing(null); if (saved) void load(); }}
        />
      )}

      {/* groups */}
      <div className="toolbar" style={{ marginTop: "0.8rem" }}>
        <strong className="small">Till groups</strong>
        {editingGroup === null && <button className="ghost small" onClick={() => setEditingGroup("new")}>+ New group</button>}
      </div>
      {(bundle?.groups ?? []).length === 0 && editingGroup === null &&
        <p className="muted small">Optional — group tills that should share a scheme without being a whole store.</p>}
      {(bundle?.groups ?? []).map((g) => (
        <div key={g.id} className="toolbar" style={{ alignItems: "center" }}>
          <span className="small grow">
            <strong>{g.name}</strong>{" "}
            <span className="muted">
              ({g.tillIds.length} till{g.tillIds.length === 1 ? "" : "s"}: {g.tillIds.map((id) => realTills.find((t) => t.id === id)?.name ?? "?").join(", ") || "empty"})
            </span>
          </span>
          <button className="ghost small" onClick={() => setEditingGroup(g)}>Edit</button>
          <button className="ghost small" onClick={() => void removeGroup(g)}>Delete</button>
        </div>
      ))}
      {editingGroup !== null && (
        <GroupEditor
          group={editingGroup === "new" ? null : editingGroup} tills={tills}
          onDone={(saved) => { setEditingGroup(null); if (saved) void load(); }}
        />
      )}

      {/* assignments */}
      <div className="toolbar" style={{ marginTop: "0.8rem" }}>
        <strong className="small">Where each scheme applies</strong>
      </div>
      <table>
        <thead><tr><th>Target</th><th>Scheme</th><th className="muted small">Currently</th></tr></thead>
        <tbody>
          <tr>
            <td><strong>Whole company</strong> <span className="muted small">— the default for every till</span></td>
            <td><ThemeSelect scope={0} scopeKey="" inheritLabel="Plutus default (follows each device)" /></td>
            <td className="muted small">{themeName.get(current(0, "").toLowerCase()) ?? (current(0, "") ? current(0, "") : "default")}</td>
          </tr>
          {stores.map((s) => (
            <tr key={s.id}>
              <td>Store — {s.name ?? `Store ${s.id}`}</td>
              <td><ThemeSelect scope={1} scopeKey={String(s.id)} inheritLabel="(inherit company)" /></td>
              <td className="muted small">{themeName.get(current(1, String(s.id)).toLowerCase()) ?? "inherits"}</td>
            </tr>
          ))}
          {(bundle?.groups ?? []).map((g) => (
            <tr key={g.id}>
              <td>Group — {g.name}</td>
              <td><ThemeSelect scope={2} scopeKey={g.id} inheritLabel="(inherit store / company)" /></td>
              <td className="muted small">{themeName.get(current(2, g.id).toLowerCase()) ?? "inherits"}</td>
            </tr>
          ))}
          {realTills.map((t) => (
            <tr key={t.id}>
              <td>Till — {t.name} <span className="muted small">({stores.find((s) => s.id === t.storeId)?.name ?? `Store ${t.storeId}`})</span></td>
              <td><ThemeSelect scope={3} scopeKey={t.id} inheritLabel="(inherit group / store / company)" /></td>
              <td className="muted small">{themeName.get(current(3, t.id).toLowerCase()) ?? "inherits"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
