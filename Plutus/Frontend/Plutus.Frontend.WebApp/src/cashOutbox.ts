// W-P5 — cash events survive an outage, and a Z-closed day can be reopened.
//
// ⚠⚠ WHY THE QUEUE EXISTS. `CashPage` posted online-only, so a float taken while the line was down
// was simply lost — and **a shop opens before its broadband does**. The money moves whether or not the
// platform hears about it, so a float that failed to post is a day whose banking cannot be reconciled
// at all. MAUI has queued these since till 1.44.0 (`CashPushService`).
//
// ⚠⚠ THE STATUS IS THE POLICY, mirrored from `CashPushService`'s header, and getting it wrong is how a
// till either loses a float or hides one for ever:
//
//   • **409** — the business day is already Z-closed. **TERMINAL.** Retrying cannot help, and a till
//     that retries a 409 for ever looks healthy while quietly never banking.
//   • **400** — the platform refused the shape. Terminal for the same reason.
//   • anything else (offline, 5xx, 429, auth hiccup) — retry on the next drain.
//
// ⚠ NOTHING HERE MAY BLOCK SELLING. It rides the 60 s cadence beside the sale outbox.

import { getTillState, openDb, putTillState } from "./offline.ts";
import { businessDay, getDeviceCredential, getDeviceToken, type CashEventType } from "./pipeline.ts";
import { headers } from "./api.ts";
import { lastMarkClosesDay, terminalRefusal, zMayGo } from "./cashRules.ts";

/** ⚠ `ZReopen` is new here (W-P5). It is the ONE event a closed day accepts — see `postOrQueue`. */
export type QueueableCashType = CashEventType;

export interface QueuedCashEvent {
  eventId: string;
  type: QueueableCashType;
  businessDay: string;
  occurredAtUtc: string;
  amountPence: number;
  countedPence: number | null;
  reason: string | null;
  /** Set once the platform has refused it terminally. ⚠ Kept, never deleted — the money moved. */
  refusedReason?: string;
  /** What the platform said the drawer should hold, once it has told us (finding I). */
  expectedPence?: number | null;
  variancePence?: number | null;
}

const STORE = "cashOutbox";

// ── the store (its own tx helper, over `offline.ts`'s SHARED opener) ─────────
// ⚠ The old version of this comment said *"`offline.ts`'s is private"* and that sentence is the
// whole bug: it justified a second `indexedDB.open` with a version number that then went stale.

/**
 * ⚠⚠ USES `offline.ts`'s OPENER — it no longer opens the database itself, and that is the 2026-08-25
 * fix. This function used to read `indexedDB.open("plutus-till", 3)`, a hardcoded version with no
 * `onupgradeneeded` handler, written that way because `offline.ts`'s opener was private.
 *
 * When `DB_VERSION` went to 4 for multi-barcode on 2026-08-20, IndexedDB began refusing this open
 * outright — `VersionError: The requested version (3) is less than the existing version (4)` — so
 * **every cash event that should have queued while the line was down failed to queue**, silently
 * from the operator's side. Matt saw the error in the console on 2026-08-25.
 *
 * ⚠ The version and the upgrade handler must travel together, so there is now exactly one opener.
 * Never call `indexedDB.open("plutus-till", …)` from anywhere else.
 */
function withStore<T>(mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return openDb().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const t = db.transaction(STORE, mode);
        const r = fn(t.objectStore(STORE));
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
      }),
  );
}

export const queuedCashEvents = (): Promise<QueuedCashEvent[]> =>
  withStore("readonly", (s) => s.getAll() as IDBRequest<QueuedCashEvent[]>);

/** Events still waiting to be sent — ⚠ excludes terminally refused ones, which stay for the record. */
export const pendingCashEvents = async (): Promise<QueuedCashEvent[]> =>
  (await queuedCashEvents()).filter((e) => !e.refusedReason);

const put = (e: QueuedCashEvent) => withStore("readwrite", (s) => s.put(e)).then(() => undefined);
const drop = (eventId: string) => withStore("readwrite", (s) => s.delete(eventId)).then(() => undefined);

// ── the local Z guard ───────────────────────────────────────────────────────

/**
 * ⚠⚠ ONE Z PER BUSINESS DAY, ENFORCED LOCALLY AS WELL AS SERVER-SIDE, and it refuses **every type
 * after it** rather than merely a second Z.
 *
 * The server's guard cannot be consulted with the line down — which is exactly when it matters. A till
 * that queued a float after its own Z would send the platform events it is bound to refuse with a 409,
 * and the operator would learn about it a day later.
 *
 * ⚠ A **ZReopen** is the one exception: it is the escape hatch, and locking it inside the thing it
 * unlocks would strand a till until midnight.
 */
export async function localDayIsClosed(day = businessDay()): Promise<boolean> {
  const events = await queuedCashEvents();
  const forDay = events
    .filter((e) => e.businessDay === day && !e.refusedReason)
    .sort((a, b) => a.occurredAtUtc.localeCompare(b.occurredAtUtc));

  // ⚠ The decision lives in `cashRules.ts` so it can be tested without a browser — see its header.
  return lastMarkClosesDay(forDay.map((e) => e.type));
}

// ── sending ─────────────────────────────────────────────────────────────────

export interface CashSendOutcome {
  kind: "sent" | "queued" | "refused";
  /** The platform's own words on a refusal, or the local reason it was refused. */
  detail?: string;
  expectedPence?: number | null;
  variancePence?: number | null;
}

/**
 * Post a cash event, or queue it if the platform cannot be reached.
 *
 * ⚠ Returns `queued` rather than throwing when offline — the operator is told the money is recorded
 * and waiting, which is true, instead of being told it failed.
 */
export async function postOrQueue(input: {
  type: QueueableCashType;
  amountPence?: number;
  countedPence?: number | null;
  reason?: string | null;
}): Promise<CashSendOutcome> {
  const cred = getDeviceCredential();
  if (!cred) return { kind: "refused", detail: "This till is not enrolled as a device — see Settings → Till device." };

  // ⚠ The local guard runs BEFORE anything is queued, so a post-Z event is refused at the counter
  // rather than accepted and 409'd tomorrow.
  if (input.type !== "ZReopen" && (await localDayIsClosed())) {
    return { kind: "refused", detail: "The drawer is already closed for today. Reopen the day first." };
  }

  const event: QueuedCashEvent = {
    eventId: crypto.randomUUID(),
    type: input.type,
    businessDay: businessDay(),
    occurredAtUtc: new Date().toISOString(),
    amountPence: input.amountPence ?? 0,
    countedPence: input.countedPence ?? null,
    reason: input.reason ?? null,
  };

  // ⚠ QUEUED FIRST, THEN SENT. If the browser dies between the POST and the local write, the money is
  // still recorded — the other order loses it. A successful send removes the row immediately.
  await put(event);

  const outcome = await trySend(event);
  if (outcome.kind === "sent") await drop(event.eventId);
  return outcome;
}

async function trySend(event: QueuedCashEvent): Promise<CashSendOutcome> {
  try {
    const token = await getDeviceToken();
    const cred = getDeviceCredential();

    const res = await fetch(`/api/v1/cash-events`, {
      method: "POST",
      headers: { ...headers(), "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify({
        eventId: event.eventId,
        deviceId: cred?.deviceId,
        type: event.type,
        businessDay: event.businessDay,
        occurredAtUtc: event.occurredAtUtc,
        amountPence: event.amountPence,
        countedPence: event.countedPence,
        reason: event.reason,
      }),
    });

    // ⚠⚠ 409 AND 400 ARE TERMINAL, and the platform's own words are kept. A till that retried them
    // for ever would look healthy while quietly never banking.
    if (terminalRefusal(res.status)) {
      const detail = (await res.text().catch(() => "")) || `The platform refused it (${res.status}).`;
      await put({ ...event, refusedReason: detail });
      return { kind: "refused", detail };
    }

    if (!res.ok) return { kind: "queued" }; // 5xx / 429 / auth hiccup — retry on the next drain

    // ⚠ THE BODY IS KEPT. Discarding it was finding I: the platform computes expected-vs-counted and
    // the till threw the only copy away, so a drawer £20 short was accepted with a 201 and nobody at
    // the counter was told.
    const body = (await res.json().catch(() => null)) as
      | { expectedPence?: number | null; variancePence?: number | null }
      | null;

    return { kind: "sent", expectedPence: body?.expectedPence ?? null, variancePence: body?.variancePence ?? null };
  } catch {
    return { kind: "queued" }; // offline, or no token could be minted
  }
}

/**
 * Drain the queue. Called on the 60 s cadence and on the browser's `online` event.
 *
 * ⚠⚠ A Z CLOSE WAITS FOR ITS OWN DAY'S SALES TO DRAIN FIRST. The platform's expected drawer is
 * float + **cash takings** + ins − outs, so a Z that overtakes queued sales reports a shortage equal
 * to every sale still waiting: a till that traded £400 through an outage would tell the person who
 * counted it correctly that they were £400 down. ⚠ And the Z is TERMINAL server-side, so it would then
 * refuse those very sales — putting the day's real takings into quarantine behind their own close.
 *
 * ⚠ `break`, not `continue`: events drain oldest-first and the Z is the last of its day, so anything
 * after it belongs to a later day and must wait its turn.
 *
 * ⚠ Held PER BUSINESS DAY, so one stuck sale from last week cannot block every close from now on.
 */
export async function drainCashOutbox(pendingSalesForDay: (day: string) => Promise<number>): Promise<void> {
  const pending = await pendingCashEvents();
  pending.sort((a, b) => a.occurredAtUtc.localeCompare(b.occurredAtUtc));

  for (const event of pending) {
    if (event.type === "ZClose" && !zMayGo(await pendingSalesForDay(event.businessDay))) break;

    const outcome = await trySend(event);
    if (outcome.kind === "sent") await drop(event.eventId);
    else if (outcome.kind === "queued") break; // still offline — stop, keep the order
  }
}

/** ⚠ For the badge. Counts what is WAITING, not what was refused. */
export const queuedCashCount = async (): Promise<number> => (await pendingCashEvents()).length;

/** Remembered so the Cash screen can show the last refusal without re-reading the whole store. */
export const lastCashRefusal = (): Promise<string | undefined> => getTillState<string>("lastCashRefusal");
export const rememberCashRefusal = (detail: string) => putTillState("lastCashRefusal", detail);
