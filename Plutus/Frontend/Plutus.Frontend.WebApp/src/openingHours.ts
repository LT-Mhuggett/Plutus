/**
 * The store's opening hours, as the portal stores them.
 *
 * ⚠⚠ WHY THIS EXISTS. Both tills rendered *"Not set — add opening hours in the management portal"*
 * for **three different situations**: genuinely not set, present but malformed, and present in a
 * shape the reader did not expect. Matt, 2026-08-17: *"webtill does not show the opening hours set in
 * the portal"* — and from that message nobody could tell which of the three it was, because the
 * screen said the same thing for all of them. **A screen that reports one sentence for three states
 * is not information, it is a shrug.**
 *
 * ⚠⚠ AND THE PORTAL HAS AN UNVALIDATED "Advanced (JSON)" TEXTAREA. `StoresPage.tsx`'s hours editor
 * offers a raw textarea whose `onChange` sends the typed string straight to the server — a trailing
 * comma, single quotes or `{mon: […]}` all save happily. The portal then shows the operator their own
 * text back, so the portal looks *set* while every till reads it as *not set*. That is precisely the
 * shape of what was reported, and it is why this parser is TOLERANT and, when it cannot cope, SAYS SO.
 *
 * ⚠ C2 TWIN of `Plutus.Client.Core/OpeningHours.cs`. Same tolerances, same three states, same
 * wording — a shop whose hours read fine on one till and "not set" on the other is the same class of
 * defect, just cheaper than money. `till-design.md` C2 pins the pair.
 *
 * ⚠ NOT money, so it fails OPEN: anything it cannot understand is reported to the operator as
 * unreadable and the rest of the screen renders normally. A store-details screen must never be the
 * thing that takes a till down.
 */

export interface OpeningSpan {
  open: string;
  close: string;
}

export interface OpeningDay {
  /** The portal's own key — `mon`…`sun`. */
  key: string;
  label: string;
  /** Empty means CLOSED that day — not "unknown". */
  spans: OpeningSpan[];
}

export type OpeningHours =
  /** The portal has hours for this store; render the week. */
  | { state: "set"; week: OpeningDay[] }
  /** Nothing stored. The honest answer, and the only one that should send somebody to the portal. */
  | { state: "unset" }
  /** Something IS stored and this cannot read it. ⚠ Never silently shown as "unset". */
  | { state: "unreadable"; detail: string };

/** ⚠ The portal's keys in the portal's own order — never `Date`'s day numbering, which starts on
 *  Sunday and would silently reorder a shop's week. */
export const DAYS: readonly (readonly [string, string])[] = [
  ["mon", "Monday"], ["tue", "Tuesday"], ["wed", "Wednesday"], ["thu", "Thursday"],
  ["fri", "Friday"], ["sat", "Saturday"], ["sun", "Sunday"],
];

/**
 * A day key as the portal writes it, from whatever a human typed.
 *
 * ⚠ `Monday`, `MON`, ` mon ` and `monday` all mean Monday. The simple editor writes `mon`; the
 * advanced textarea is typed by a person, and refusing their whole week over a capital letter would
 * be pedantry with a shop's opening times as the cost.
 */
function dayKey(raw: string): string | null {
  const k = raw.trim().toLowerCase().slice(0, 3);
  return DAYS.some(([key]) => key === k) ? k : null;
}

/**
 * A time, normalised for display.
 *
 * ⚠ `9:00` → `09:00` so the column lines up, and `09:00:00` → `09:00` because a shop's door does not
 * open on a second boundary. Anything else is passed through untouched rather than rejected — a
 * time this does not recognise is still better shown than swallowed.
 */
function time(raw: unknown): string | null {
  if (typeof raw !== "string") return null;
  const t = raw.trim();
  if (!t) return null;
  const m = /^(\d{1,2}):(\d{2})(?::\d{2})?$/.exec(t);
  return m ? `${m[1].padStart(2, "0")}:${m[2]}` : t;
}

/**
 * One span, from the several shapes a person writes it in.
 *
 * ⚠ `open`/`close` are the portal's names; `from`/`to` are what somebody types when they have not
 * looked. ⚠ A plain string `"09:00-17:30"` is accepted too (en dash included — a portal field
 * pasted out of a document carries one).
 */
function span(raw: unknown): OpeningSpan | null {
  if (typeof raw === "string") {
    const parts = raw.split(/[-–—to]+/i).map((p) => p.trim()).filter(Boolean);
    if (parts.length !== 2) return null;
    const open = time(parts[0]);
    const close = time(parts[1]);
    return open && close ? { open, close } : null;
  }

  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;

  // ⚠ Case-insensitive field names for the same reason the keys are: this half of the blob can be
  // hand-typed.
  const lowered: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(raw as Record<string, unknown>)) lowered[k.toLowerCase()] = v;

  const open = time(lowered.open ?? lowered.from ?? lowered.start);
  const close = time(lowered.close ?? lowered.to ?? lowered.end);
  return open && close ? { open, close } : null;
}

/** A day's value, which may be one span, several, or a string. */
function spansOf(raw: unknown): { spans: OpeningSpan[]; hadContent: boolean } {
  if (raw === null || raw === undefined) return { spans: [], hadContent: false };

  // ⚠ `false` / `"closed"` are how a person says shut. Treated as an explicit CLOSED, not as junk.
  if (raw === false) return { spans: [], hadContent: true };
  if (typeof raw === "string" && /^\s*(closed|shut|)\s*$/i.test(raw)) return { spans: [], hadContent: true };

  const list = Array.isArray(raw) ? raw : [raw];
  if (list.length === 0) return { spans: [], hadContent: true };   // `[]` IS "closed", deliberately

  const spans = list.map(span).filter((s): s is OpeningSpan => s !== null);
  return { spans, hadContent: true };
}

/**
 * Read the portal's blob.
 *
 * ⚠⚠ THE THREE STATES ARE THE POINT. `unset` sends somebody to the portal; `unreadable` tells them
 * the portal already holds something and it needs looking at. Collapsing them — which is what both
 * tills did until 2026-08-17 — means a shop with mistyped hours is told to type them again into the
 * same box that is already wrong.
 */
export function parseOpeningHours(json: string | null | undefined): OpeningHours {
  if (!json || !json.trim()) return { state: "unset" };

  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch (e) {
    // ⚠ The parser's own message, verbatim. "Invalid JSON" tells nobody which character to look at.
    return { state: "unreadable", detail: e instanceof Error ? e.message : String(e) };
  }

  if (raw === null) return { state: "unset" };   // a stored literal `null` is nothing stored

  if (typeof raw !== "object" || Array.isArray(raw))
    return {
      state: "unreadable",
      detail: `expected an object of days, e.g. {"mon":[{"open":"09:00","close":"17:30"}]} — got ${
        Array.isArray(raw) ? "a list" : typeof raw}`,
    };

  const entries = Object.entries(raw as Record<string, unknown>);
  if (entries.length === 0) return { state: "unset" };

  const byKey = new Map<string, OpeningSpan[]>();
  const unknownKeys: string[] = [];
  let recognisedContent = false;
  let lostContent = false;

  for (const [rawKey, value] of entries) {
    const key = dayKey(rawKey);
    if (key === null) {
      unknownKeys.push(rawKey);
      continue;
    }
    const { spans, hadContent } = spansOf(value);
    // ⚠ MERGED, not replaced. `{"mon":…,"Monday":…}` is a mistake somebody could make in the
    // advanced box, and dropping the second one would lose an afternoon without saying so.
    byKey.set(key, [...(byKey.get(key) ?? []), ...spans]);
    if (hadContent) recognisedContent = true;
    if (hadContent && spans.length === 0 && value !== false && !Array.isArray(value) && typeof value !== "string")
      lostContent = true;   // an object we could not read as a span — do not pass it off as "closed"
  }

  if (unknownKeys.length > 0 && byKey.size === 0)
    return {
      state: "unreadable",
      detail: `none of these are days: ${unknownKeys.slice(0, 7).join(", ")} — expected mon, tue, wed, thu, fri, sat, sun`,
    };

  if (lostContent && [...byKey.values()].every((s) => s.length === 0))
    return { state: "unreadable", detail: 'each day needs times, e.g. {"open":"09:00","close":"17:30"}' };

  if (!recognisedContent) return { state: "unset" };

  // ⚠ A partly-understood blob still renders — five correct days plus `unknownDayKeys` naming the
  // leftovers beats showing nothing at all.
  return { state: "set", week: DAYS.map(([key, label]) => ({ key, label, spans: byKey.get(key) ?? [] })) };
}

/** What a day reads as on screen. ⚠ "Closed" — never blank, which looks like a broken binding. */
export const dayText = (d: OpeningDay): string =>
  d.spans.length === 0 ? "Closed" : d.spans.map((s) => `${s.open}–${s.close}`).join(", ");

/** ⚠ The keys the portal sent that are not days, so a screen can name them rather than drop them
 *  silently. Empty for a clean blob. */
export function unknownDayKeys(json: string | null | undefined): string[] {
  if (!json || !json.trim()) return [];
  try {
    const raw = JSON.parse(json);
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) return [];
    return Object.keys(raw as Record<string, unknown>).filter((k) => dayKey(k) === null);
  } catch {
    return [];
  }
}
