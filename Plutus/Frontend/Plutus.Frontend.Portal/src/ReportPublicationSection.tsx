import { useEffect, useMemo, useState } from "react";
import {
  clearReportPublication, fetchReportPublication, putReportPublication,
  type ReportPublicationState, type TillRow,
} from "./api.ts";
import { ask } from "./Ask.tsx";

/**
 * Ruling 5b(a) — which reports each till shows.
 *
 * ⚠⚠ Matt: *"Portal shows which reports a till can show."* This is that screen. The shop default
 * applies to every till; a till can be given its own list when the counter and the back office want
 * different menus.
 *
 * ⚠⚠ **THE PUBLISH DECIDES THE MENU, THE PERMISSION DECIDES THE DOOR.** Ticking a box here does NOT
 * grant anybody the report — a cashier without the permission still will not see it. The helper text
 * says so, because "I ticked it and Sam still cannot see it" is otherwise a support call.
 *
 * ⚠⚠ **"NOTHING CHOSEN" IS NOT "NOTHING PUBLISHED", AND THE SCREEN MUST NOT CONFLATE THEM.** A shop
 * that has never opened this page has `tenantKeys === null` and sees every report; a shop that
 * deliberately unticked everything has `[]`. Rendering null as "all boxes clear" would tell an owner
 * they had switched their reports off when they had simply never looked.
 *
 * ⚠ Tills poll on their sync cadence, so a change lands without touching the till — same as themes.
 */
export default function ReportPublicationSection({ tills }: { tills: TillRow[] }) {
  const [state, setState] = useState<ReportPublicationState | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  /** Which till is being edited, or "" for the shop default. */
  const [scope, setScope] = useState<string>("");

  /** The ticked set for the scope on screen. Null until loaded. */
  const [ticked, setTicked] = useState<Set<string> | null>(null);

  const load = () =>
    fetchReportPublication()
      .then((s) => { setState(s); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  useEffect(() => { void load(); }, []);

  /** The stored list for the scope on screen, or null when that scope has no row. */
  const storedForScope = useMemo<string[] | null>(() => {
    if (state === null) return null;
    if (scope === "") return state.tenantKeys;
    return state.tills.find((t) => t.tillId === scope)?.keys ?? null;
  }, [state, scope]);

  // ⚠ Re-seed the boxes whenever the scope or the loaded state changes. An unchosen scope seeds from
  // the EFFECTIVE list — every report for the shop default, or the shop default for a till — because
  // that is what the till is showing right now, and a screen that opened with everything clear would
  // invite an owner to "save" and switch the lot off by accident.
  useEffect(() => {
    if (state === null) return;
    const effective = storedForScope
      ?? (scope === "" ? state.catalogue.map((c) => c.key) : (state.tenantKeys ?? state.catalogue.map((c) => c.key)));
    setTicked(new Set(effective));
  }, [state, scope, storedForScope]);

  if (error !== "") return <section className="panel"><p className="error">{error}</p></section>;
  if (state === null || ticked === null) return <section className="panel"><p className="muted">Loading…</p></section>;

  const overridden = scope !== "" && storedForScope !== null;
  const neverChosen = storedForScope === null;

  const toggle = (key: string) => {
    const next = new Set(ticked);
    if (next.has(key)) next.delete(key); else next.add(key);
    setTicked(next);
  };

  const save = async () => {
    // ⚠ PUBLISHING NOTHING IS ALLOWED BUT CONFIRMED. It is a legitimate choice — a stockroom till with
    // no reports at all — and it is also exactly what an accidental "untick all then save" looks like.
    if (ticked.size === 0) {
      const go = await ask.confirm({
        title: "Show no reports at all?",
        body: scope === ""
          ? "Every till will have an empty Reports tab until you publish something."
          : "This till will have an empty Reports tab until you publish something.",
        confirmLabel: "Publish nothing",
        cancelLabel: "Go back",
      });
      if (!go) return;
    }

    setBusy(true);
    try {
      // ⚠ Sent in catalogue order for tidiness; the server re-sorts and de-duplicates regardless.
      const keys = state.catalogue.map((c) => c.key).filter((k) => ticked.has(k));
      await putReportPublication(scope === "" ? null : scope, keys);
      await load();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  };

  const followDefault = async () => {
    setBusy(true);
    try {
      await clearReportPublication(scope === "" ? null : scope);
      await load();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel">
      <div className="panel-head"><h2>Reports on the tills</h2></div>

      <p className="muted small">
        Which reports appear on a till's Reports tab. Tills pick this up on their next sync, so you do
        not need to touch the till.
      </p>

      {/* ⚠ THE SENTENCE THAT PREVENTS A SUPPORT CALL. Publishing is not granting. */}
      <p className="muted small">
        <strong>This decides the menu, not who may read it.</strong> A member of staff still needs the
        matching permission — publishing the VAT report does not show it to somebody whose role cannot
        read VAT. Set permissions under <em>Users &amp; roles</em>.
      </p>

      <div className="toolbar">
        <label>
          Applies to
          <select value={scope} onChange={(e) => setScope(e.target.value)} disabled={busy}>
            <option value="">Every till (shop default)</option>
            {tills.filter((t) => !t.isWebstore).map((t) => (
              <option key={t.id} value={t.id}>{t.name}</option>
            ))}
          </select>
        </label>
      </div>

      {/* ⚠⚠ THE THREE STATES SAID OUT LOUD, because they behave differently and an owner cannot guess. */}
      {scope === "" && neverChosen && (
        <p className="muted small">
          Nothing has been chosen yet, so <strong>every report shows on every till</strong>. The boxes
          below start ticked to match what your tills are showing right now.
        </p>
      )}
      {scope !== "" && !overridden && (
        <p className="muted small">
          This till <strong>follows the shop default</strong>. The boxes start from that default —
          saving here gives this till its own list.
        </p>
      )}
      {overridden && (
        <p className="muted small">
          This till has <strong>its own list</strong>, set {new Date(
            state.tills.find((t) => t.tillId === scope)!.updatedAtUtc,
          ).toLocaleString("en-GB")}.
        </p>
      )}

      <ul className="plain">
        {state.catalogue.map((c) => (
          <li key={c.key}>
            <label>
              <input
                type="checkbox"
                checked={ticked.has(c.key)}
                onChange={() => toggle(c.key)}
                disabled={busy}
              />{" "}
              <strong>{c.label}</strong>
            </label>
            <div className="muted small">{c.blurb}</div>
          </li>
        ))}
      </ul>

      <div className="dialog-actions">
        <button className="primary" onClick={() => void save()} disabled={busy}>
          {busy ? "Saving…" : scope === "" ? "Save shop default" : "Save for this till"}
        </button>
        {overridden && (
          <button className="ghost" onClick={() => void followDefault()} disabled={busy}>
            Follow the shop default instead
          </button>
        )}
        {scope === "" && !neverChosen && (
          // ⚠ The documented way back to out-of-the-box: delete the row rather than tick everything.
          // A shop that ticks all eight and a shop that has never chosen then read the same on the
          // till, but only the second keeps up automatically when a new report is added.
          <button className="ghost" onClick={() => void followDefault()} disabled={busy}>
            Reset to "every report"
          </button>
        )}
      </div>
    </section>
  );
}
