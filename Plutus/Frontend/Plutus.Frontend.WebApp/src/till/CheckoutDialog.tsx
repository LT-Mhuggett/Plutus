import { useEffect, useMemo, useState } from "react";
import DialogX from "../DialogX.tsx";
import {
  checkout, fetchActiveGateway, fetchPayMethods, lookupGiftCard, tenderTypeFor,
  type ActiveGateway, type CustomerDetail, type GiftCardLookup, type PayMethod,
} from "../api.ts";
import { gbp } from "../money.ts";
import {
  assess, capacityFor, parseAmounts, refundCapacities, refundSplitRefusal, refusalReason, restFor,
} from "./tendering.ts";
import type { BasketLine } from "./basket.ts";
import { cachedSurcharge, cardIsTendered, rememberSurcharge, surchargeLine } from "./surcharge.ts";
import type { ReceiptData } from "./Receipt.tsx";

// Synthetic pay-method id for the store-credit tender (Phase 8 retrofit). Real PayMethod ids
// are positive; −1 never collides. Maps to a Credit tender server-side (name contains "credit").
const CREDIT_PAYID = -1;
// FE7: same trick for the gift-card tender. Synthetic (not a real PayMethod row) on purpose — a real
// row would put a free-typed "Gift card" amount on every checkout screen, letting a cashier tender
// money against no card at all. This row only appears once a card has been scanned and checked.
const GIFTCARD_PAYID = -2;
// W-P7: the key on the card-surcharge line. Negative because basket keys start at 1 and count up, so
// it can never collide with a real line — and the fee is built here, never in basket state, so there
// is no `nextKey` to draw from.
const FEE_LINE_KEY = -1;

interface Props {
  lines: BasketLine[];
  totals: { totalPence: number; totalExTaxPence: number };
  customer?: CustomerDetail | null;
  /**
   * How the sale being returned was PAID — finding Y, 2026-08-13. Empty for an ordinary sale, and
   * empty when the split is unknown, in which case nothing is capped here and the server gate is the
   * only thing standing between an operator and an over-refund.
   */
  refundTenders?: { tenderType: string; amountPence: number }[];
  onClose: () => void;
  onComplete: (receipt: ReceiptData) => void;
}

export default function CheckoutDialog(
  { lines, totals, customer, refundTenders, onClose, onComplete }: Props,
) {
  const [methods, setMethods] = useState<PayMethod[] | null>(null);
  const [amounts, setAmounts] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // 17.2: the tenant's card-payment setup — shown as a hint on the tender screen. Offline or
  // unfetchable → assume standalone (today's flow); never blocks checkout.
  const [gateway, setGateway] = useState<ActiveGateway | null>(null);
  // W-P7: the surcharge setting starts at LAST-KNOWN-GOOD and is refreshed from the portal. A fee
  // that vanished offline and reappeared online would make two identical baskets total differently
  // an hour apart, and the operator would wear the argument (see surcharge.ts).
  const [surcharge, setSurcharge] = useState(cachedSurcharge);
  useEffect(() => {
    fetchActiveGateway()
      .then((g) => {
        setGateway(g);
        const bp = g.surchargeBp ?? 0;
        const flat = g.surchargeFlatPence ?? 0;
        setSurcharge({ bp, flatPence: flat });
        rememberSurcharge(bp, flat);
      })
      .catch(() => undefined);
  }, []);

  // FE7: a gift card being SPENT on this sale. Scanned/typed here, checked against the server (which
  // owns the balance), then offered as a tender capped at what the card actually holds.
  const [cardInput, setCardInput] = useState("");
  const [card, setCard] = useState<GiftCardLookup | null>(null);
  const [cardBusy, setCardBusy] = useState(false);
  const [cardError, setCardError] = useState("");
  // ⚠ The gift-card box is CLOSED until asked for (2026-08-19). Matt: *"can the Giftcard go below
  // and say Pay with Gift Card button, which then pops the box to scan. There is no point showing it
  // all, unless you have a card."* Most sales involve no gift card, and a permanently-open scan box at
  // the top of the checkout is the first thing an operator reads on every single sale.
  const [cardOpen, setCardOpen] = useState(false);
  // Selling a card and paying with one in the same sale would launder an expiring balance into a
  // fresh card, so the tender is not offered when the basket contains an activation.
  const sellingACard = lines.some((l) => l.giftCardCode);

  useEffect(() => {
    fetchPayMethods()
      .then((all) => {
        // ⚠⚠ THE LEGACY TABLE CONTAINS A ROW LITERALLY NAMED "Credit", SEEDED IN 2019, AND IT MUST
        // NEVER BE OFFERED (2026-08-19). Matt found it: *"I can add store credit in the checkout box,
        // but then nothing? How am I supposed to assign that to a specific user?"*
        //
        // He could not, and that is the bug. The server maps a tender to its type BY NAME
        // (`Tenders.FromMethodName` — anything containing "credit" is the STORE-CREDIT byte), while
        // `creditRedeemPence` is only sent when a customer is attached. So money typed into that row
        // was recorded as store credit and drawn from NOBODY's balance: the takings show credit taken,
        // no account moved, and the books do not reconcile. It is also the same tender byte finding Y's
        // cap and the 2026-08-19 accumulation fix operate on.
        //
        // ⚠ The synthetic row below is the ONLY legitimate source of a store-credit tender, and it
        // appears only with a customer, a positive balance and a live connection. MAUI never had this
        // hole because it builds its tender list from `TillTenders.Offered` and ignores this table —
        // which is why the same basket offered Cash and Card there and four methods here.
        //
        // ⚠ Filtered by NAME rather than by id 4, because the name is what the server maps on: a
        // renamed or re-seeded row would keep the hazard under a different id.
        const real = all.filter((m) => !/credit/i.test(m.name));

        // Store credit is offered only with a customer attached, a positive balance, AND
        // online (the redeem needs a live balance check — it can't queue offline).
        const eligible = customer && customer.creditBalancePence > 0 && navigator.onLine;
        setMethods(
          eligible
            ? [...real, { id: CREDIT_PAYID, name: "Store credit", charge: 0, minimumCharge: 0, isChangeable: false, isCashBackable: false }]
            : real,
        );
      })
      .catch((e) => setError(String(e)));
  }, [customer]);

  async function checkCard() {
    const typed = cardInput.trim();
    if (!typed) return;
    setCardBusy(true); setCardError("");
    try {
      const found = await lookupGiftCard(typed);
      if (found.status !== "active" || found.balancePence <= 0) {
        // say WHICH problem — "not active" sends a cashier hunting for the wrong thing
        setCardError(
          found.status === "unsold" ? "That card hasn't been sold yet, so it holds no money."
          : found.status === "spent" ? "That card has been fully spent."
          : found.status === "expired" ? "That card has expired."
          : found.status === "void" ? "That card has been cancelled."
          : "That card can't be used.");
        setCard(null);
      } else {
        setCard(found);
        setCardInput("");
      }
    } catch (e) {
      setCardError(String(e instanceof Error ? e.message : e));
      setCard(null);
    } finally {
      setCardBusy(false);
    }
  }

  // ⚠ The arithmetic lives in ./tendering.ts so it can be TESTED. It is the same code, moved: this
  // is the web half of a C2 twin whose .NET side (TenderLoop) has 19 mutation-checked tests and
  // whose TypeScript side had none, because this project had no test runner at all. Two tills that
  // disagree by a penny on one basket disagree on every VAT return afterwards.
  const parsed = useMemo(() => parseAmounts(amounts), [amounts]);

  // ⚠⚠ EVERY HOOK MUST BE ABOVE THE EARLY RETURN BELOW, AND THIS ONE WAS NOT — the defect that
  // stopped the web till taking money. Matt, 2026-08-18: *"When I try to checkout on the webtill, I
  // get… Minified React error #310"* = "Rendered more hooks than during the previous render."
  //
  // The dialog mounts with `methods === null`, returns early, and renders N hooks. `fetchPayMethods`
  // resolves, it re-renders, sails past the early return and reaches hook N+1 — and React throws
  // rather than render. So **checkout crashed on the second render, every single time**: not an edge
  // case, the ONLY path. It shipped in the deployed 1.10.0 and survived 1.12.0 because the web till's
  // hand-run has never been run and no automated test in this project mounts a component.
  //
  // ⚠ It arrived with finding Y (`e6c6b65d`), which added `caps` — correct arithmetic, placed six
  // lines the wrong side of a guard clause. ⚠ `refundTenders` is a PROP, so hoisting is behaviour-free.
  const caps = useMemo(() => refundCapacities(refundTenders), [refundTenders]);

  // ⚠ NOTHING BELOW THIS LINE MAY CALL A HOOK. If you need one, put it above.
  if (!methods) {
    return (
      <div className="overlay">
        <div className="dialog">{error ? <p className="error">{error}</p> : <p className="muted">Loading payment methods…</p>}</div>
      </div>
    );
  }

  // FE7: the gift-card row joins the tender list only once a card has been checked, so a cashier
  // can't tender gift-card money without one. Not changeable and not cash-backable: a card buys
  // goods, it never pays out cash.
  const tenders: PayMethod[] = card
    ? [...methods, { id: GIFTCARD_PAYID, name: `Gift card ${card.pretty}`, charge: 0, minimumCharge: 0, isChangeable: false, isCashBackable: false }]
    : methods;

  // ── W-P7: the card surcharge ────────────────────────────────────────────────
  //
  // ⚠⚠ THE FEE IS A REAL SALE LINE and it RAISES WHAT IS OWED. `TenderLoop` does exactly this in
  // .NET (`total += surcharge; outstanding += surcharge`), and it must: the ingest guard requires the
  // header gross to equal the sum of the line grosses, so a fee on the sale that the operator was
  // never asked to collect is a short tender the server rejects.
  //
  // ⚠ CHOOSING CARD IS WHAT CREATES IT — the twin of MAUI adding the line when a card method is
  // picked. Here "picked" is a positive amount in a card row.
  //
  // ⚠ NO CIRCULARITY: the fee is a function of the GOODS, not of the amounts, so covering the new
  // remainder cannot move the fee again. Filling the card row settles in one step.
  //
  // ⚠ ONCE per sale — `surchargeLine` refuses a basket that already has the line, so a split across
  // two cards cannot be charged the flat half twice.
  const cardTendered = parsed.valid && cardIsTendered(
    tenders.map((m) => ({ tenderType: tenderTypeFor(m.name), pence: parsed.perMethod.get(m.id) ?? 0 })),
  );

  let fee: BasketLine | null = null;
  let feeRefusal = "";
  if (cardTendered && (surcharge.bp > 0 || surcharge.flatPence > 0)) {
    try {
      fee = surchargeLine(lines, surcharge.bp, surcharge.flatPence, FEE_LINE_KEY);
    } catch (e) {
      // ⚠⚠ FAIL CLOSED ON MONEY. The shared rule throws on a setting or a basket it cannot price
      // honestly (a negative rate, an ex total outside [0, gross]). Completing the sale without the
      // fee would take the wrong money silently; blanking the till would lose the basket. So the
      // sale is REFUSED, with the reason on screen, and the operator can take cash instead.
      feeRefusal = `This basket's card fee can't be priced (${e instanceof Error ? e.message : String(e)}). `
        + "Take another tender, or ask for the card surcharge setting to be checked in the portal.";
    }
  }

  // ⚠ The fee rides on the wire and the receipt as an ordinary line — nothing downstream needs to
  // know it is a fee, which is the whole point of pricing it as a pair.
  const wireLines = fee ? [...lines, fee] : lines;
  const feePence = fee?.pricePence ?? 0;
  const effective = fee
    ? {
        totalPence: totals.totalPence + fee.pricePence,
        totalExTaxPence: totals.totalExTaxPence + fee.exPricePence,
      }
    : totals;

  // A basket of returns worth more than anything bought is a REFUND: the same sale, with every
  // figure negative (the T1.3 invariants are sign-agnostic — see SalesV2Tests). The operator
  // types the amount to hand back as a positive number; it goes on the wire negative.
  const settled = assess(effective.totalPence, parsed, tenders);
  const { refunding, owed, paid, remaining, overpay, overRefund, changeOk } = settled;

  // ⚠⚠ FINDING Y (Matt, 2026-08-13): THE MONEY GOES BACK THE WAY IT CAME, IN THE AMOUNTS IT CAME.
  // The web till had no restriction at all — every method was offered for every refund and none was
  // capped — so a £2.00 cash + £2.40 card sale could be refunded £4.40 to the card: the card credited
  // £2.40 it never took, the £2.00 left in the drawer, and no report anywhere disagreeing.
  //
  // ⚠ Binding default 19, from Matt: *"If the card machine is down, we cannot refund cards."* There is
  // no cash exception and no supervisor override.
  //
  // ⚠ `caps` is COMPUTED ABOVE THE EARLY RETURN now — see the note there. It was here, which is one
  // hook below a guard clause, which is React error #310 on every second render.

  // Refunding ONTO store credit or a gift card would be a ledger write, not a tender — out of
  // scope, so a refund offers only the real money methods.
  //
  // ⚠ And on a refund, only the methods the ORIGINAL sale actually used. A method with nothing left to
  // give back is not shown at all: an operator should never be offered a button that can only refuse.
  const rows = refunding
    ? tenders.filter((m) => m.id !== CREDIT_PAYID && m.id !== GIFTCARD_PAYID)
        .filter((m) => {
          const cap = capacityFor(caps, tenderTypeFor(m.name));
          return cap === null || cap > 0;
        })
    : tenders;
  const creditRedeem = parsed.valid ? parsed.perMethod.get(CREDIT_PAYID) ?? 0 : 0;
  const creditOverBalance = creditRedeem > (customer?.creditBalancePence ?? 0);
  const giftRedeem = parsed.valid ? parsed.perMethod.get(GIFTCARD_PAYID) ?? 0 : 0;
  const giftOverBalance = giftRedeem > (card?.balancePence ?? 0);

  // ⚠⚠ FINDING Y, ENFORCED — not merely pre-filled. The operator can always type over a default, so
  // the cap has to gate Complete or it is decoration. Empty string = allowed.
  const tenderRefusal = refunding && parsed.valid
    ? refundSplitRefusal(caps, rows.map((m) => ({
        tenderType: tenderTypeFor(m.name),
        pence: parsed.perMethod.get(m.id) ?? 0,
      })))
    : "";

  const canComplete = parsed.valid && remaining === 0 && changeOk && !overRefund
    && !creditOverBalance && !giftOverBalance && !tenderRefusal && !feeRefusal && !busy;

  function quickFill(id: number) {
    // "rest" = make THIS row cover everything the others don't, so it must ignore what this row
    // already holds. Using the bare remainder made "rest" toggle 0.00 ↔ full whenever the row was
    // already filled (reported 2026-08-07), and left an overpaid row untouched.
    const own = parsed.valid ? parsed.perMethod.get(id) ?? 0 : 0;
    // FE7: "rest" on the gift-card row is capped at what the card holds — the common case is a card
    // that doesn't cover the whole basket, and filling the full remainder would just be refused.
    // ⚠ FINDING Y: on a refund the ceiling is what THIS METHOD took on the original sale, so "rest"
    // fills a number that will be accepted rather than one the gate is about to refuse.
    // ⚠⚠ STORE CREDIT IS CAPPED AT THE BALANCE TOO (2026-08-19). Matt: *"the 'Rest' button needs to
    // only ever put the max credit they have at the time in. There is No point putting the full number
    // in."* He is right, and the argument is the one written two lines above for the gift card —
    // "filling the full remainder would just be refused" — which was made for one balance-backed tender
    // and not the other. A £76 basket against £10 of credit filled £76, which the redeem then refuses.
    // ⚠ MAUI has capped this since the tender-cap work: `capPence = CreditAvailablePence`, and the
    // amount prompt pre-fills the SMALLER of that and the balance. The web till was the odd one out.
    const rowCap = id === GIFTCARD_PAYID
      ? card?.balancePence ?? 0
      : id === CREDIT_PAYID
        ? customer?.creditBalancePence ?? 0
        : refunding ? capacityFor(caps, tenderTypeFor(rows.find((m) => m.id === id)?.name ?? "")) ?? undefined : undefined;
    const cap = restFor(owed, paid, own, rowCap ?? undefined);
    setAmounts((a) => ({ ...a, [id]: (cap / 100).toFixed(2) }));
  }

  async function complete() {
    if (!canComplete || !parsed.valid) return;
    setBusy(true);
    setError("");
    try {
      // Change is attributed to the changeable method(s) proportionally, with the LAST
      // changeable payment absorbing the rounding remainder — Σchange must equal the
      // overpay to the penny (the v1 pipeline enforces net tender == gross).
      // ⚠ Computed in ./tendering.ts and tested there; `assess` already did it for this basket.
      const entries = [...parsed.perMethod.entries()];
      const changeByPayId = settled.changeByPayId;
      const payments = entries.map(([payId, pence]) => ({
        payId,
        name: tenders.find((x) => x.id === payId)!.name,
        // money OUT on a refund — the wire carries the sign, so net tender == the (negative)
        // gross and the T1.3 invariant reconciles
        amountPence: refunding ? -pence : pence,
        changePence: changeByPayId.get(payId) ?? 0,
      }));

      // FE7 single-purpose treatment: the card's VAT was declared when it was sold, so here it is a
      // negative sale line (built inside checkout()), NOT a tender — the wire tenders carry only the
      // real money. The receipt still lists the card with the payments, which is what the customer
      // expects to read. Multi-purpose keeps the card as a true tender.
      const wireTenders = giftRedeem > 0 && card?.vatTreatment === "single"
        ? payments.filter((p) => p.payId !== GIFTCARD_PAYID)
        : payments;

      // ⚠ W-P7: `wireLines`/`effective`, not `lines`/`totals` — the fee line and the totals that
      // include it. The ingest guard compares the header gross against the sum of the line grosses,
      // so sending one without the other is a sale the server refuses.
      const sale = await checkout(wireLines, wireTenders, effective, {
        customerId: customer?.id,
        creditRedeemPence: creditRedeem > 0 && customer ? creditRedeem : undefined,
        // FE7: the ledger write happens inside checkout(), BEFORE the sale is recorded, so a card the
        // server refuses aborts here instead of leaving a short-tendered sale on the books.
        giftCardRedeem: giftRedeem > 0 && card
          ? { code: card.code, amountPence: giftRedeem, treatment: card.vatTreatment }
          : undefined,
      });
      onComplete({
        saleId: sale.saleId,
        date: new Date().toISOString(),
        // ⚠ The customer's receipt must show the fee it was charged, itemised — a total that is 35p
        // more than the goods with nothing saying why is the complaint that follows.
        lines: wireLines,
        totalPence: effective.totalPence,
        totalExTaxPence: effective.totalExTaxPence,
        payments: payments.map((p) => ({ name: p.name, amountPence: p.amountPence, changePence: p.changePence })),
        queued: sale.queued,
      });
    } catch (e) {
      setError(String(e));
      setBusy(false);
    }
  }

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog">
        {/* ⚠ W-P7: `effective`, so the heading is the figure the operator must actually collect. */}
        <h2>{refunding ? `Refund — ${gbp(owed)}` : `Checkout — ${gbp(effective.totalPence)}`}</h2>
        <DialogX onClose={onClose} disabled={busy} />

        {/* ⚠⚠ W-P7: SAY THE FEE, ITEMISED, BEFORE the sale completes. A total that jumps when a card
            row is filled and explains nothing is the surcharge complaint every time. */}
        {feePence > 0 && (
          <p className="small discount-note">
            💳 Card fee <strong>{gbp(feePence)}</strong> added — {gbp(totals.totalPence)} of goods
            {" "}+ {gbp(feePence)}. It comes off if the card row is cleared.
          </p>
        )}
        {feeRefusal && <p className="error small">{feeRefusal}</p>}

        {refunding && (
          <p className="small discount-note">
            ↩ This basket returns more than it sells, so it is a <strong>refund</strong>: enter how
            much goes back on each method. The sale is recorded with negative totals.
          </p>
        )}

        {/* 17.2 card-payment setup hint: standalone (default) = external chip & pin, cashier
            confirms; an integrated provider shows its name until its integration is wired. */}
        {(!gateway || gateway.provider === "standalone") ? (
          <p className="muted small">
            💳 Card: {refunding
              ? "refund on the chip & pin terminal, confirm it went through, then complete."
              : "take payment on the chip & pin terminal, confirm it's approved, then complete."}
          </p>
        ) : (
          <p className="muted small">💳 Card via <strong>{gateway.label}</strong>{!gateway.integrated && " (integration pending — use the terminal and confirm approval as usual)"}.</p>
        )}

        {/* FE7: take a gift card as payment. Scan it (keyboard-wedge types + Enter) or type the code;
            the balance comes from the server, which is the authority — the till never guesses. */}
        {sellingACard ? (
          <p className="muted small">
            🎁 This sale <strong>sells</strong> a gift card, so another card can't be used to pay for it.
          </p>
        ) : !navigator.onLine ? (
          <p className="muted small">🎁 Gift cards need a connection — this till is offline, so cards can't be taken right now.</p>
        ) : card ? (
          <p className="small">
            🎁 <strong>{card.pretty}</strong> holds <strong>{gbp(card.balancePence)}</strong>
            {card.expiresAtUtc && <span className="muted"> · expires {new Date(card.expiresAtUtc).toLocaleDateString("en-GB")}</span>}
            {" "}
            <button className="ghost small" onClick={() => { setCard(null); setAmounts((a) => ({ ...a, [GIFTCARD_PAYID]: "" })); }}>
              remove
            </button>
          </p>
        ) : cardOpen ? (
          <div className="setting-row">
            <span className="grow small">🎁 Scan or type the gift-card code.</span>
            <input
              className="pref-input"
              placeholder="e.g. K7QP-2M9W-XT4R-8"
              value={cardInput}
              // ⚠ Focused on open, because the operator's next act is a SCAN — a box that needs
              // clicking first turns a scanner into a keyboard that types into nothing.
              autoFocus
              onChange={(e) => setCardInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); void checkCard(); } }}
            />
            <button className="ghost" disabled={cardBusy || !cardInput.trim()} onClick={() => void checkCard()}>
              {cardBusy ? "Checking…" : "Check card"}
            </button>
            <button
              className="ghost small"
              onClick={() => { setCardOpen(false); setCardInput(""); setCardError(""); }}
            >
              Cancel
            </button>
          </div>
        ) : null}
        {cardError && <p className="error small">{cardError}</p>}

        <div className="pay-methods">
          {rows.map((m) => (
            <label key={m.id} className="pay-row">
              <span className="grow">
                {m.name}
                {m.isChangeable && <span className="muted small"> (gives change)</span>}
                {m.id === GIFTCARD_PAYID && <span className="muted small"> (up to {gbp(card?.balancePence ?? 0)})</span>}
                {/* ⚠ Say the ceiling on the credit row too, for the same reason it is said on the gift
                    card: "rest" now fills at most this, so a number the redeem would refuse is never
                    offered. Matt: *"There is No point putting the full number in."* */}
                {m.id === CREDIT_PAYID && <span className="muted small"> (up to {gbp(customer?.creditBalancePence ?? 0)})</span>}
              </span>
              <button className="ghost small" tabIndex={-1} onClick={(e) => { e.preventDefault(); quickFill(m.id); }}>
                rest
              </button>
              <input
                inputMode="decimal"
                placeholder="0.00"
                value={amounts[m.id] ?? ""}
                onChange={(e) => setAmounts((a) => ({ ...a, [m.id]: e.target.value }))}
              />
            </label>
          ))}
        </div>

        {/* ⚠⚠ BELOW THE TENDERS AND CLOSED BY DEFAULT (2026-08-19). Matt: *"can the Giftcard go below
            and say Pay with Gift Card button, which then pops the box to scan. There is no point showing
            it all, unless you have a card."* Most sales involve no gift card, and the scan box used to be
            the FIRST thing on the checkout screen — read past on every single sale.

            ⚠ Hidden entirely while a card is already attached (the row above says what it holds) and
            while the basket is SELLING a card — paying with one in that sale would launder an expiring
            balance onto a fresh card, which is why the tender is refused there anyway. */}
        {!card && !cardOpen && !sellingACard && !refunding && (
          <button className="ghost" onClick={() => { setCardOpen(true); setCardError(""); }}>
            🎁 Pay with a gift card
          </button>
        )}

        <div className="totals">
          <div className="row muted">
            <span>{refunding ? "Refunding" : "Paid"}</span>
            <span>{parsed.valid ? gbp(paid) : "—"}</span>
          </div>
          {remaining > 0 && (
            <div className="row">
              <span>{refunding ? "Still to refund" : "Remaining"}</span>
              <span>{gbp(remaining)}</span>
            </div>
          )}
          {overRefund && (
            <div className="row error">
              <span>Too much — refund exactly {gbp(owed)}</span>
              <span>{gbp(paid - owed)} over</span>
            </div>
          )}
          {overpay > 0 && (
            <div className={changeOk ? "row grand" : "row error"}>
              <span>{changeOk ? "Change" : "Change (not available on chosen methods)"}</span>
              <span>{gbp(overpay)}</span>
            </div>
          )}
        </div>

        {/* ⚠ FINDING Y: say where the rest has to go, or the operator's next move is to hunt for a
            different card rather than to split the refund. */}
        {tenderRefusal && (
          <p className="error small">
            {tenderRefusal} Refund what each method paid — {rows.map((m) =>
              `${m.name} up to ${gbp(capacityFor(caps, tenderTypeFor(m.name)) ?? 0)}`).join(", ")}.
          </p>
        )}
        {creditOverBalance && (
          <p className="error small">Store credit exceeds the customer's balance ({gbp(customer?.creditBalancePence ?? 0)}).</p>
        )}
        {giftOverBalance && (
          <p className="error small">That's more than the gift card holds ({gbp(card?.balancePence ?? 0)}) — take the rest another way.</p>
        )}
        {error && <p className="error small">{error}</p>}

        {/* Say WHY the sale can't complete, not just refuse to let it.
            Matt, 2026-08-11, about the MAUI till: over-paying by card said only "Something went
            wrong". The web till's version of that failure is quieter and easier to miss — the
            Complete button simply greys out and nothing explains it, which is the same fault:
            the screen has decided something and not said what.
            MAUI names its refusals (TenderRefusal — zero, wrong direction, overpaid without
            change); this is the matching sentence, from the same tested arithmetic.
            Suppressed while `busy` and when one of the specific messages above is already showing,
            so the operator never gets two explanations of one problem. */}
        {!busy && !creditOverBalance && !giftOverBalance && !feeRefusal && !canComplete && (
          <p className="muted small">{refusalReason(settled, parsed, gbp)}</p>
        )}

        <div className="dialog-actions">
          <button className="ghost" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button className="primary" disabled={!canComplete} onClick={complete}>
            {busy ? "Completing…" : refunding ? "Complete refund" : "Complete sale"}
          </button>
        </div>
      </div>
    </div>
  );
}
