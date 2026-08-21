import { useEffect, useState } from "react";
import {
  createLoyaltyTier, fetchLoyaltyTiers, updateLoyaltyTier,
  type LoyaltyTier, type LoyaltyTierInput,
} from "./api.ts";
import { ask } from "./Ask.tsx";
import DialogX from "./DialogX.tsx";

// FE1: the loyalty tier catalogue manager — this is where "specific tiers" are defined so a
// membership is ASSIGNED a level instead of re-typing a name + rate every time. Writes are gated
// server-side on customers.manage (Owner/Company Admin/Store Manager/Supervisor hold it).
// Deliberately no delete: a tier is DEACTIVATED, because memberships reference it and re-rating
// history must stay readable. Editing a rate applies to every member of the tier immediately.

const pct = (rate: number) => `${Math.round(rate * 1000) / 10}%`;

export default function TierManagerDialog({ onClose }: { onClose: () => void }) {
  const [tiers, setTiers] = useState<LoyaltyTier[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<LoyaltyTier | "new" | null>(null);

  const refresh = () => {
    setLoading(true); setError("");
    fetchLoyaltyTiers(true) // include inactive so retired tiers can be re-activated here
      .then(setTiers)
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, []);

  const toggleActive = async (t: LoyaltyTier) => {
    if (t.active && t.memberCount > 0 && !await ask.confirm({
      title: `Deactivate “${t.name}”?`,
      body: (
        <>
          <p className="small">
            It has <strong>{t.memberCount} member{t.memberCount === 1 ? "" : "s"}</strong>.
          </p>
          <p className="muted small">
            Deactivating only hides the tier from the pickers — existing members keep their discount,
            and you can re-activate it at any time.
          </p>
        </>
      ),
      confirmLabel: "Deactivate tier",
    })) return;
    setError("");
    try {
      await updateLoyaltyTier(t.id, {
        name: t.name, autoDiscountRate: t.autoDiscountRate,
        durationMonths: t.durationMonths, sortOrder: t.sortOrder, active: !t.active,
      });
      refresh();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <DialogX onClose={onClose} />
        <div className="toolbar" style={{ justifyContent: "space-between" }}>
          <h3>Loyalty tiers</h3>
          <button className="primary small" onClick={() => setEditing("new")}>+ Add tier</button>
        </div>
        <p className="muted small">
          Members are given one of these levels. Changing a tier's discount updates every member of
          it straight away; past sales are never re-priced.
        </p>
        {error && <p className="error small">{error}</p>}
        {loading ? <p className="muted">Loading…</p> : (
          <table>
            <thead><tr>
              <th>Tier</th><th className="num">Discount</th><th className="num">Renews after</th>
              <th className="num">Members</th><th>Status</th><th />
            </tr></thead>
            <tbody>
              {tiers.map((t) => (
                <tr key={t.id}>
                  <td>{t.name}</td>
                  <td className="num">{pct(t.autoDiscountRate)}</td>
                  <td className="num">{t.durationMonths} months</td>
                  <td className="num">{t.memberCount}</td>
                  <td>{t.active ? <span className="chip ok">active</span> : <span className="chip">inactive</span>}</td>
                  <td>
                    <button className="ghost small" onClick={() => setEditing(t)}>Edit</button>{" "}
                    <button className="ghost small" onClick={() => void toggleActive(t)}>
                      {t.active ? "Deactivate" : "Re-activate"}
                    </button>
                  </td>
                </tr>
              ))}
              {tiers.length === 0 && (
                <tr><td colSpan={6} className="muted">No tiers yet — add one (e.g. “Club”, 10%).</td></tr>
              )}
            </tbody>
          </table>
        )}
        <div className="dialog-actions"><button className="ghost" onClick={onClose}>Close</button></div>

        {editing && (
          <TierForm
            tier={editing === "new" ? null : editing}
            onClose={() => setEditing(null)}
            onDone={() => { setEditing(null); refresh(); }}
          />
        )}
      </div>
    </div>
  );
}

function TierForm({ tier, onClose, onDone }:
  { tier: LoyaltyTier | null; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(tier?.name ?? "");
  const [rate, setRate] = useState(tier ? String(Math.round(tier.autoDiscountRate * 1000) / 10) : "10");
  const [months, setMonths] = useState(String(tier?.durationMonths ?? 12));
  const [sortOrder, setSortOrder] = useState(String(tier?.sortOrder ?? 0));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const ratePct = parseFloat(rate);
    if (!(ratePct >= 0 && ratePct <= 100)) { setError("Discount must be between 0 and 100%."); return; }
    setBusy(true); setError("");
    const body: LoyaltyTierInput = {
      name: name.trim(),
      autoDiscountRate: Math.round(ratePct * 10) / 1000, // 12.5% → 0.125
      durationMonths: parseInt(months, 10) || 12,
      sortOrder: parseInt(sortOrder, 10) || 0,
    };
    try {
      if (tier) await updateLoyaltyTier(tier.id, { ...body, active: tier.active });
      else await createLoyaltyTier(body);
      onDone();
    } catch (err) {
      // 409 = the name is taken (case-insensitive)
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <DialogX onClose={onClose} disabled={busy} />
        <h3>{tier ? `Edit ${tier.name}` : "Add tier"}</h3>
        <div className="form-grid">
          <label>Name <input value={name} onChange={(e) => setName(e.target.value)} maxLength={50} required disabled={busy} placeholder="e.g. Gold" /></label>
          <label>Discount % <input inputMode="decimal" value={rate} onChange={(e) => setRate(e.target.value)} required disabled={busy} /></label>
          <label>Renews after (months) <input inputMode="numeric" value={months} onChange={(e) => setMonths(e.target.value)} disabled={busy} /></label>
          <label>Sort order <input inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} disabled={busy} /></label>
        </div>
        {tier && tier.memberCount > 0 && (
          <p className="muted small">
            {tier.memberCount} member{tier.memberCount === 1 ? "" : "s"} are on this tier — saving a new
            discount applies to all of them immediately.
          </p>
        )}
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !name.trim()}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </div>
  );
}
