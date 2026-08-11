import { useEffect, useMemo, useState } from "react";
import {
  checkout, fetchActiveGateway, fetchPayMethods, lookupGiftCard,
  type ActiveGateway, type CustomerDetail, type GiftCardLookup, type PayMethod,
} from "../api.ts";
import { gbp } from "../money.ts";
import { assess, parseAmounts, refusalReason, restFor } from "./tendering.ts";
import type { BasketLine } from "./basket.ts";
import type { ReceiptData } from "./Receipt.tsx";

// Synthetic pay-method id for the store-credit tender (Phase 8 retrofit). Real PayMethod ids
// are positive; −1 never collides. Maps to a Credit tender server-side (name contains "credit").
const CREDIT_PAYID = -1;
// FE7: same trick for the gift-card tender. Synthetic (not a real PayMethod row) on purpose — a real
// row would put a free-typed "Gift card" amount on every checkout screen, letting a cashier tender
// money against no card at all. This row only appears once a card has been scanned and checked.
const GIFTCARD_PAYID = -2;

interface Props {
  lines: BasketLine[];
  totals: { totalPence: number; totalExTaxPence: number };
  customer?: CustomerDetail | null;
  onClose: () => void;
  onComplete: (receipt: ReceiptData) => void;
}

export default function CheckoutDialog({ lines, totals, customer, onClose, onComplete }: Props) {
  const [methods, setMethods] = useState<PayMethod[] | null>(null);
  const [amounts, setAmounts] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // 17.2: the tenant's card-payment setup — shown as a hint on the tender screen. Offline or
  // unfetchable → assume standalone (today's flow); never blocks checkout.
  const [gateway, setGateway] = useState<ActiveGateway | null>(null);
  useEffect(() => { fetchActiveGateway().then(setGateway).catch(() => undefined); }, []);

  // FE7: a gift card being SPENT on this sale. Scanned/typed here, checked against the server (which
  // owns the balance), then offered as a tender capped at what the card actually holds.
  const [cardInput, setCardInput] = useState("");
  const [card, setCard] = useState<GiftCardLookup | null>(null);
  const [cardBusy, setCardBusy] = useState(false);
  const [cardError, setCardError] = useState("");
  // Selling a card and paying with one in the same sale would launder an expiring balance into a
  // fresh card, so the tender is not offered when the basket contains an activation.
  const sellingACard = lines.some((l) => l.giftCardCode);

  useEffect(() => {
    fetchPayMethods()
      .then((real) => {
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

  // A basket of returns worth more than anything bought is a REFUND: the same sale, with every
  // figure negative (the T1.3 invariants are sign-agnostic — see SalesV2Tests). The operator
  // types the amount to hand back as a positive number; it goes on the wire negative.
  const settled = assess(totals.totalPence, parsed, tenders);
  const { refunding, owed, paid, remaining, overpay, overRefund, changeOk } = settled;

  // Refunding ONTO store credit or a gift card would be a ledger write, not a tender — out of
  // scope, so a refund offers only the real money methods.
  const rows = refunding ? tenders.filter((m) => m.id !== CREDIT_PAYID && m.id !== GIFTCARD_PAYID) : tenders;
  const creditRedeem = parsed.valid ? parsed.perMethod.get(CREDIT_PAYID) ?? 0 : 0;
  const creditOverBalance = creditRedeem > (customer?.creditBalancePence ?? 0);
  const giftRedeem = parsed.valid ? parsed.perMethod.get(GIFTCARD_PAYID) ?? 0 : 0;
  const giftOverBalance = giftRedeem > (card?.balancePence ?? 0);
  const canComplete = parsed.valid && remaining === 0 && changeOk && !overRefund
    && !creditOverBalance && !giftOverBalance && !busy;

  function quickFill(id: number) {
    // "rest" = make THIS row cover everything the others don't, so it must ignore what this row
    // already holds. Using the bare remainder made "rest" toggle 0.00 ↔ full whenever the row was
    // already filled (reported 2026-08-07), and left an overpaid row untouched.
    const own = parsed.valid ? parsed.perMethod.get(id) ?? 0 : 0;
    // FE7: "rest" on the gift-card row is capped at what the card holds — the common case is a card
    // that doesn't cover the whole basket, and filling the full remainder would just be refused.
    const cap = restFor(owed, paid, own, id === GIFTCARD_PAYID ? card?.balancePence ?? 0 : undefined);
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

      const sale = await checkout(lines, wireTenders, totals, {
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
        lines,
        totalPence: totals.totalPence,
        totalExTaxPence: totals.totalExTaxPence,
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
        <h2>{refunding ? `Refund — ${gbp(owed)}` : `Checkout — ${gbp(totals.totalPence)}`}</h2>

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
        ) : (
          <div className="setting-row">
            <span className="grow small">🎁 Paying with a gift card? Scan or type the code.</span>
            <input
              className="pref-input"
              placeholder="e.g. K7QP-2M9W-XT4R-8"
              value={cardInput}
              onChange={(e) => setCardInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); void checkCard(); } }}
            />
            <button className="ghost" disabled={cardBusy || !cardInput.trim()} onClick={() => void checkCard()}>
              {cardBusy ? "Checking…" : "Check card"}
            </button>
          </div>
        )}
        {cardError && <p className="error small">{cardError}</p>}

        <div className="pay-methods">
          {rows.map((m) => (
            <label key={m.id} className="pay-row">
              <span className="grow">
                {m.name}
                {m.isChangeable && <span className="muted small"> (gives change)</span>}
                {m.id === GIFTCARD_PAYID && <span className="muted small"> (up to {gbp(card?.balancePence ?? 0)})</span>}
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
        {!busy && !creditOverBalance && !giftOverBalance && !canComplete && (
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
