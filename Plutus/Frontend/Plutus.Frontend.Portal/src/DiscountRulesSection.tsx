import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";
import { accessToken } from "./auth.ts";
import DataTable, { type Column } from "./DataTable.tsx";
import { ask } from "./Ask.tsx";

/**
 * Scheduled discounts — "Wednesday Warhammer". Set here, applied by every till.
 *
 * ⚠⚠ MATT, 2026-08-20: *"I have Wednesday Warhammer discount that should flag items in the warhammer
 * catergory on a Wednesday as 'Should have 10%'."*
 *
 * ⚠⚠ THIS SCREEN IS THE STRICT HALF OF A DELIBERATELY ASYMMETRIC PAIR — the opening-hours lesson,
 * applied on purpose rather than learned again. The tills are TOLERANT readers of whatever is stored;
 * this writer refuses to create anything a till would have to guess at, and says why in a sentence.
 * A writer as lax as its readers guarantees the next unreadable value reaches production, and there
 * it looks identical to "nothing set".
 *
 * ⚠ On the PRICES tab rather than Company: a discount is a decision about what a customer pays, it is
 * gated on `portal.prices.manage`, and whoever may edit a price may certainly take money off one.
 */

interface DiscountRule {
  id: number;
  name: string;
  type: number;
  /** A FRACTION — 0.10 is 10%. Used when `type === PERCENT`. */
  percentFraction: number;
  /** ⚠ INTEGER PENCE, per unit. Used when `type === FIXED`. The API carries the two units as separate
   *  fields so neither side has to ask which one a single "amount" meant. */
  fixedAmountPence: number;
  autoApply: boolean;
  allApplicable: boolean;
  active: boolean;
  daysOfWeekMask: number | null;
  windowStartLocal: string | null;
  windowEndLocal: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  categoryIds: string[];
  itemIdOnes: string[];
}

interface CategoryRef { id: string; name: string }

const FIXED = 0;
const PERCENT = 1;

/** ⚠ Sunday FIRST, because bit 0 is Sunday everywhere in this system — .NET's `DayOfWeek`, JS's
 *  `getDay()`, the RBAC mask and now this. Rendering Monday-first while indexing Sunday-first is how a
 *  Wednesday promotion silently becomes a Thursday one. */
const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

async function j<T>(method: string, url: string, body?: unknown): Promise<T> {
  const token = accessToken();
  const res = await fetch(url, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep the status */ }
    throw new ApiError(res.status, detail);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

/** How a rule's schedule reads on the list — the answer to "when does this actually fire?". */
const scheduleText = (r: DiscountRule): string => {
  const parts: string[] = [];

  if (r.daysOfWeekMask === null || r.daysOfWeekMask === undefined) parts.push("Every day");
  else parts.push(DAYS.filter((_, i) => (r.daysOfWeekMask! & (1 << i)) !== 0).join(", ") || "No days");

  const hhmm = (t: string | null) => (t ? t.slice(0, 5) : null);
  if (r.windowStartLocal || r.windowEndLocal) {
    parts.push(`${hhmm(r.windowStartLocal) ?? "00:00"}–${hhmm(r.windowEndLocal) ?? "23:59"}`);
  }

  const day = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString("en-GB") : null);
  if (r.validFromUtc || r.validToUtc) {
    parts.push(`${day(r.validFromUtc) ?? "…"} to ${day(r.validToUtc) ?? "…"}`);
  }
  return parts.join(" · ");
};

const targetText = (r: DiscountRule, cats: CategoryRef[]): string => {
  if (r.allApplicable) return "Everything in the basket";
  const names = r.categoryIds.map((id) => cats.find((c) => c.id === id)?.name ?? "a category");
  const bits: string[] = [];
  if (names.length > 0) bits.push(names.join(", "));
  if (r.itemIdOnes.length > 0) bits.push(`${r.itemIdOnes.length} item${r.itemIdOnes.length === 1 ? "" : "s"}`);
  return bits.join(" + ") || "Nothing";
};

const amountText = (r: DiscountRule): string =>
  r.type === PERCENT ? `${+(r.percentFraction * 100).toFixed(2)}%` : gbp(r.fixedAmountPence);

export default function DiscountRulesSection() {
  const [rules, setRules] = useState<DiscountRule[] | null>(null);
  const [cats, setCats] = useState<CategoryRef[]>([]);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<DiscountRule | null>(null);
  const [denied, setDenied] = useState(false);

  /** ⚠ A 403 HERE IS A SENTENCE, NOT A NUMBER — the carrier-bags rule. Somebody who cannot manage
   *  prices should not be shown the bare string "403" and left to raise a support call. */
  const readError = (e: unknown) =>
    e instanceof ApiError && e.status === 403
      ? "Only someone who can manage prices may change the discounts."
      : String(e instanceof Error ? e.message : e);

  const load = () =>
    j<{ rules: DiscountRule[] }>("GET", "/api/v1/discounts/rules/manage")
      .then((r) => { setRules(r.rules); setError(""); })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 403) { setDenied(true); return; }
        setError(readError(e));
      });

  useEffect(() => {
    void load();
    j<CategoryRef[]>("GET", "/api/v1/discounts/rules/categories").then(setCats).catch(() => setCats([]));
  }, []);

  if (denied) return null;

  const columns: Column<DiscountRule>[] = [
    { key: "name", label: "Discount" },
    { key: "amount", label: "Takes off", numeric: true, render: amountText },
    { key: "allApplicable", label: "Applies to", sortable: false, render: (r) => targetText(r, cats) },
    { key: "daysOfWeekMask", label: "When", sortable: false, render: scheduleText },
    {
      key: "autoApply",
      label: "How",
      // ⚠ THE COLUMN THAT ANSWERS THE QUESTION PEOPLE WILL ACTUALLY ASK — "why didn't it come off?".
      // Automatic and by-hand are completely different behaviours at a counter and a single "enabled"
      // tick could not tell them apart.
      render: (r) => (r.autoApply ? "Automatic" : "Operator picks it"),
    },
    { key: "active", label: "Status", render: (r) => (r.active ? "Live" : "Paused") },
  ];

  return (
    <details className="card store-card">
      {/* ⚠ "Discount Settings", not "Discounts" — Matt, 2026-08-20. It is the difference between a
          list of discounts and the place they are CONFIGURED, and this page also carries the price
          list, so the word has to say which of the two you are opening. */}
      <summary><strong>Discount Settings</strong></summary>

      <p className="muted">
        Rules that take money off at the till. An <strong>automatic</strong> rule applies itself
        whenever it is live — a Wednesday-only 10% off a category needs nobody to remember it. A rule
        an <strong>operator picks</strong> stays on the till's discount list until somebody chooses it.
      </p>
      <p className="muted small">
        One discount per line: where a rule and a member's tier discount both apply, the line takes
        whichever is worth more — never both. An operator's own discount always wins over both.
      </p>

      {error !== "" && <p className="error">{error}</p>}

      {rules === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <DataTable
          columns={columns}
          rows={rules}
          getKey={(r) => String(r.id)}
          initialSortKey="name"
          emptyText="No discounts yet."
          search={(r) => `${r.name} ${amountText(r)} ${targetText(r, cats)}`}
          searchPlaceholder="Search discounts…"
          rowActions={(r) => (
            <>
              <button type="button" className="ghost small" onClick={() => setEditing(r)}>Edit</button>{" "}
              {r.active && (
                <button
                  type="button"
                  className="ghost small"
                  onClick={async () => {
                    if (!await ask.confirm({
                      title: `Pause “${r.name}”?`,
                      body: (
                        <>
                          <p className="small">It stops applying at every till on their next sync.</p>
                          <p className="muted small">
                            The rule is kept, not deleted — every sale that ever took this discount
                            points at it, so the record of what you charged stays intact. You can make
                            it live again later.
                          </p>
                        </>
                      ),
                      confirmLabel: "Pause it",
                      danger: true,
                    })) return;
                    await j("DELETE", `/api/v1/discounts/rules/${r.id}`).catch((e) => setError(readError(e)));
                    await load();
                  }}
                >
                  Pause
                </button>
              )}
            </>
          )}
        />
      )}

      <div className="toolbar">
        <button
          type="button"
          className="primary small"
          onClick={() => setEditing({
            id: 0, name: "", type: PERCENT, percentFraction: 0.1, fixedAmountPence: 0,
            autoApply: true, allApplicable: false,
            active: true, daysOfWeekMask: null, windowStartLocal: null, windowEndLocal: null,
            validFromUtc: null, validToUtc: null, categoryIds: [], itemIdOnes: [],
          })}
        >
          Add a discount
        </button>
      </div>

      {editing && (
        <RuleDialog
          rule={editing}
          cats={cats}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await load(); }}
        />
      )}
    </details>
  );
}

/**
 * Create or edit one rule.
 *
 * ⚠ The form holds the percentage as a PERCENT NUMBER ("10") because that is what a person types, and
 * converts to the FRACTION the wire carries (0.1) once, at save. That conversion is the whole reason
 * `LineDiscounts.Percentage` throws above 1.0: the legacy till multiplied a price by a typed "10".
 */
function RuleDialog({ rule, cats, onClose, onSaved }: {
  rule: DiscountRule;
  cats: CategoryRef[];
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const [name, setName] = useState(rule.name);
  const [type, setType] = useState(rule.type);
  // Percent as "10", fixed as pounds "1.50" — both as typed, converted at save.
  const [amount, setAmount] = useState(
    rule.type === PERCENT
      ? String(+(rule.percentFraction * 100).toFixed(2))
      : (rule.fixedAmountPence / 100).toFixed(2),
  );
  const [autoApply, setAutoApply] = useState(rule.autoApply);
  const [allApplicable, setAllApplicable] = useState(rule.allApplicable);
  const [active, setActive] = useState(rule.active);
  const [days, setDays] = useState<number | null>(rule.daysOfWeekMask ?? null);
  const [from, setFrom] = useState(rule.windowStartLocal?.slice(0, 5) ?? "");
  const [to, setTo] = useState(rule.windowEndLocal?.slice(0, 5) ?? "");
  const [validFrom, setValidFrom] = useState(rule.validFromUtc?.slice(0, 10) ?? "");
  const [validTo, setValidTo] = useState(rule.validToUtc?.slice(0, 10) ?? "");
  const [categoryIds, setCategoryIds] = useState<string[]>(rule.categoryIds);
  const [itemText, setItemText] = useState(rule.itemIdOnes.join("\n"));
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const toggleDay = (i: number) => {
    // ⚠ null means EVERY day and 0 means NO days — two different states, and the server refuses the
    // second. Unticking the last day therefore goes back to null (every day) rather than to a rule
    // that can never fire.
    const current = days ?? 0b111_1111;
    const next = current ^ (1 << i);
    setDays(next === 0b111_1111 ? null : next);
  };

  const save = async () => {
    setBusy(true);
    setError("");
    try {
      const typed = Number(amount.replace(/[£%\s,]/g, ""));
      if (!Number.isFinite(typed) || typed <= 0) throw new Error("Enter how much comes off — for example 10 for 10%.");

      await j<{ id: number }>("PUT", "/api/v1/discounts/rules", {
        id: rule.id > 0 ? rule.id : null,
        name,
        type,
        // ⚠ THE ONE CONVERSION, and it is per FIELD rather than per row. A percent typed as "10"
        // becomes the fraction 0.1; pounds typed as "1.50" become 150 PENCE. The API carries the two
        // units separately so nothing downstream has to know which a bare "amount" meant.
        percentFraction: type === PERCENT ? typed / 100 : 0,
        fixedAmountPence: type === PERCENT ? 0 : Math.round(typed * 100),
        autoApply,
        allApplicable,
        active,
        daysOfWeekMask: days,
        windowStartLocal: from ? `${from}:00` : null,
        windowEndLocal: to ? `${to}:00` : null,
        // ⚠ A date input gives a LOCAL calendar day; the column is a UTC instant. Sent as midnight UTC
        // so "from the 19th" means the whole of the 19th everywhere, rather than shifting by the
        // browser's offset — the till reads these against its own clock and must not inherit the
        // portal user's timezone.
        validFromUtc: validFrom ? `${validFrom}T00:00:00Z` : null,
        validToUtc: validTo ? `${validTo}T23:59:59Z` : null,
        categoryIds,
        itemIdOnes: itemText.split(/[\s,]+/).map((s) => s.trim()).filter((s) => s.length > 0),
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="panel" style={{ marginTop: 12 }}>
      <h3>{rule.id > 0 ? `Edit “${rule.name}”` : "New discount"}</h3>

      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label className="grow">
          Name <span className="muted small">(the customer reads this on the receipt)</span>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Wednesday Warhammer" />
        </label>
      </div>

      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>
          Takes off
          <select value={type} onChange={(e) => setType(Number(e.target.value))}>
            <option value={PERCENT}>A percentage</option>
            <option value={FIXED}>A fixed amount, per unit</option>
          </select>
        </label>
        <label>
          {type === PERCENT ? "Percent (%)" : "Amount (£)"}
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" />
        </label>
        <label style={{ alignSelf: "end" }}>
          <input type="checkbox" checked={autoApply} onChange={(e) => setAutoApply(e.target.checked)} />{" "}
          Apply it automatically
        </label>
        <label style={{ alignSelf: "end" }}>
          <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> Live
        </label>
      </div>
      <p className="muted small">
        Leave <strong>Apply it automatically</strong> unticked to make this a discount an operator
        chooses from the till's list instead.
      </p>

      <h4>What it applies to</h4>
      <label>
        <input
          type="checkbox"
          checked={allApplicable}
          onChange={(e) => setAllApplicable(e.target.checked)}
        />{" "}
        Everything in the basket
      </label>
      {!allApplicable && (
        <>
          <p className="muted small">
            Pick the categories, and add individual barcodes if you need extras. Both apply — an item
            matching either one gets the discount.
          </p>
          <div className="toolbar" style={{ flexWrap: "wrap", gap: 12 }}>
            {cats.map((c) => (
              <label key={c.id}>
                <input
                  type="checkbox"
                  checked={categoryIds.includes(c.id)}
                  onChange={(e) =>
                    setCategoryIds(e.target.checked
                      ? [...categoryIds, c.id]
                      : categoryIds.filter((x) => x !== c.id))}
                />{" "}
                {c.name}
              </label>
            ))}
            {cats.length === 0 && <span className="muted small">No categories yet.</span>}
          </div>
          <label className="grow">
            Extra barcodes <span className="muted small">(one per line)</span>
            <textarea rows={3} value={itemText} onChange={(e) => setItemText(e.target.value)} />
          </label>
        </>
      )}

      <h4>When it is live</h4>
      <div className="toolbar" style={{ flexWrap: "wrap", gap: 10 }}>
        {DAYS.map((d, i) => (
          <label key={d}>
            <input
              type="checkbox"
              checked={days === null || (days & (1 << i)) !== 0}
              onChange={() => toggleDay(i)}
            />{" "}
            {d}
          </label>
        ))}
      </div>
      <p className="muted small">All seven ticked means every day.</p>

      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>From (time) <input type="time" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To (time) <input type="time" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>First day <input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} /></label>
        <label>Last day <input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} /></label>
      </div>
      <p className="muted small">
        Leave the times blank for all day. ⚠ A window cannot run over midnight — use two rules, one
        each side of it.
      </p>

      {error !== "" && <p className="error">{error}</p>}

      <div className="toolbar">
        <button type="button" className="primary small" disabled={busy || !name.trim()} onClick={() => void save()}>
          {rule.id > 0 ? "Save changes" : "Create discount"}
        </button>
        <button type="button" className="ghost small" onClick={onClose}>Cancel</button>
      </div>
    </div>
  );
}
