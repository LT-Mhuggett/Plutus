import { useEffect, useRef, useState } from "react";
import {
  effectivePriceFor, findItemById, getCustomer, lookupGiftCard, parkTransaction, searchCustomers,
  searchItemsOfflineAware, createCustomer, updateCustomer,
  type CustomerDetail, type CustomerSummary, type GiftCardLookup, type Item,
} from "../api.ts";
import { canManageCustomers } from "../pipeline.ts";
import { requestNewItem } from "../newItemHandoff.ts";
import { gbp, parsePence } from "../money.ts";
import { useBasket, basketTotals, lineDiscountPence, lineTotalPence, type BasketState } from "./basket.ts";
import { getPrefs } from "../prefs.ts";
import { agentAvailable, openDrawer, printDocument } from "../hardware.ts";
import { receiptToDocument } from "./receiptDoc.ts";
import { ask } from "../Ask.tsx";
import CheckoutDialog from "./CheckoutDialog.tsx";
import DiscountDialog from "./DiscountDialog.tsx";
import ReturnDialog from "./ReturnDialog.tsx";
import ParkedDialog from "./ParkedDialog.tsx";
import Receipt, { type ReceiptData } from "./Receipt.tsx";

type Dialog = "none" | "checkout" | "discount" | "return" | "parked" | "receipt";

/** Search returns ALL matches (the count is always true); render at most this many rows so a
 *  one-letter search can't jank the till with tens of thousands of DOM nodes. */
const MAX_SHOWN = 500;

/** FE2: the shape of a membership-card barcode payload — "C" + 6-digit sequence + check character
 *  (see MemberNumbers on the server, which validates the check character for real). A shape test is
 *  enough here: it only decides whether to TRY a customer lookup before the item lookup. */
const MEMBER_CARD = /^C[0-9A-Z]{7}$/i;

/** FE7: the shape of a gift-card barcode payload — "G" + 12 code characters + check character.
 *  Like MEMBER_CARD this is only a routing hint; the server validates the check character and owns
 *  the balance. A product barcode that happens to match falls through to the item lookup. */
const GIFT_CARD = /^G[0-9A-Z]{13}$/i;

/** The customer attached to the persisted basket (see useBasket) — stored beside it so navigating
 *  away and back restores the whole sale, member discount included. */
const CUSTOMER_KEY = "plutus.basketCustomer";

export default function TillPage() {
  const [basket, dispatch] = useBasket();
  const [scan, setScan] = useState("");
  const [qty, setQty] = useState(1);
  const [results, setResults] = useState<Item[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  /** A scan that matched nothing — offers "Add this item" with the barcode carried over. */
  const [unknownScan, setUnknownScan] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog>("none");
  const [receipt, setReceipt] = useState<ReceiptData | null>(null);
  const [printOnShow, setPrintOnShow] = useState(false);
  // "Sale complete ✓" banner: shows on completion, fades after 3s (or on its ✕).
  const [donePhase, setDonePhase] = useState<"shown" | "fading" | "gone">("gone");
  useEffect(() => { if (receipt) setDonePhase("shown"); }, [receipt]);
  // The countdown runs ONLY while the banner is actually on screen — while the receipt
  // dialog is up the banner is hidden and the timer is parked, so dismissing the receipt
  // always gives the full 3s (then a 600ms fade, matching the CSS transition).
  useEffect(() => {
    if (!receipt || dialog === "receipt" || donePhase === "gone") return;
    const t = donePhase === "shown"
      ? setTimeout(() => setDonePhase("fading"), 3000)
      : setTimeout(() => setDonePhase("gone"), 600);
    return () => clearTimeout(t);
  }, [receipt, dialog, donePhase]);
  const prefs = getPrefs();
  // NatApp TillListOrderReversed: display order only — checkout order is unaffected
  const displayLines = prefs.newestFirst ? [...basket.lines].reverse() : basket.lines;
  const [editingKey, setEditingKey] = useState<number | null>(null);
  const [editValue, setEditValue] = useState("");
  const scanRef = useRef<HTMLInputElement>(null);

  // Customer attach (Phase 8 retrofit): drives the members' auto-discount + store-credit tender.
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  // The attached customer rides along with the persisted basket. Restoring the LINES without the
  // member would leave member-discounted lines on screen with nobody attached — the discount would
  // look unexplained, and the next scanned item wouldn't get it.
  const [savedCustomerId] = useState<string | null>(() => {
    try { return localStorage.getItem(CUSTOMER_KEY); } catch { return null; }
  });
  const [customerRestored, setCustomerRestored] = useState(false);
  const [showCust, setShowCust] = useState(false);
  const [custSearch, setCustSearch] = useState("");
  const [custResults, setCustResults] = useState<CustomerSummary[] | null>(null);
  // Loyalty usability: create/edit a customer at the till (supervisors/managers only). id=null → create.
  const [custForm, setCustForm] = useState<{ id: string | null; name: string; email: string; phone: string } | null>(null);

  const totals = basketTotals(basket.lines);

  // Re-attach the customer the persisted basket was rung up against. Runs once, and only when a
  // basket actually came back with us — a leftover id with an empty basket is just stale.
  useEffect(() => {
    if (!savedCustomerId || basket.lines.length === 0) { setCustomerRestored(true); return; }
    let cancelled = false;
    void getCustomer(savedCustomerId)
      .then((c) => { if (!cancelled) setCustomer(c); })
      .catch(() => undefined)   // deleted/unreachable → carry on without them
      .finally(() => { if (!cancelled) setCustomerRestored(true); });
    return () => { cancelled = true; };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ...and keep that pointer in step. Gated on the restore having settled, so the initial
  // customer=null doesn't wipe the id before it has been read.
  useEffect(() => {
    if (!customerRestored) return;
    try {
      if (customer) localStorage.setItem(CUSTOMER_KEY, customer.id);
      else localStorage.removeItem(CUSTOMER_KEY);
    } catch { /* private mode — the basket still works */ }
  }, [customer, customerRestored]);

  // Auto-apply the members' discount to eligible lines whenever a member is attached or a new
  // line is added (applyMemberDiscount only touches lines without a discount — no stacking).
  useEffect(() => {
    const m = customer?.membership;
    if (m && !m.expired && m.autoDiscountRate > 0)
      dispatch({ type: "applyMemberDiscount", rate: m.autoDiscountRate, name: `${m.tier} ${(m.autoDiscountRate * 100).toFixed(0)}%` });
  }, [customer, basket.lines.length, dispatch]);

  async function doCustSearch() {
    try {
      setCustResults(await searchCustomers(custSearch.trim()));
    } catch (e) {
      setNotice(String(e));
    }
  }

  async function attachCustomer(id: string) {
    try {
      setCustomer(await getCustomer(id));
      setShowCust(false);
      setCustResults(null);
      setCustSearch("");
    } catch (e) {
      setNotice(String(e));
    }
  }

  function detachCustomer() {
    setCustomer(null);
    dispatch({ type: "clearMemberDiscount" });
  }

  async function submitCustForm(e: React.FormEvent) {
    e.preventDefault();
    if (!custForm || !custForm.name.trim()) return;
    const body = { name: custForm.name.trim(), email: custForm.email.trim() || undefined, phone: custForm.phone.trim() || undefined };
    try {
      if (custForm.id) {
        await updateCustomer(custForm.id, body);
        setCustomer(await getCustomer(custForm.id)); // reflect the edit on the attached chip
      } else {
        const { id } = await createCustomer(body);
        setCustomer(await getCustomer(id)); // create then auto-attach to the sale
        setShowCust(false);
        setCustResults(null);
        setCustSearch("");
      }
      setCustForm(null);
    } catch (err) {
      setNotice(String(err));
    }
  }

  // Keyboard-wedge scanners type + Enter: keep the scan input focused.
  useEffect(() => {
    if (dialog === "none" && editingKey === null) scanRef.current?.focus();
  }, [dialog, editingKey, basket.lines.length]);

  async function addItem(item: Item) {
    // WP5.4 retrofit: sell at the effective price (store override → central → legacy), not the
    // catalogue price. effectivePriceFor falls back to the cached legacy price when offline.
    const eff = await effectivePriceFor(item);
    const priced = { ...item, price: eff.pricePence / 100, exPrice: eff.exPricePence / 100 };
    dispatch({ type: "add", item: priced, quantity: qty });
    setQty(1); // NatApp resets the pending quantity after each add
    setScan("");
    setResults(null);
    scanRef.current?.focus();
  }

  /**
   * FE7: a scanned/typed gift card. Returns true when the scan was handled as a card.
   *
   * Selling one adds a basket line priced at the amount being loaded (zero VAT — the activation item
   * sits on a zero-rate band), and the card is only actually LOADED when the sale completes, inside
   * checkout(). So an abandoned basket leaves the card worthless, which is the safe way round.
   *
   * ⚠ Online only: the server owns the balance and there is no offline queue for cards. Ordinary
   * sales keep working offline.
   */
  async function handleGiftCardScan(term: string): Promise<boolean> {
    if (!navigator.onLine) {
      setNotice("Gift cards need a connection — this till is offline.");
      return true;   // it WAS a card; don't fall through and hunt for a product with that barcode
    }

    let found: GiftCardLookup;
    try {
      found = await lookupGiftCard(term);
    } catch {
      return false;  // not a card for this shop → let the item lookup have it
    }

    if (found.status === "active") {
      setNotice(`Gift card ${found.pretty} holds ${gbp(found.balancePence)} — take it as payment at checkout.`);
      return true;
    }
    if (found.status !== "unsold") {
      setNotice(
        found.status === "spent" ? `Gift card ${found.pretty} has been fully spent.`
        : found.status === "expired" ? `Gift card ${found.pretty} has expired.`
        : `Gift card ${found.pretty} has been cancelled.`);
      return true;
    }

    // unsold → sell it. The amount is free-form because a card is worth what the customer pays.
    // The VAT copy follows the tenant's declared treatment — this is a legal statement to the
    // customer, so it must match what the sale actually posts.
    const single = found.vatTreatment === "single";
    const typed = await ask.prompt({
      title: `Sell gift card ${found.pretty}`,
      body: (
        <>
          <p className="small">How much is being loaded onto this card?</p>
          <p className="muted small">
            {single
              ? "VAT is charged on this sale (single-rate store) — spending the card later doesn't add VAT again."
              : "No VAT is charged on the card — VAT applies to the goods it's spent on later."}{" "}
            The card becomes spendable once this sale is completed.
          </p>
        </>
      ),
      label: "Amount (£)",
      placeholder: "e.g. 20.00",
      confirmLabel: "Add to sale",
    });
    if (typed === null) return true;

    const pence = parsePence(typed);
    if (pence === null || pence <= 0) {
      setNotice("That isn't a valid amount — the card was not added.");
      return true;
    }

    // Single-purpose: the amount INCLUDES VAT declared now, so ex = amount/1.2.
    // Multi-purpose: ex == amount → the line's VAT is zero.
    const exPence = single ? Math.round(pence / 1.2) : pence;

    // A synthetic catalogue item: itemIdOne comes from the SERVER (the provisioned row), so the
    // sale line resolves to a real Item and the legacy projection's FK holds.
    dispatch({
      type: "addGiftCard",
      code: found.code,
      amountPence: pence,
      exAmountPence: exPence,
      item: {
        idOne: found.itemIdOne,
        name: `Gift card ${found.pretty}`,
        brand: "-",
        desc: "",
        cost: 0,
        exPrice: exPence / 100,
        price: pence / 100,
        taxId: 0,
        catId: "",
      },
    });
    setNotice(`Gift card ${found.pretty} added at ${gbp(pence)} — it activates when the sale completes.`);
    return true;
  }

  async function submitScan() {
    const term = scan.trim();
    if (!term || busy) return;
    setBusy(true);
    setNotice("");
    setUnknownScan(null);
    try {
      // FE2: a scanned LOYALTY CARD attaches its customer instead of adding an item. Only the
      // "C"-prefixed payload is treated this way, so product barcodes are never hijacked — and if
      // no customer matches (a real SKU that happens to start with C) it falls through to the
      // normal item lookup below. The server does the authoritative check-character validation.
      if (MEMBER_CARD.test(term.replace(/[\s-]/g, ""))) {
        const matches = await searchCustomers(term);
        if (matches.length === 1) {
          await attachCustomer(matches[0].id);
          setScan("");
          setNotice(`Member ${matches[0].name} attached.`);
          return;
        }
      }

      // FE7: a scanned GIFT CARD either gets SOLD (an unsold code → ask what to load it with) or
      // reports its balance (an active one → the cashier takes it at checkout, not here). Same
      // fall-through rule as the member card: no match, and it's treated as a product barcode.
      if (GIFT_CARD.test(term.replace(/[\s-]/g, ""))) {
        if (await handleGiftCardScan(term)) { setScan(""); return; }
      }

      const exact = await findItemById(term);
      if (exact) {
        await addItem(exact);
        return;
      }
      const found = await searchItemsOfflineAware(term); // all matches — the list scrolls
      if (found.length === 0) {
        setNotice(`Nothing found for “${term}”`);
        // A scanned barcode that matches nothing is usually new stock, so offer to create it
        // rather than making the cashier retype the code in Inventory (see newItemHandoff.ts).
        setUnknownScan(term);
      }
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
    const name = await ask.prompt({
      title: "Save this transaction",
      body: <p className="muted small">Give it a name so you can find it again from “Retrieve Transaction”.</p>,
      label: "Name",
      placeholder: "e.g. the customer's name",
      confirmLabel: "Save transaction",
      required: false,
    });
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

      {/* customer bar (Phase 8 retrofit): attach a customer for member discount + store credit */}
      <div className="customer-bar">
        {custForm ? (
          <form className="customer-search" onSubmit={submitCustForm}>
            <input className="cust-input" placeholder="name" value={custForm.name} autoFocus required
              onChange={(e) => setCustForm({ ...custForm, name: e.target.value })} />
            <input className="cust-input" placeholder="email" value={custForm.email}
              onChange={(e) => setCustForm({ ...custForm, email: e.target.value })} />
            <input className="cust-input" placeholder="phone" value={custForm.phone}
              onChange={(e) => setCustForm({ ...custForm, phone: e.target.value })} />
            <button className="primary small" disabled={!custForm.name.trim()}>{custForm.id ? "Save" : "Create"}</button>
            <button type="button" className="linklike small" onClick={() => setCustForm(null)}>cancel</button>
          </form>
        ) : customer ? (
          <span className="customer-chip">
            👤 {customer.name}
            {customer.memberNo && <> · <span className="mono">{customer.memberNo}</span></>}
            {customer.creditBalancePence > 0 && <> · {gbp(customer.creditBalancePence)} credit</>}
            {customer.membership && !customer.membership.expired && (
              <> · {customer.membership.tier} {(customer.membership.autoDiscountRate * 100).toFixed(0)}%</>
            )}{" "}
            {canManageCustomers() && (
              <button className="linklike small"
                onClick={() => setCustForm({ id: customer.id, name: customer.name, email: customer.email ?? "", phone: customer.phone ?? "" })}>
                edit
              </button>
            )}{" "}
            <button className="linklike small" onClick={detachCustomer}>remove</button>
          </span>
        ) : showCust ? (
          <span className="customer-search">
            <input
              className="cust-input"
              placeholder="scan card, or name / email / phone / member no"
              value={custSearch}
              autoFocus
              onChange={(e) => setCustSearch(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && doCustSearch()}
            />
            <button className="ghost small" onClick={doCustSearch}>Find</button>
            {canManageCustomers() && (
              <button className="ghost small"
                onClick={() => setCustForm({ id: null, name: custSearch.trim(), email: "", phone: "" })}>
                ＋ New
              </button>
            )}
            <button className="linklike small" onClick={() => { setShowCust(false); setCustResults(null); }}>cancel</button>
            {custResults && (
              <ul className="results cust-results">
                {custResults.map((c) => (
                  <li key={c.id}>
                    <button onClick={() => attachCustomer(c.id)}>
                      <span className="grow">{c.name}</span>
                      {c.memberNo && <span className="mono small">{c.memberNo}</span>}
                      <span className="muted small">{c.email ?? c.phone ?? ""}</span>
                    </button>
                  </li>
                ))}
                {custResults.length === 0 && <li className="muted small no-cust">No customers found.</li>}
              </ul>
            )}
          </span>
        ) : (
          <button className="ghost small" onClick={() => setShowCust(true)}>＋ Customer</button>
        )}
      </div>

      {notice && <p className="error small">{notice}</p>}
      {unknownScan && (
        <p className="small">
          <button
            className="ghost small"
            onClick={() => { requestNewItem(unknownScan); setUnknownScan(null); setScan(""); setNotice(""); }}
          >
            ＋ Add this item
          </button>{" "}
          <span className="muted">
            Creates a new catalogue item with barcode <span className="mono">{unknownScan}</span>.
          </span>
        </p>
      )}
      {results && (
        <>
          <p className="muted small scan-count">
            {results.length} match{results.length === 1 ? "" : "es"}
            {results.length > MAX_SHOWN && ` — showing the first ${MAX_SHOWN}, keep typing to narrow`}
            <button className="linklike small" onClick={() => { setResults(null); scanRef.current?.focus(); }}>clear</button>
          </p>
          <ul className="results scan-results">
            {results.slice(0, MAX_SHOWN).map((i) => (
              <li key={i.idOne}>
                <button onClick={() => addItem(i)}>
                  <span className="grow">{i.name}</span>
                  <span>{gbp(Math.round(i.price * 100))}</span>
                </button>
              </li>
            ))}
          </ul>
        </>
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
                  {/* FE7: a gift-card line is one specific CODE, so quantity is fixed at 1 — "2 ×" would
                      charge twice and load once. Sell a second card by scanning a second card. */}
                  {l.giftCardCode ? <span>1</span> : (
                    <>
                      <button className="step" onClick={() => dispatch({ type: "quantity", key: l.key, delta: -1 })}>−</button>
                      <span>{l.quantity}</span>
                      <button className="step" onClick={() => dispatch({ type: "quantity", key: l.key, delta: 1 })}>+</button>
                    </>
                  )}
                </td>
                <td>
                  {l.isReturn && <span className="return-tag">RETURN</span>} {l.item.name}
                  <span className="mono muted small barcode"> {l.item.idOne}</span>
                  {/* FE7: say plainly that this line takes money for a card rather than selling goods,
                      and that the card isn't live until the sale completes. */}
                  {l.giftCardCode && (
                    <div className="small discount-note">
                      🎁 activates on completion · {l.exPricePence === l.pricePence ? "no VAT (due when spent)" : "VAT charged now"}
                    </div>
                  )}
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
            {receipt && dialog !== "receipt" && donePhase !== "gone" ? (
              <div className={`sale-done${donePhase === "fading" ? " fade-out" : ""}`}>
                <button className="ghost small sale-done-x" title="Dismiss" onClick={() => setDonePhase("gone")}>✕</button>
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
          disabled={basket.lines.length === 0}
          onClick={() => setDialog("checkout")}
        >
          {totals.totalPence < 0 ? "Refund" : "Checkout"}
        </button>
      </div>
      {totals.totalPence < 0 && (
        <p className="small discount-note">
          Refund — {gbp(-totals.totalPence)} goes back to the customer. Choose how on the next screen.
        </p>
      )}

      {dialog === "checkout" && (
        <CheckoutDialog
          lines={basket.lines}
          totals={totals}
          customer={customer}
          onClose={() => setDialog("none")}
          onComplete={async (data) => {
            setReceipt(data);
            dispatch({ type: "clear" });
            setCustomer(null); // fresh sale starts with no customer attached
            // ⚠ The sale is RECORDED by this point. Close the checkout dialog before any of the
            // slow work below (ask, agent probe, print) — leaving it mounted showed it recomputing
            // against the now-empty basket ("Checkout — £0.00", change owed on the whole total),
            // and it sat ON TOP of the "Print receipt?" question, so the till looked frozen and
            // needed F5 (reported 2026-08-07).
            setDialog("none");

            // FE3.3: kick the drawer on a cash sale. Fire-and-forget and silent when there is no
            // agent — the drawer is opened by hand today and must keep working that way.
            const tookCash = data.payments.some((p) => p.name.toLowerCase().includes("cash"));
            if (tookCash) void openDrawer();

            // NatApp AskForReceipt: the ask wins over auto-print when enabled.
            // The sale is ALREADY committed at this point, so awaiting the operator's answer can't
            // affect it — the basket is cleared first and the receipt dialog opens either way.
            const p = getPrefs();
            const wantsReceipt = p.askReceipt
              ? await ask.confirm({ title: "Print receipt?", confirmLabel: "Print", cancelLabel: "No receipt" })
              : p.autoPrintReceipt;

            // FE3.3: with a healthy agent the receipt prints SILENTLY on the till printer — no
            // browser print dialog. Any failure (no agent, printer off, wrong token) falls straight
            // through to the existing browser/PDF receipt, so a sale is never held up by hardware.
            let printedOnPaper = false;
            if (wantsReceipt) {
              const agent = await agentAvailable();
              if (agent) {
                printedOnPaper = await printDocument(
                  receiptToDocument(data, agent.columns ?? 42, false));
              }
            }
            setPrintOnShow(wantsReceipt && !printedOnPaper);
            setDialog("receipt");
            if (printedOnPaper) setNotice("Receipt printed.");
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
