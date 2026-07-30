import { useEffect, useRef, useState } from "react";
import {
  adjustGiftCard, fetchCompanies, fetchGiftCard, fetchGiftCardLiability, fetchGiftCards, fetchLoyalty,
  gbp, generateGiftCards, linkGiftCardCustomer, unvoidGiftCard, voidGiftCard,
  type GeneratedCard, type GiftCardDetail, type GiftCardLiability, type GiftCardRow, type LoyaltyRow,
} from "./api.ts";
import Barcode39 from "./Barcode39.tsx";
import DataTable from "./DataTable.tsx";
import { ask } from "./Ask.tsx";

/**
 * FE7 gift cards. Codes are minted here (worthless until a till sells one), sold and spent at the
 * till, and printed here as either a receipt-width voucher or an A4 certificate.
 *
 * ⚠ Money treatment. Selling a gift card is NOT revenue — it takes a deposit against goods chosen
 * later, so it is a LIABILITY, and no VAT is charged on the sale (VAT lands on the goods the card is
 * eventually spent on). The activation still shows up in the day's takings, because the till really
 * did take the cash; the "outstanding" figure below is what the shop still owes in goods, and the
 * accounts must back the activation total out of turnover. See GiftCardSaleItem on the server.
 */

const statusChip = (status: string) => {
  const cls = status === "active" ? "chip ok" : status === "void" || status === "expired" ? "chip warn" : "chip";
  return <span className={cls}>{status}</span>;
};

const day = (iso: string | null) => (iso ? new Date(iso + "Z").toLocaleDateString("en-GB") : "—");

export default function GiftCardsPage() {
  const [rows, setRows] = useState<GiftCardRow[]>([]);
  const [liability, setLiability] = useState<GiftCardLiability | null>(null);
  const [status, setStatus] = useState("all");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [open, setOpen] = useState<string | null>(null);
  const [generating, setGenerating] = useState(false);
  const [printBatch, setPrintBatch] = useState<GeneratedCard[] | null>(null);

  const refresh = () => {
    setLoading(true); setError("");
    Promise.all([fetchGiftCards("", status), fetchGiftCardLiability()])
      .then(([r, l]) => { setRows(r); setLiability(l); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  };
  useEffect(refresh, [status]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <section className="panel">
      <div className="toolbar" style={{ justifyContent: "space-between" }}>
        <h2>Gift cards</h2>
        <button className="primary" onClick={() => setGenerating(true)}>Generate cards</button>
      </div>

      <div className="stat-row">
        <div className="stat">
          <span className="stat-label" title="Money customers have paid that you still owe them in goods">
            Outstanding liability
          </span>
          <span className="stat-value">{gbp(liability?.outstandingPence ?? 0)}</span>
        </div>
        <div className="stat">
          <span className="stat-label">Live cards</span>
          <span className="stat-value">{liability?.outstandingCards ?? 0}</span>
        </div>
        <div className="stat">
          <span className="stat-label" title="Generated but never sold — they hold no money">Unsold</span>
          <span className="stat-value">{liability?.unsoldCards ?? 0}</span>
        </div>
        <div className="stat">
          <span className="stat-label" title="Balances on cancelled or expired cards — no longer owed">
            Cancelled / expired
          </span>
          <span className="stat-value">{gbp(liability?.lockedPence ?? 0)}</span>
        </div>
      </div>
      <p className="muted small">
        A gift card is a <strong>liability, not turnover</strong>: no VAT is charged when a card is sold — it
        falls due on the goods the card is later spent on. The sale still appears in that day's takings
        (the till took the money), so the accounts should treat “sold” as deferred income.
      </p>

      <div className="toolbar">
        <label>
          Show{" "}
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="all">All cards</option>
            <option value="active">Active (has a balance)</option>
            <option value="unsold">Unsold</option>
            <option value="spent">Spent</option>
            <option value="expired">Expired</option>
            <option value="void">Cancelled</option>
          </select>
        </label>
      </div>

      {error && <p className="error">{error}</p>}
      {notice && <p className="callout small">{notice}</p>}

      {loading ? <p className="muted">Loading…</p> : (
        <DataTable<GiftCardRow>
          columns={[
            { key: "pretty", label: "Code", render: (c) => <span className="mono">{c.pretty}</span> },
            { key: "status", label: "Status", render: (c) => statusChip(c.status) },
            { key: "balancePence", label: "Balance", render: (c) => gbp(c.balancePence) },
            { key: "customerName", label: "Customer", render: (c) => c.customerName ?? <span className="muted">—</span> },
            { key: "batch", label: "Batch", render: (c) => c.batch ?? <span className="muted">—</span> },
            { key: "issuedAtUtc", label: "Sold", render: (c) => day(c.issuedAtUtc) },
            { key: "expiresAtUtc", label: "Expires", render: (c) => c.expiresAtUtc ? day(c.expiresAtUtc) : <span className="muted">never</span> },
          ]}
          rows={rows} getKey={(c) => c.code} initialSortKey="createdAtUtc" initialSortDir="desc"
          search={(c) => `${c.pretty} ${c.code} ${c.batch ?? ""} ${c.customerName ?? ""} ${c.status}`}
          searchPlaceholder="Search code / batch / customer…"
          rowActions={(c) => <button className="ghost small" onClick={() => setOpen(c.code)}>Open</button>}
          emptyText="No gift cards yet — generate a batch to get started."
        />
      )}

      {generating && (
        <GenerateDialog
          onClose={() => setGenerating(false)}
          onDone={(cards) => {
            setGenerating(false);
            setPrintBatch(cards);
            setNotice(`${cards.length} card(s) generated. They hold no money until a till sells them.`);
            refresh();
          }}
        />
      )}
      {printBatch && <BatchPrintDialog cards={printBatch} onClose={() => setPrintBatch(null)} />}
      {open && (
        <CardDialog
          code={open}
          onClose={() => { setOpen(null); refresh(); }}
          onNotice={setNotice}
        />
      )}
    </section>
  );
}

/** Mint a print run. Nothing here moves money — the codes are worthless until sold. */
function GenerateDialog({ onClose, onDone }: { onClose: () => void; onDone: (cards: GeneratedCard[]) => void }) {
  const [count, setCount] = useState("20");
  const [batch, setBatch] = useState("");
  const [expires, setExpires] = useState("");   // blank = never
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      const cards = await generateGiftCards(
        parseInt(count, 10) || 0,
        expires.trim() === "" ? null : parseInt(expires, 10),
        batch.trim());
      onDone(cards);
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <h3>Generate gift cards</h3>
        <p className="muted small">
          Creates unique codes to print. They are <strong>worthless until a till sells one</strong>, so it's
          safe to make more than you need.
        </p>
        <div className="form-grid">
          <label>How many <input inputMode="numeric" value={count} onChange={(e) => setCount(e.target.value)} disabled={busy} /></label>
          <label>Batch name (optional)
            <input value={batch} onChange={(e) => setBatch(e.target.value)} maxLength={60} disabled={busy} placeholder="e.g. Christmas 2026" />
          </label>
          <label>Expires after (months)
            <input inputMode="numeric" value={expires} onChange={(e) => setExpires(e.target.value)} disabled={busy} placeholder="blank = never" />
          </label>
        </div>
        <p className="muted small">
          Expiry runs from today, not from the day the card is sold — leave it blank unless you have a
          reason, since an expired balance is money a customer paid you and can no longer spend.
        </p>
        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="primary" disabled={busy}>{busy ? "Generating…" : "Generate"}</button>
        </div>
      </form>
    </div>
  );
}

/**
 * FE7.4 print, straight after a generate: a sheet of voucher tiles, one per card, each with the code
 * as text AND as a Code 39 barcode so the till can scan it. Browser print — @media print in
 * portal.css shows only .gc-print.
 */
function BatchPrintDialog({ cards, onClose }: { cards: GeneratedCard[]; onClose: () => void }) {
  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide">
        <h3 className="no-print">{cards.length} card(s) ready to print</h3>
        <p className="muted small no-print">
          Print, then keep the sheet behind the counter. A card only becomes spendable when a till sells
          it, so an unsold sheet is not money.
        </p>
        <div className="gc-print">
          {cards.map((c) => (
            <div className="gc-tile" key={c.code}>
              <div className="gc-tile-head">GIFT CARD</div>
              <Barcode39 value={c.barcode} height={34} showText={false} />
              <div className="gc-tile-code">{c.pretty}</div>
              <div className="gc-tile-foot">
                {c.expiresAtUtc ? `Valid until ${day(c.expiresAtUtc)}` : "No expiry date"}
              </div>
            </div>
          ))}
        </div>
        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>Close</button>
          <button className="primary" onClick={() => window.print()}>Print sheet</button>
        </div>
      </div>
    </div>
  );
}

/** One card: history, void/reinstate, manual correction, customer link, and the two print formats. */
function CardDialog({ code, onClose, onNotice }:
  { code: string; onClose: () => void; onNotice: (msg: string) => void }) {
  const [card, setCard] = useState<GiftCardDetail | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [print, setPrint] = useState<"none" | "voucher" | "a4">("none");
  const [adjust, setAdjust] = useState({ amount: "", reason: "" });
  const [customers, setCustomers] = useState<LoyaltyRow[]>([]);
  const [shopName, setShopName] = useState("");

  const load = () => {
    setError("");
    fetchGiftCard(code).then(setCard).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  };
  useEffect(load, [code]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    // for the link picker and the A4 certificate's letterhead
    fetchLoyalty().then((r) => setCustomers(r.rows)).catch(() => undefined);
    fetchCompanies().then((c) => setShopName(c[0]?.name ?? "")).catch(() => undefined);
  }, []);

  const run = async (p: Promise<unknown>, done: string) => {
    setBusy(true); setError("");
    try { await p; onNotice(done); load(); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
    finally { setBusy(false); }
  };

  if (print !== "none" && card) {
    return <PrintDialog card={card} shopName={shopName} kind={print} onClose={() => setPrint("none")} />;
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog wide">
        <h3>Gift card {card ? card.pretty : code}</h3>
        {error && <p className="error small">{error}</p>}
        {!card ? <p className="muted">Loading…</p> : (
          <>
            <dl className="kv">
              <dt>Status</dt><dd>{statusChip(card.status)}</dd>
              <dt>Balance</dt><dd><strong>{gbp(card.balancePence)}</strong></dd>
              <dt>Code</dt>
              <dd>
                <span className="mono">{card.pretty}</span>
                <div><Barcode39 value={card.barcode} height={30} showText={false} /></div>
              </dd>
              <dt>Sold</dt><dd>{card.issuedAtUtc ? new Date(card.issuedAtUtc + "Z").toLocaleString("en-GB") : <span className="muted">not sold yet</span>}</dd>
              <dt>Expires</dt><dd>{card.expiresAtUtc ? day(card.expiresAtUtc) : <span className="muted">never</span>}</dd>
              <dt>Batch</dt><dd>{card.batch ?? <span className="muted">—</span>}</dd>
              <dt>Customer</dt>
              <dd>
                <select
                  value={card.customerId ?? ""}
                  disabled={busy}
                  onChange={(e) => void run(
                    linkGiftCardCustomer(card.code, e.target.value || null),
                    e.target.value ? "Card linked to the customer." : "Card unlinked.")}
                >
                  <option value="">— not linked —</option>
                  {customers.map((c) => <option key={c.id} value={c.id}>{c.name}{c.memberNo ? ` (${c.memberNo})` : ""}</option>)}
                </select>
                <span className="muted small block">
                  Linking lets you find the card again if the customer loses it.
                </span>
              </dd>
            </dl>

            <div className="toolbar">
              <button className="ghost" onClick={() => setPrint("voucher")}>Print voucher (receipt)</button>
              <button className="ghost" onClick={() => setPrint("a4")}>Print A4 certificate</button>
            </div>

            <h4>History</h4>
            <table>
              <thead><tr><th>When</th><th>What</th><th className="num">Amount</th><th>Sale</th></tr></thead>
              <tbody>
                {card.entries.map((e) => (
                  <tr key={e.id}>
                    <td className="small">{new Date(e.atUtc + "Z").toLocaleString("en-GB")}</td>
                    <td>{e.type}{e.reason ? <span className="muted small"> · {e.reason}</span> : null}</td>
                    <td className="num">{e.amountPence > 0 ? "+" : ""}{gbp(e.amountPence)}</td>
                    <td className="mono small">{e.saleId ? e.saleId.slice(0, 8) + "…" : "—"}</td>
                  </tr>
                ))}
                {card.entries.length === 0 && (
                  <tr><td colSpan={4} className="muted">Nothing yet — this card hasn't been sold.</td></tr>
                )}
              </tbody>
            </table>

            {card.issuedAtUtc && (
              <>
                <h4>Correct the balance</h4>
                <p className="muted small">
                  For a mis-keyed activation or a goodwill top-up. Use a minus sign to take money off.
                  Always recorded in the audit trail with your reason.
                </p>
                <div className="toolbar">
                  <label>Amount £ <input className="short" inputMode="decimal" value={adjust.amount}
                    onChange={(e) => setAdjust({ ...adjust, amount: e.target.value })} /></label>
                  <label>Reason <input value={adjust.reason}
                    onChange={(e) => setAdjust({ ...adjust, reason: e.target.value })} /></label>
                  <button className="primary small" disabled={busy || !adjust.amount.trim() || !adjust.reason.trim()}
                    onClick={() => {
                      const pence = Math.round(parseFloat(adjust.amount) * 100);
                      if (!Number.isFinite(pence) || pence === 0) { setError("Enter a non-zero amount."); return; }
                      void run(adjustGiftCard(card.code, pence, adjust.reason.trim()), "Balance corrected.")
                        .then(() => setAdjust({ amount: "", reason: "" }));
                    }}>
                    Apply
                  </button>
                </div>
              </>
            )}

            <h4>{card.voidedAtUtc ? "Reinstate" : "Cancel this card"}</h4>
            {card.voidedAtUtc ? (
              <div className="setting-row">
                <span className="grow muted small">
                  Cancelled {day(card.voidedAtUtc)}. Reinstating makes its {gbp(card.balancePence)} spendable again —
                  the history was never altered.
                </span>
                <button className="ghost" disabled={busy}
                  onClick={() => void run(unvoidGiftCard(card.code), "Card reinstated.")}>
                  Reinstate card
                </button>
              </div>
            ) : (
              <div className="setting-row">
                <span className="grow muted small">
                  For a lost, stolen or mis-issued card. The balance stops being spendable and drops out
                  of the outstanding liability; the history stays so the money can still be explained.
                </span>
                <button className="ghost" disabled={busy} onClick={async () => {
                  const reason = await ask.prompt({
                    title: `Cancel ${card.pretty}?`,
                    body: (
                      <p className="small">
                        It holds <strong>{gbp(card.balancePence)}</strong>, which will stop being spendable.
                        You can reinstate it later.
                      </p>
                    ),
                    label: "Reason",
                    placeholder: "e.g. reported lost by the customer",
                    confirmLabel: "Cancel card",
                  });
                  if (reason === null) return;
                  await run(voidGiftCard(card.code, reason), "Card cancelled.");
                }}>
                  Cancel card
                </button>
              </div>
            )}
          </>
        )}
        <div className="dialog-actions">
          <button className="ghost" onClick={onClose} disabled={busy}>Close</button>
        </div>
      </div>
    </div>
  );
}

/**
 * FE7.4 the two printable formats for a single card:
 *   • voucher — receipt width (80mm), what a till roll prints when someone buys a card
 *   • a4      — a gift certificate to hand over or post
 * Both carry the code as text (readable down a phone) and as a Code 39 barcode (scannable at the
 * till), because a voucher whose barcode smudges must still be usable.
 */
function PrintDialog({ card, shopName, kind, onClose }:
  { card: GiftCardDetail; shopName: string; kind: "voucher" | "a4"; onClose: () => void }) {
  const printed = useRef(false);
  const amount = gbp(card.balancePence);
  const expiry = card.expiresAtUtc ? day(card.expiresAtUtc) : null;
  const shop = shopName || "Gift card";

  // Print once on open — the same "open it and it prints" behaviour as the member card.
  useEffect(() => {
    if (printed.current) return;
    printed.current = true;
    const t = setTimeout(() => window.print(), 150);   // let the barcode paint first
    return () => clearTimeout(t);
  }, []);

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide">
        <div className={kind === "a4" ? "gc-a4" : "gc-voucher"}>
          <div className="gc-shop">{shop}</div>
          <div className="gc-title">{kind === "a4" ? "Gift Certificate" : "GIFT CARD"}</div>
          <div className="gc-amount">{amount}</div>
          <Barcode39 value={card.barcode} height={kind === "a4" ? 56 : 40} showText={false} />
          <div className="gc-code mono">{card.pretty}</div>
          {kind === "a4" && (
            <p className="gc-blurb">
              Spend this in store, all at once or a bit at a time — whatever is left stays on the card.
            </p>
          )}
          <div className="gc-terms">
            {expiry ? `Valid until ${expiry}.` : "No expiry date."}{" "}
            Redeem in store. Not exchangeable for cash. Keep this safe — it is worth its balance to
            whoever holds it.
          </div>
        </div>
        <div className="dialog-actions no-print">
          <button className="ghost" onClick={onClose}>Close</button>
          <button className="primary" onClick={() => window.print()}>Print again</button>
        </div>
      </div>
    </div>
  );
}
