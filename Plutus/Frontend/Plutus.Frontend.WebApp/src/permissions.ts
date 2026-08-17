// W-P3 — what the signed-in operator may do, and up to how much money.
//
// ⚠⚠ THE C2 TWIN of `src/Plutus.SharedKernel/Permissions.cs` — `PermissionGrant.IsActiveAt`,
// `EffectivePermission.Merge` and `PermissionResolution.Can`. **This is a MONEY rule**: it decides
// how much a cashier may take off a basket, so the two tills disagreeing means one shop's cashiers
// have a limit and another's do not. Mirrored case for case against `PermissionsTests`.
//
// ⚠⚠ WHY THE WEB TILL NEEDED IT AT ALL. It had **no client-side permission model whatsoever** —
// `session.ts` holds token, employeeId and name, and nothing else. So a web-till cashier could take
// off ANY amount and the only thing standing in the way was the server at ingest. MAUI has gated on
// `pos.discount` with the operator's own ceiling since 2026-08-14.
//
// ⚠ Matt, 2026-08-14: *"Base it on roles."* A discount level IS a role's `pos.discount` `MaxPence` —
// there is no separate tier entity, deliberately: it would state a cashier's money limit twice with
// nothing to notice the two disagreeing.

import type { OperatorGrant } from "./roster.ts";

/** One resolved permission: a code and its ceiling. ⚠ `null` = unlimited / not amount-based. */
export interface EffectivePermission {
  code: string;
  maxPence: number | null;
}

/**
 * Is this grant live right now?
 *
 * ⚠ Mirrors `PermissionGrant.IsActiveAt` exactly, including its deliberate limitation: **a window
 * that wraps midnight (22:00–02:00) is NOT supported** — it rejects everything. Left unhandled
 * rather than half-handled on the .NET side, because a night-shift window that silently denied every
 * action would be worse than one nobody can save in the first place. Do not "fix" that here alone:
 * it would make the two tills disagree.
 *
 * ⚠ Valid-from/to are compared in **UTC**; the day mask and the time window in **local** time. That
 * split is intentional — a validity period is a platform fact, a shift window is a shop-floor one.
 */
export function isActiveAt(grant: OperatorGrant, nowLocal: Date): boolean {
  const nowUtcMs = nowLocal.getTime();

  if (grant.validFromUtc && nowUtcMs < Date.parse(grant.validFromUtc)) return false;
  if (grant.validToUtc && nowUtcMs > Date.parse(grant.validToUtc)) return false;

  // ⚠ Bit per day, Sunday = 0 — same as .NET's `DayOfWeek`, which also starts at Sunday. A different
  // origin here would shift every shift window by a day.
  if (grant.daysOfWeekMask != null && (grant.daysOfWeekMask & (1 << nowLocal.getDay())) === 0) return false;

  if (grant.windowStartLocal || grant.windowEndLocal) {
    const minutes = nowLocal.getHours() * 60 + nowLocal.getMinutes();
    const start = parseTimeOfDay(grant.windowStartLocal);
    const end = parseTimeOfDay(grant.windowEndLocal);
    if (start != null && minutes < start) return false;
    if (end != null && minutes > end) return false;
  }

  return true;
}

/** `"HH:mm"` / `"HH:mm:ss"` → minutes since midnight, or null. ⚠ An unparseable window is treated as
 *  ABSENT rather than as a closed door: a malformed value in the portal must not lock a shop out. */
function parseTimeOfDay(value: string | null): number | null {
  if (!value) return null;
  const m = /^(\d{1,2}):(\d{2})/.exec(value.trim());
  if (!m) return null;
  return Number(m[1]) * 60 + Number(m[2]);
}

/**
 * Union-merge the live grants. ⚠ Mirrors `EffectivePermission.Merge`: **per code, an unlimited grant
 * beats any ceiling; otherwise the LARGEST ceiling wins.**
 *
 * ⚠ Union, not intersection — two roles that each allow a £20 discount do not make £40, but a role
 * with no ceiling plus a role capped at £20 means no ceiling. Getting this backwards would silently
 * demote every manager who also holds a cashier role.
 */
export function effectiveAt(grants: OperatorGrant[] | null | undefined, nowLocal: Date): EffectivePermission[] {
  const live = (grants ?? []).filter((g) => g && g.code && isActiveAt(g, nowLocal));
  const byCode = new Map<string, number | null>();

  for (const g of live) {
    const existing = byCode.get(g.code);
    if (!byCode.has(g.code)) {
      byCode.set(g.code, g.maxPence ?? null);
      continue;
    }
    // ⚠ Once a code has an unlimited grant it stays unlimited.
    if (existing === null) continue;
    byCode.set(g.code, g.maxPence == null ? null : Math.max(existing as number, g.maxPence));
  }

  return [...byCode.entries()]
    .map(([code, maxPence]) => ({ code, maxPence }))
    .sort((a, b) => (a.code < b.code ? -1 : a.code > b.code ? 1 : 0));
}

/**
 * May this operator do `code`, for `amountPence`?
 *
 * ⚠⚠ FAILS CLOSED, including on an unknown code. `"perm:x"` and a policy name are different
 * namespaces, and a typo between them has already caused one silent outage here (the pick-notes
 * gate) — so an unrecognised code is DENIED, never waved through.
 *
 * ⚠ A ceiling applies to the AMOUNT, so a null amount against a ceiling-bearing grant is **allowed**:
 * *"may discount at all"* and *"may discount £40"* are different questions, and the discount dialog
 * asks the first before it knows the answer to the second.
 *
 * ⚠ Codes compare **case-sensitively** — same as the .NET `StringComparison.Ordinal`.
 */
export function can(
  grants: OperatorGrant[] | null | undefined,
  code: string,
  nowLocal: Date,
  amountPence: number | null = null,
): boolean {
  if (!code) return false;

  for (const p of effectiveAt(grants, nowLocal)) {
    if (p.code !== code) continue;
    if (amountPence == null || p.maxPence == null) return true; // unlimited, or not amount-based
    if (amountPence <= p.maxPence) return true;
  }
  return false;
}

/** The ceiling for a code, or null when unlimited/absent. ⚠ Callers must use `can` to decide
 *  ALLOWED-ness — this is only for showing the operator the number they are up against. */
export function ceilingFor(
  grants: OperatorGrant[] | null | undefined,
  code: string,
  nowLocal: Date,
): number | null {
  const hit = effectiveAt(grants, nowLocal).find((p) => p.code === code);
  return hit ? hit.maxPence : null;
}

/** The permission a manual discount is gated on. ⚠ Same code MAUI uses. */
export const POS_DISCOUNT = "pos.discount";

/** The permission that reopens a Z-closed day (W-P5). ⚠ Supervisor and up hold it, never Cashier —
 *  the person who counted the drawer must not be the only one who can quietly un-count it. */
export const POS_CASH_REOPEN = "pos.cash.reopen";
