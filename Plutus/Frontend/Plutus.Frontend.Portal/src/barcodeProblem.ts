/**
 * What is wrong with a barcode somebody is typing — decided as they type, before any request.
 *
 * ⚠⚠ MATT, 2026-08-20: *"When barcodes are added, they need to be checked real time to ensure
 * uniqueness. With numbers having no whitespace on the start or end (Highlighting and warning if they
 * do)."* And: *"Barcodes do need to be editable incase you misstype it, but again have the same real
 * time check."* — hence the `editing` parameter, which is the whole reason a correction is possible
 * without the box calling the code a duplicate of itself.
 *
 * ⚠⚠ UNIQUENESS IS ANSWERED LOCALLY AND INSTANTLY, because the barcodes endpoint returns the WHOLE
 * tenant's codes — so the alias namespace needs no round trip at all. The one thing this cannot see is
 * whether the code is some other item's OWN barcode; that is a debounced lookup in the component, and
 * the server re-checks both halves on submit regardless.
 *
 * ⚠⚠ THIS FILE EXISTS IN BOTH FRONTENDS AND THE TWO COPIES ARE BYTE-FOR-BYTE IDENTICAL:
 * `Plutus.Frontend.WebApp/src/barcodeProblem.ts` and `Plutus.Frontend.Portal/src/barcodeProblem.ts`.
 * Change one → change the other. Same rule as `DataTable.tsx` / `Ask.tsx` / `Barcode39.tsx`, and
 * `barcodeProblem.test.ts` in the web till FAILS if the two files differ, so this one is pinned rather
 * than merely asked for politely.
 *
 * ⚠ It is pulled out of `ItemBarcodes.tsx` because those two components are NOT identical — the till's
 * gates on `canManageBarcodes()` and uses the till's class names — and a rule buried in a file that
 * legitimately differs is a rule that drifts without anyone noticing.
 *
 * ⚠⚠ THIS IS THE COURTESY CHECK, NOT THE GATE. What may be a barcode is decided once, server-side, by
 * `SharedKernel.ItemBarcodeRules` plus the `(TenantId, Code)` unique index; this only spares the
 * operator a round trip for the two refusals that need no rule to spot. It deliberately does NOT
 * restate the reserved shapes (membership cards, gift cards, bag ids, platform ids) — those change,
 * and a stale copy here would refuse a code the server would have accepted.
 */

/** `error` blocks the save; `warn` does not — the server trims, so whitespace is a nudge, not a veto. */
export interface BarcodeProblem {
  level: "error" | "warn";
  message: string;
}

/**
 * @param raw      exactly what is in the box, UNTRIMMED — the whitespace warning needs to see it
 * @param mine     the codes this item already has
 * @param allCodes every additional code in the tenant, this item's included
 * @param ownId    this item's own `IdOne`, which is an identity rather than an alias
 * @param editing  when correcting an existing row, the code being corrected — it must not clash with
 *                 itself, which is what would otherwise happen the moment the box is opened
 */
export function barcodeProblem(
  raw: string,
  mine: readonly string[],
  allCodes: readonly string[],
  ownId: string,
  editing?: string,
): BarcodeProblem | null {
  if (raw === "") return null;

  // ⚠ WHITESPACE IS A WARNING, NOT A REFUSAL. The server trims, so the code saves correctly — but a
  // pasted code with a trailing space is worth saying out loud, because it usually means the rest of
  // what was pasted is not what the person thinks either.
  //
  // ⚠ FIRST, and on the RAW string. " 123 " and "123" are the same barcode to the server, so checking
  // the trimmed value against the list would call it a duplicate of itself and block a legal save.
  if (raw !== raw.trim()) {
    return { level: "warn", message: "That has a space at the start or end — it will be saved without it." };
  }

  const code = raw.trim();

  // ⚠ The item's OWN barcode is not an alias of itself. Allowing it would create a row that resolves
  // an item to itself — harmless to scan, but it would then appear in this list as a removable code,
  // which invites somebody to "tidy up" an identity they cannot actually remove.
  if (code === ownId) return { level: "error", message: "That is already this item's own barcode." };

  // ⚠ Before the two list checks: the code being edited IS in both lists, by definition.
  if (editing !== undefined && code === editing) return null;

  if (mine.includes(code)) return { level: "error", message: "This item already has that barcode." };
  if (allCodes.includes(code)) return { level: "error", message: "Another item already has that barcode." };
  return null;
}
