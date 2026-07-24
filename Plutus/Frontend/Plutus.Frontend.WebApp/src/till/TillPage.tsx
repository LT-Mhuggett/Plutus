import { useEffect, useRef, useState } from "react";
import { fetchItems, findItemById, parkTransaction, type Item } from "../api.ts";
import { gbp, parsePence } from "../money.ts";
import { useBasket, basketTotals, lineDiscountPence, lineTotalPence, type BasketState } from "./basket.ts";
import { getPrefs } from "../prefs.ts";
import CheckoutDialog from "./CheckoutDialog.tsx";
import DiscountDialog from "./DiscountDialog.tsx";
import ReturnDialog from "./ReturnDialog.tsx";
import ParkedDialog from "./ParkedDialog.tsx";
import Receipt, { type ReceiptData } from "./Receipt.tsx";

type Dialog = "none" | "checkout" | "discount" | "return" | "parked" | "receipt";

export default function TillPage() {
  const [basket, dispatch] = useBasket();
  const [scan, setScan] = useState("");
  const [qty, setQty] = useState(1);
  const [results, setResults] = useState<Item[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [dialog, setDialog] = useState<Dialog>("none");
  const [receipt, setReceipt] = useState<ReceiptData | null>(null);
  const [printOnShow, setPrintOnShow] = useState(false);
  const prefs = getPrefs();
  // NatApp TillListOrderReversed: display order only — checkout order is unaffected
  const displayLines = prefs.newestFirst ? [...basket.lines].reverse() : basket.lines;
  const [editingKey, setEditingKey] = useState<number | null>(null);
  const [editValue, setEditValue] = useState("");
  const scanRef = useRef<HTMLInputElement>(null);

  const totals = basketTotals(basket.lines);

  // Keyboard-wedge scanners type + Enter: keep the scan input focused.
  useEffect(() => {
    if (dialog === "none" && editingKey === null) scanRef.current?.focus();
  }, [dialog, editingKey, basket.lines.length]);

  function addItem(item: Item) {
    dispatch({ type: "add", item, quantity: qty });
    setQty(1); // NatApp resets the pending quantity after each add
    setScan("");
    setResults(null);
    scanRef.current?.focus();
  }

  async function submitScan() {
    const term = scan.trim();
    if (!term || busy) return;
    setBusy(true);
    setNotice("");
    try {
      const exact = await findItemById(term);
      if (exact) {
        addItem(exact);
        return;
      }
      const found = await fetchItems(1, 8, term);
      if (found.length === 0) setNotice(`Nothing found for “${term}”`);
      setResults(found.length ? found : null);
    } catch (e) {
      setNotice(String(e));
    } finally {
      setBusy(false);
    }
  }

  function startAdjust(key: number, currentPence: number) {
    setEditingKey(key);
    setEditValue((currentPence / 100).toFixed(2));
  }

  function commitAdjust(key: number) {
    const pence = parsePence(editValue);
    if (pence !== null && pence >= 0) dispatch({ type: "adjust", key, pricePence: pence });
    setEditingKey(null);
  }

  async function saveTransaction() {
    const name = window.prompt("Name this saved transaction (e.g. customer name):");
    if (name === null) return;
    setBusy(true);
    try {
      await parkTransaction(name.trim() || "Unnamed", JSON.stringify(basket));
      dispatch({ type: "clear" });
      setNotice("Transaction saved.");
    } catch (e) {
      setNotice(String(e));
    } finally {
      setBusy(false);
    }
  }

  const lineTaxPence = (pricePence: number, exPricePence: number, quantity: number, isReturn?: boolean) =>
    (pricePence - exPricePence) * quantity * (isReturn ? -1 : 1);

  return (
    <div className="till-classic">
      {/* scan row: pending-quantity stepper + full-width scan bar (NatApp layout) */}
      <div className="scan-row">
        <div className="qty-box">
          <span className="qty-value">{qty}</span>
          <button className="step" onClick={() => setQty((q) => Math.max(1, q - 1))}>−</button>
          <button className="step" onClick={() => setQty((q) => q + 1)}>+</button>
        </div>
        <input
          ref={scanRef}
          className="scan grow"
          placeholder=""
          title="Scan a barcode or type to search"
          value={scan}
          onChange={(e) => setScan(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && submitScan()}
          disabled={busy}
        />
        {prefs.bagBarcode && (
          <button
            className="ghost"
            title={`Add carrier bag (${prefs.bagBarcode})`}
            disabled={busy}
            onClick={async () => {
              const bag = await findItemById(prefs.bagBarcode);
              if (bag) addItem(bag);
              else setNotice(`Bag barcode "${prefs.bagBarcode}" not found — check Settings.`);
            }}
          >
            Bag
          </button>
        )}
        <button className="ghost" onClick={() => setDialog("return")}>
          Return Item
        </button>
      </div>

      {notice && <p className="error small">{notice}</p>}
      {results && (
        <ul className="results scan-results">
          {results.map((i) => (
            <li key={i.idOne}>
              <button onClick={() => addItem(i)}>
                <span className="grow">{i.name}</span>
                <span>{gbp(Math.round(i.price * 100))}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* basket grid */}
      <div className="basket-grid">
        <table>
          <thead>
            <tr>
              <th className="col-qty">Quantity</th>
              <th>Name</th>
              <th className="num">Price</th>
              <th className="num col-tax">Tax</th>
              <th className="col-x" />
            </tr>
          </thead>
          <tbody>
            {displayLines.map((l) => (
              <tr key={l.key} className={l.isReturn ? "return-line" : l.adjusted ? "adjusted" : undefined}>
                <td className="qty-cell">
                  <button className="step" onClick={() => dispatch({ type: "quantity", key: l.key, delta: -1 })}>−</button>
                  <span>{l.quantity}</span>
                  <button className="step" onClick={() => dispatch({ type: "quantity", key: l.key, delta: 1 })}>+</button>
                </td>
                <td>
                  {l.isReturn && <span className="return-tag">RETURN</span>} {l.item.name}
                  <span className="mono muted small barcode"> {l.item.idOne}</span>
                  {l.discount && (
                    <div className="small discount-note">
                      {l.discount.name} −{gbp(lineDiscountPence(l))}{" "}
                      <button className="linklike small" onClick={() => dispatch({ type: "clearDiscount", key: l.key })}>
                        remove
                      </button>
                    </div>
                  )}
                </td>
                <td className="num">
                  {editingKey === l.key ? (
                    <input
                      className="price-edit"
                      autoFocus
                      value={editValue}
                      onChange={(e) => setEditValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") commitAdjust(l.key);
                        if (e.key === "Escape") setEditingKey(null);
                      }}
                      onBlur={() => commitAdjust(l.key)}
                    />
                  ) : l.isReturn ? (
                    gbp(lineTotalPence(l))
                  ) : (
                    <button className="linklike" title="Adjust price" onClick={() => startAdjust(l.key, l.pricePence)}>
                      {gbp(lineTotalPence(l))}
                      {l.adjusted && <span className="adj-mark">*</span>}
                    </button>
                  )}
                </td>
                <td className="num col-tax">{gbp(lineTaxPence(l.pricePence, l.exPricePence, l.quantity, l.isReturn))}</td>
                <td className="col-x line-tools">
                  <button className="step" title="Move up" onClick={() => dispatch({ type: "move", key: l.key, direction: -1 })}>
                    ↑
                  </button>
                  <button className="step" title="Move down" onClick={() => dispatch({ type: "move", key: l.key, direction: 1 })}>
                    ↓
                  </button>
                  <button className="ghost" title="Remove line" onClick={() => dispatch({ type: "remove", key: l.key })}>
                    ✕
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {basket.lines.length === 0 && (
          <div className="empty">
            {receipt && dialog !== "receipt" ? (
              <div className="sale-done">
                <strong>Sale complete ✓</strong>
                <span>
                  {gbp(receipt.totalPence)} taken
                  {receipt.payments.reduce((c, p) => c + p.changePence, 0) > 0
                    ? ` — change ${gbp(receipt.payments.reduce((c, p) => c + p.changePence, 0))}`
                    : ""}
                </span>
                <button className="ghost" onClick={() => setDialog("receipt")}>
                  Show receipt
                </button>
              </div>
            ) : null}
          </div>
        )}
      </div>

      {/* bottom: totals strip + action grid (NatApp layout) */}
      <div className="totals-strip">
        <span>Sale Ex. Tax</span>
        <span className="totals-val">{gbp(totals.totalExTaxPence)}</span>
        <span className="spacer" />
        <span>Sale Inc. Tax</span>
        <span className="totals-val">{gbp(totals.totalPence)}</span>
      </div>

      <div className="action-grid">
        <button className="action alter" disabled={basket.lines.length === 0} onClick={() => setDialog("discount")}>
          Alter Transaction
        </button>
        <button className="action save" disabled={basket.lines.length === 0 || busy} onClick={saveTransaction}>
          Save Transaction
        </button>
        <button className="action retrieve" onClick={() => setDialog("parked")}>
          Retrieve Transaction
        </button>
        <button className="action cancel" disabled={basket.lines.length === 0} onClick={() => dispatch({ type: "clear" })}>
          Cancel Transaction
        </button>
        <button
          className="action checkout primary"
          disabled={basket.lines.length === 0 || totals.totalPence < 0}
          onClick={() => setDialog("checkout")}
        >
          Checkout
        </button>
      </div>
      {totals.totalPence < 0 && <p className="error small">Refund-only baskets aren't supported yet — total must be ≥ £0.</p>}

      {dialog === "checkout" && (
        <CheckoutDialog
          lines={basket.lines}
          totals={totals}
          onClose={() => setDialog("none")}
          onComplete={(data) => {
            setReceipt(data);
            dispatch({ type: "clear" });
            // NatApp AskForReceipt: prompt wins over auto-print when enabled
            const p = getPrefs();
            setPrintOnShow(p.askReceipt ? window.confirm("Print receipt?") : p.autoPrintReceipt);
            setDialog("receipt");
          }}
        />
      )}
      {dialog === "discount" && (
        <DiscountDialog
          lines={basket.lines}
          onClose={() => setDialog("none")}
          onApply={(discount, keys) => {
            dispatch({ type: "applyDiscount", discount, keys });
            setDialog("none");
          }}
        />
      )}
      {dialog === "return" && (
        <ReturnDialog
          onClose={() => setDialog("none")}
          onPick={(p) => {
            dispatch({ type: "addReturn", ...p });
            setDialog("none");
          }}
        />
      )}
      {dialog === "parked" && (
        <ParkedDialog
          onClose={() => setDialog("none")}
          onLoad={(state: BasketState) => {
            dispatch({ type: "restore", state });
            setDialog("none");
          }}
        />
      )}
      {dialog === "receipt" && receipt && (
        <Receipt data={receipt} autoPrint={printOnShow} onClose={() => { setDialog("none"); setPrintOnShow(false); }} />
      )}
    </div>
  );
}
