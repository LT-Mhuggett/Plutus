import { useEffect, useState } from "react";
import { closePeriod, createPeriod, fetchPeriods, gbp, type Period } from "./api.ts";
import DataTable from "./DataTable.tsx";

/** Financial periods (WP3.4): create, close (snapshot + lock). Late sales into a closed
 *  period post to the next open day and are flagged in the audit trail. */
export default function PeriodsPage() {
  const [periods, setPeriods] = useState<Period[]>([]);
  const [error, setError] = useState("");
  const [form, setForm] = useState({ name: "", startDay: "", endDay: "" });
  const [busy, setBusy] = useState(false);

  const refresh = () => fetchPeriods().then(setPeriods).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await createPeriod(form.name, form.startDay, form.endDay);
      setForm({ name: "", startDay: "", endDay: "" });
      await refresh();
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
    } finally {
      setBusy(false);
    }
  }

  function snapshotSummary(json: string | null): string {
    if (!json) return "";
    try {
      const s = JSON.parse(json);
      return `${gbp(s.grossPence)} gross · ${gbp(s.vatPence)} VAT · ${s.txnCount} txns`;
    } catch {
      return "";
    }
  }

  return (
    <section className="panel">
      <h2>Financial periods</h2>
      {error && <p className="error">{error}</p>}

      <DataTable<Period>
        columns={[
          { key: "name", label: "Name" },
          { key: "startDay", label: "Range", render: (p) => `${p.startDay} → ${p.endDay}` },
          { key: "status", label: "Status", render: (p) => `${p.status}${p.closedAtUtc ? ` (${new Date(p.closedAtUtc + "Z").toLocaleDateString("en-GB")})` : ""}` },
          { key: "snapshotJson", label: "Snapshot at close", sortable: false, render: (p) => <span className="small">{snapshotSummary(p.snapshotJson)}</span> },
        ]}
        rows={periods} getKey={(p) => p.id} initialSortKey="startDay"
        search={(p) => `${p.name} ${p.startDay} ${p.endDay} ${p.status}`}
        searchPlaceholder="Search name / status…"
        rowActions={(p) => p.status === "Open" ? (
          <button
            className="ghost small"
            disabled={busy}
            onClick={() => {
              if (window.confirm(`Close ${p.name}? This snapshots and LOCKS ${p.startDay} → ${p.endDay}; late sales will post to the next open day.`))
                void closePeriod(p.id).then(refresh).catch((e) => setError(String(e)));
            }}
          >
            Close period
          </button>
        ) : null}
        emptyText="No periods defined yet."
      />

      <form className="toolbar" onSubmit={submit}>
        <label>Name <input required placeholder="FY 2026/27" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
        <label>Start <input required type="date" value={form.startDay} onChange={(e) => setForm({ ...form, startDay: e.target.value })} /></label>
        <label>End <input required type="date" value={form.endDay} onChange={(e) => setForm({ ...form, endDay: e.target.value })} /></label>
        <button className="primary small" disabled={busy}>Create period</button>
      </form>
    </section>
  );
}
