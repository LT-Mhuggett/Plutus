import { useEffect, useMemo, useState } from "react";
import {
  checkout, fetchActiveGateway, fetchPayMethods, lookupGiftCard,
  type ActiveGateway, type CustomerDetail, type GiftCardLookup, type PayMethod,
} from "../api.ts";
import { gbp, parsePence } from "../money.ts";
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

  const parsed = useMemo(() => {
    const perMethod = new Map<number, number>();
    for (const [id, raw] of Object.entries(amounts)) {
      if (!raw.trim()) continue;
      const pence = parsePence(raw);
      if (pence === null) return { valid: false as const };
      if (pence > 0) perMethod.set(Number(id), pence);
    }
    const paid = [...perMethod.values()].reduce((a, b) => a + b, 0);
    return { valid: true as const, perMethod, paid };
  }, [amounts]);

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

  const paid = parsed.valid ? parsed.paid : 0;
  const remaining = Math.max(0, totals.totalPence - paid);
  const overpay = Math.max(0, paid - totals.totalPence);
  // change can only be given from a changeable method (cash)
  const changeablePaid = parsed.valid
    ? [...parsed.perMethod.entries()].filter(([id]) => tenders.find((m) => m.id === id)?.isChangeable).reduce((a, [, v]) => a + v, 0)
    : 0;
  const changeOk = overpay === 0 || overpay <= changeablePaid;
  const creditRedeem = parsed.valid ? parsed.perMethod.get(CREDIT_PAYID) ?? 0 : 0;
  const creditOverBalance = creditRedeem > (customer?.creditBalancePence ?? 0);
  const giftRedeem = parsed.valid ? parsed.perMethod.get(GIFTCARD_PAYID) ?? 0 : 0;
  const giftOverBalance = giftRedeem > (card?.balancePence ?? 0);
  const canComplete = parsed.valid && remaining === 0 && changeOk
    && !creditOverBalance && !giftOverBalance && !busy;

  function quickFill(id: number) {
    // FE7: "rest" on the gift-card row is capped at what the card holds — the common case is a card
    // that doesn't cover the whole basket, and filling the full remainder would just be refused.
    const cap = id === GIFTCARD_PAYID ? Math.min(remaining, card?.balancePence ?? 0) : remaining;
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
      const entries = [...parsed.perMethod.entries()];
      const changeable = entries.filter(([id]) => tenders.find((m) => m.id === id)?.isChangeable);
      const changeByPayId = new Map<number, number>();
      let allocated = 0;
      changeable.forEach(([payId, pence], i) => {
        const change =
          i === changeable.length - 1 ? overpay - allocated : Math.round((overpay * pence) / changeablePaid);
        allocated += change;
        changeByPayId.set(payId, change);
      });
      const payments = entries.map(([payId, pence]) => ({
        payId,
        name: tenders.find((x) => x.id === payId)!.name,
        amountPence: pence,
        changePence: changeByPayId.get(payId) ?? 0,
      }));

      const sale = await checkout(lines, payments, totals, {
        customerId: customer?.id,
        creditRedeemPence: creditRedeem > 0 && customer ? creditRedeem : undefined,
        // FE7: the ledger write happens inside checkout(), BEFORE the sale is recorded, so a card the
        // server refuses aborts here instead of leaving a short-tendered sale on the books.
        giftCardRedeem: giftRedeem > 0 && card ? { code: card.code, amountPence: giftRedeem } : undefined,
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
        <h2>Checkout — {gbp(totals.totalPence)}</h2>

        {/* 17.2 card-payment setup hint: standalone (default) = external chip & pin, cashier
            confirms; an integrated provider shows its name until its integration is wired. */}
        {(!gateway || gateway.provider === "standalone") ? (
          <p className="muted small">💳 Card: take payment on the chip &amp; pin terminal, confirm it's approved, then complete.</p>
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
          {tenders.map((m) => (
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
            <span>Paid</span>
            <span>{parsed.valid ? gbp(paid) : "—"}</span>
          </div>
          {remaining > 0 && (
            <div className="row">
              <span>Remaining</span>
              <span>{gbp(remaining)}</span>
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

        <div className="dialog-actions">
          <button className="ghost" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button className="primary" disabled={!canComplete} onClick={complete}>
            {busy ? "Completing…" : "Complete sale"}
          </button>
        </div>
      </div>
    </div>
  );
}
