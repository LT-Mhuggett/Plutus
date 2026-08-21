import { useEffect, useRef, useState } from "react";
import {
  effectivePriceFor, fetchCarrierBags, fetchDiscountRules, findItemById, getCustomer, lookupGiftCard,
  parkTransaction, searchCustomers, searchItemsOfflineAware, createCustomer, updateCustomer,
  type CarrierBag, type CustomerDetail, type CustomerSummary, type DiscountRule, type GiftCardLookup,
  type Item,
} from "../api.ts";
import { NO_MEMBER } from "./autoDiscounts.ts";
import { canAddCustomers, canManageCustomers } from "../pipeline.ts";
import { requestNewItem } from "../newItemHandoff.ts";
import { gbp, parsePence } from "../money.ts";
import { useBasket, basketTotals, lineDiscountPence, lineTotalPence, type BasketState } from "./basket.ts";
import { getPrefs } from "../prefs.ts";
import { tryCanonicalise } from "../memberNumbers.ts";
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
  // Ruling 2026-08-19 — the carrier bags the portal set, one button each. Nothing is configured on
  // the till any more; see `carrierBags.ts` for why an unreachable server means no buttons rather
  // than a guessed price.
  const [bags, setBags] = useState<CarrierBag[]>([]);
  useEffect(() => {
    let live = true;
    void fetchCarrierBags().then((rows) => { if (live) setBags(rows); });
    return () => { live = false; };
  }, []);

  /**
   * The shop's scheduled discount rules ("Wednesday Warhammer"), fetched once and cached.
   *
   * ⚠ FETCHED ONCE, NOT POLLED, and that is decision D5 again: re-fetching mid-sale could change
   * what a basket costs while the operator is looking at it. A rule created in the portal reaches
   * this till when it is next opened or reloaded — which is the same freshness the manual discount
   * list has always had, and a till tab that stays open for days already shows an update badge.
   *
   * ⚠ It cannot throw: `fetchDiscountRules` answers with the cache, or an empty list, rather than
   * failing — a shop must not lose its till because a promotions feed was unreachable.
   */
  const [rules, setRules] = useState<DiscountRule[]>([]);
  useEffect(() => {
    let live = true;
    void fetchDiscountRules().then((rows) => { if (live) setRules(rows); });
    return () => { live = false; };
  }, []);

  const prefs = getPrefs();
  // NatApp TillListOrderReversed: display order only — checkout order is unaffected
  const displayLines = prefs.newestFirst ? [...basket.lines].reverse() : basket.lines;
  const [editingKey, setEditingKey] = useState<number | null>(null);
  const [editValue, setEditValue] = useState("");
  const scanRef = useRef<HTMLInputElement>(null);

  // Customer attach (Phase 8 retrofit): drives the members' auto-discount + store-credit tender.
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);

  /**
   * How the sale(s) being returned were PAID — finding Y, 2026-08-13. Empty for an ordinary sale.
   * ⚠ Page state, not basket state: see the note where it is set.
   */
  const [refundTenders, setRefundTenders] = useState<{ tenderType: string; amountPence: number }[]>([]);
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

  /**
   * Every AUTOMATIC discount — the member's tier and any live scheduled rule — recomputed whenever
   * the basket or the customer changes, through the one shared resolver.
   *
   * ⚠⚠ DECISION D5: THE INSTANT THAT MATTERS IS THE MOMENT A LINE IS ADDED, and this effect is
   * deliberately NOT on a timer. A basket must not silently re-price under the operator's hands
   * because a rule expired at 17:00 while a customer was deciding — that is the live-data lesson
   * (D5 in till-design) applied to money instead of to a table. A basket rung up on Wednesday and
   * parked keeps its Wednesday prices when it is recalled, because nothing re-runs this on recall
   * either.
   *
   * ⚠ It re-runs on `lines.length`, not on the lines themselves: adding, removing or scanning
   * changes the count, and the resolver is idempotent, so a deeper dependency would just churn.
   * A QUANTITY change does re-price correctly — `quantity` is folded into the discount arithmetic at
   * display time, not frozen here.
   */
  useEffect(() => {
    const m = customer?.membership;
    dispatch({
      type: "autoDiscounts",
      member: m
        ? { hasMembership: true, expired: m.expired, autoDiscountRate: m.autoDiscountRate, tierName: m.tier }
        : NO_MEMBER,
      rules,
      nowMs: Date.now(),
    });
  }, [customer, basket.lines.length, rules, dispatch]);

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
    // ⚠ No clear-down call any more: the effect above re-runs with no member and recomputes every
    // automatic discount from scratch, so a detached member's tier discount comes off and a scheduled
    // rule that was being out-bid by it lands instead. One code path, so there is nothing to forget.
    setCustomer(null);
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

  /**
   * @param scannedBarcode ⚠ What the operator actually scanned, when it may not be the item's own
   *   code (multi-barcode, 2026-08-20). Recorded on the line as a SNAPSHOT; `item.idOne` stays
   *   canonical and is what pricing, merging and the sale line key on.
   */
  async function addItem(item: Item, scannedBarcode?: string) {
    // WP5.4 retrofit: sell at the effective price (store override → central → legacy), not the
    // catalogue price. effectivePriceFor falls back to the cached legacy price when offline.
    const eff = await effectivePriceFor(item);
    const priced = { ...item, price: eff.pricePence / 100, exPrice: eff.exPricePence / 100 };
    dispatch({ type: "add", item: priced, quantity: qty, scannedBarcode });
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
        // ⚠ Multi-barcode: `exact` may have been resolved from an ADDITIONAL barcode, in which case
        // its `idOne` is the canonical code and `term` is what was scanned. The reducer records the
        // difference and drops it when there is none.
        await addItem(exact, term);
        return;
      }
      const found = await searchItemsOfflineAware(term); // all matches — the list scrolls
      if (found.length === 0) {
        // ⚠⚠ WP-T2 — A BARE MEMBER NUMBER, TYPED, ATTACHES ITS MEMBER (2026-08-19).
        //
        // Matt: *"How do I get the credit though? I have people with credit. But there is no way to
        // select them?"* Typing a member number used to answer "Nothing found" — a missing `C` prefix
        // reported as a missing product.
        //
        // ⚠⚠ SAFE HERE AND NOWHERE EARLIER. `MEMBER_CARD` above is strict on purpose: it demands the
        // `C` prefix so a six-digit PRODUCT barcode can never be hijacked into a customer lookup.
        // `tryCanonicalise` is the loose one, and it runs only after BOTH the exact lookup and the
        // search have failed — so a real barcode has already been found and the collision is
        // impossible by construction. The strict test stays exactly as it is.
        //
        // ⚠ It ASKS. A six-digit code is often just a mistype, and silently attaching a stranger —
        // with their discount and their credit on offer — is worse than one extra tap. MAUI's twin
        // asks in the same words.
        const memberNo = tryCanonicalise(term);
        if (memberNo) {
          const matches = await searchCustomers(memberNo);
          if (matches.length === 1) {
            const go = await ask.confirm({
              title: "Member number?",
              body: (
                <p>
                  Nothing in the catalogue matches “{term}”, but it looks like member number{" "}
                  <strong>{memberNo}</strong> — {matches[0].name}. Attach them to this sale?
                </p>
              ),
              confirmLabel: "Attach",
            });
            if (go) {
              await attachCustomer(matches[0].id);
              setScan("");
              setNotice(`Member ${matches[0].name} attached.`);
              return;
            }
          }
        }

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
      setRefundTenders([]);   // finding Y: a fresh basket has no refund caps
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
        {/* ⚠⚠ ONE BUTTON PER BAG, cheapest first, labelled with its price — because a shop sells a
            single-use bag AND a bag for life, and "Bag" alone made the cashier remember which one the
            till was set to. Nothing here is configurable on the till: the list comes from the portal. */}
        {bags.map((bag) => (
          <button
            key={bag.idOne}
            className="ghost"
            title={`Add ${bag.name}`}
            disabled={busy}
            onClick={async () => {
              const item = await findItemById(bag.idOne);
              // ⚠ The bag is a real catalogue item, so it is added exactly like anything else —
              // taxed, discountable, refundable, on the receipt.
              if (item) addItem(item);
              // ⚠ This should not happen now the portal owns the list, but if it does, say what to
              // do rather than "check Settings" — there is nothing to check on this till any more.
              else setNotice(`“${bag.name}” is missing from the catalogue — re-save it in the portal.`);
            }}
          >
            Bag {gbp(bag.pricePence)}
          </button>
        ))}
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
            {/* ⚠ canAddCustomers, NOT canManageCustomers — adding reaches the Cashier (default 20).
                The `edit` button above stays on canManageCustomers. */}
            {canAddCustomers() && (
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
          // ⚠⚠ "＋ Customer" IS THE LABEL MATT OBJECTED TO (2026-08-19): *"+Add member didnt make
          // sense. It implied that it was to add a new member. It needs to be more obvious what that
          // button was for, which is why I suggested 'Loyalty Customer Lookup'."* The ＋ read as
          // "create", when the control's job is to FIND an existing member and attach them — creating
          // one is a fallback inside it when the search finds nothing.
          // ⚠ Same words as MAUI's button now, per the 2026-08-19 look-and-feel ruling: an operator
          // swapping tills mid-shift should not have to learn two names for one control.
          <button className="ghost small" onClick={() => setShowCust(true)}>Loyalty customer lookup</button>
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
        {/* ⚠ Matt, 2026-08-20: renamed from "Alter Transaction" on BOTH tills. That was NatApp's
            wording and it named the mechanism (a basket alteration) rather than the job — the only
            thing this button does is discount lines. The MAUI half is the `AltTransaction` resx
            VALUE; its key is unchanged because renaming a key throws in DEBUG. */}
        <button className="action alter" disabled={basket.lines.length === 0} onClick={() => setDialog("discount")}>
          Apply Discounts
        </button>
        <button className="action save" disabled={basket.lines.length === 0 || busy} onClick={saveTransaction}>
          Save Transaction
        </button>
        <button className="action retrieve" onClick={() => setDialog("parked")}>
          Retrieve Transaction
        </button>
        <button className="action cancel" disabled={basket.lines.length === 0}
          onClick={() => { dispatch({ type: "clear" }); setRefundTenders([]); }}>
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
          refundTenders={refundTenders}
          onClose={() => setDialog("none")}
          onComplete={async (data) => {
            setReceipt(data);
            dispatch({ type: "clear" });
            setRefundTenders([]);   // finding Y: a fresh basket has no refund caps
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
          // ⚠⚠ `displayLines`, NOT `basket.lines` — Matt, 2026-08-20: *"the order of the list needs to
          // match the order of the till. The till is ordered newest at the top, the discount opens
          // newest at the bottom."* This dialog read the RAW basket (insertion order) while the till
          // screen renders `displayLines`, which honours the `newestFirst` preference. With that
          // preference on, the two lists were exact mirrors of each other — so the operator ticked
          // against a reversed list to discount the item they had just scanned.
          //
          // ⚠ Ordering is presentation only: every line is identified by its `key`, so which lines get
          // discounted cannot change. What changes is whether a person can trust what they are ticking
          // — and on a money dialog that is the whole point.
          lines={displayLines}
          onClose={() => setDialog("none")}
          onApply={(discount, keys, reason, authorisedBy) => {
            // ⚠ W-P3: `authorisedBy` is the supervisor who signed for an over-ceiling discount, or
            // null. It must reach the sale, or the record says a cashier gave it alone.
            dispatch({ type: "applyDiscount", discount, keys, reason, authorisedBy });
            setDialog("none");
          }}
          onApplyAdHoc={(kind, amount, label, keys, reason, authorisedBy) => {
            // D8 — a typed figure. It carries no catalogue id, so checkout keeps it off
            // `LineMeta.discounts[]`; the money and the authority still travel.
            dispatch({ type: "applyAdHocDiscount", kind, amount, label, keys, reason, authorisedBy });
            setDialog("none");
          }}
        />
      )}
      {dialog === "return" && (
        <ReturnDialog
          onClose={() => setDialog("none")}
          onPick={(p) => {
            // ⚠ FINDING Y: the origin's tenders are kept in PAGE state, deliberately not in the
            // basket — basket state is what a PARKED basket serialises, and widening that shape is a
            // migration on every parked basket in the field for something the checkout needs for the
            // next ninety seconds.
            //
            // ⚠ POOLED across returns, the same way the server pools capacities across the origins a
            // basket returns against: two items from two sales contribute both sales' tenders.
            const { originTenders, ...line } = p;
            setRefundTenders((prev) => [...prev, ...originTenders]);
            dispatch({ type: "addReturn", ...line });
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
