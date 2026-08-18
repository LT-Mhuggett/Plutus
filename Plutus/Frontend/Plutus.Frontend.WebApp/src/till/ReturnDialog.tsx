import { useState } from "react";
import DialogX from "../DialogX.tsx";
import { fetchSaleDetail, fetchSales, findItemById, type Item, type Sale, type SaleDetail } from "../api.ts";
import { gbp, toPence } from "../money.ts";

export interface ReturnPick {
  item: Item;
  quantity: number;
  unitPricePence: number;
  unitExPricePence: number;
  originSaleId: string;
  /**
   * How the ORIGINAL sale was paid — finding Y, 2026-08-13. The refund is capped per tender against
   * this, so £2.00 cash + £2.40 card cannot go back as £4.40 on the card.
   */
  originTenders: { tenderType: string; amountPence: number }[];
}

interface Props {
  onPick: (pick: ReturnPick) => void;
  onClose: () => void;
}

const dateInput = (d: Date) => d.toISOString().slice(0, 10);

/** FE7: the catalogue barcode of the gift-card activation item (server: GiftCardSaleItem.ItemIdOne).
 *  Lines selling a card cannot be returned — see pick(). */
const GIFT_CARD_ITEM = "GIFT-CARD";

export default function ReturnDialog({ onPick, onClose }: Props) {
  const [saleId, setSaleId] = useState("");
  const [date, setDate] = useState(() => dateInput(new Date()));
  const [sales, setSales] = useState<Sale[] | null>(null);
  const [detail, setDetail] = useState<SaleDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function findByDate() {
    setBusy(true);
    setError("");
    setDetail(null);
    try {
      const d = new Date(date);
      const found = await fetchSales(d, d);
      found.sort((a, b) => b.dateOfSale.localeCompare(a.dateOfSale));
      if (found.length === 0) setError(`No sales on ${new Date(date).toLocaleDateString("en-GB")}.`);
      setSales(found.length ? found : null);
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  async function open(id: string) {
    setBusy(true);
    setError("");
    try {
      const d = await fetchSaleDetail(id.trim());
      if (d.lines.length === 0) setError("No lines found for that sale.");
      else setDetail(d);
    } catch (e) {
      setError(`Sale not found: ${e instanceof Error ? e.message : e}`);
    } finally {
      setBusy(false);
    }
  }

  async function pick(line: SaleDetail["lines"][number]) {
    if (!detail) return;
    // FE7: a gift-card ACTIVATION line must not be refunded here. Refunding it would hand the money
    // back while the card kept its balance — the shop would pay twice. Voiding the card (portal →
    // Gift cards) is the correct remedy, and it's audited.
    if (line.itemId === GIFT_CARD_ITEM) {
      setError("That line sold a gift card. Refunding it here would give the money back and leave the "
             + "card loaded — cancel the card instead (portal → Gift cards → Void).");
      return;
    }
    setBusy(true);
    try {
      const item =
        (await findItemById(line.itemId)) ??
        // item may have been renamed/removed since the sale — synthesise enough to refund
        ({ idOne: line.itemId, name: line.name, brand: "", desc: "", cost: 0, exPrice: line.unitExPrice, price: line.unitPrice, taxId: 0, catId: "" } satisfies Item);
      onPick({
        item,
        quantity: 1,
        // refund at the price actually paid, not today's catalogue price
        unitPricePence: toPence(line.unitPrice),
        unitExPricePence: toPence(line.unitExPrice),
        originSaleId: detail.id,
        // ⚠ From the SERVER record — the only thing that knows how a sale rung up on another till
        // was paid. Finding Y. `pence` is the server's exact figure; `amount` is it in pounds and
        // converting that back would be a rounding argument at a counter.
        originTenders: detail.payments.map((p) => ({
          tenderType: p.method, amountPence: p.pence,
        })),
      });
    } catch (e) {
      setError(String(e));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog wide">
        <h2>Return item</h2>
        <DialogX onClose={onClose} disabled={busy} />

        {!detail && (
          <>
            <p className="muted small">Find the original sale — by receipt sale id, or by the day it was made.</p>
            <div className="lookup-row">
              <input
                className="scan"
                placeholder="Sale id from the receipt…"
                value={saleId}
                onChange={(e) => setSaleId(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && saleId.trim() && open(saleId)}
                disabled={busy}
              />
              <button className="ghost" onClick={() => open(saleId)} disabled={busy || !saleId.trim()}>
                Look up
              </button>
            </div>
            <div className="lookup-row">
              <input type="date" value={date} max={dateInput(new Date())} onChange={(e) => setDate(e.target.value)} disabled={busy} />
              <button className="ghost" onClick={findByDate} disabled={busy}>
                Find sales on this day
              </button>
            </div>
            {error && <p className="error small">{error}</p>}

            {sales && (
              <ul className="results">
                {sales.map((s) => (
                  <li key={s.id}>
                    <button onClick={() => open(s.id)} disabled={busy}>
                      <span className="grow">
                        {new Date(s.dateOfSale).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}
                        <span className="mono muted small barcode">{s.id.slice(0, 8)}…</span>
                      </span>
                      <span>{gbp(toPence(s.total))}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}

        {detail && (
          <>
            <p className="muted small">
              {new Date(detail.dateOfSale).toLocaleString("en-GB")} · {gbp(toPence(detail.total))} ·{" "}
              {detail.payments.map((p) => p.method).join(" + ") || "—"} · served by {detail.employee ?? "—"}
            </p>
            {error && <p className="error small">{error}</p>}
            <ul className="results">
              {detail.lines.map((l, i) => (
                <li key={i}>
                  <button onClick={() => pick(l)} disabled={busy}>
                    <span className="grow">
                      {l.name} ×{l.quantity}
                      <span className="mono muted small barcode">{l.itemId}</span>
                    </span>
                    <span>{gbp(toPence(l.unitPrice))}</span>
                    <span className="muted small">return 1</span>
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}

        <div className="dialog-actions">
          {detail && (
            <button className="ghost" onClick={() => { setDetail(null); setError(""); }} disabled={busy}>
              ‹ Back
            </button>
          )}
          <button className="ghost" onClick={onClose} disabled={busy}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
