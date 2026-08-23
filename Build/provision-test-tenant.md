# Provisioning a second tenant, to test a till against something that isn't Kapow

> **Matt, 2026-08-23:** *"I then need to test MAUI with a different tennant to Kapow"*
>
> ⚠⚠ **DO NOT TEST AGAINST KAPOW.** It is a live shop with real sales, real staff and a real VAT
> position. Every basket rung up on it lands in `salesv2` and on a return. That is the whole reason
> for this document.

## ⚠⚠ READ THIS FIRST — the chain does not currently complete

**Traced against the code on 2026-08-23, and the first version of this page was wrong about it.**
`POST /api/v1/tenants` creates a tenant, a Business, a Store *and* an admin login — and then stops.
**It creates no RBAC roles and assigns the admin no role.**

So the new admin can sign in, and `ResolveLoginScopesAsync` gives them **`pos.sell` and nothing
else** (`EffectivePermissionsService:133` — no assignments means the legacy branch, and a fresh
tenant has no `EmpAuthActions` rows to make them a legacy admin either). They cannot create a till,
cannot add a user, cannot manage the company.

⚠ **`RbacSeeder`'s own docstring says this is handled** — *"provisioning calls
`EnsureBuiltInRolesAsync` for new tenants"* (`RbacSeeder.cs:22`). **`ProvisionAsync` does not call
it.** The documentation and the code disagree, and the code wins.

⚠⚠ **AND THAT IS EXACTLY WHY `Demo Store` IS AN EMPTY SHELL** — 0 stores, 0 tills, 0 employees, 0
role assignments. It is not a half-finished experiment somebody abandoned; it is what this endpoint
produces. Provisioning a second one the same way gets a second shell.

## What exists today

| Tenant | State |
|---|---|
| **Kapow Comics Ltd** | ⚠ **LIVE.** Not for testing. |
| **Demo Store** | Flagged `IsSandbox`, and **an empty shell** — see above. |

## The fix — ~½ day, and self-serve signup needs it anyway

Make provisioning do what its docstring already claims:

1. `RbacSeeder.EnsureBuiltInRolesAsync(db, tenantId)` — idempotent, creates Owner · Company Admin ·
   Store Manager · Supervisor · Cashier · Auditor for the tenant.
2. Assign the provisioned admin the **Owner** role at company scope. Owner carries every portal
   permission including **`portal.tills.enrol`** (`RbacSeeder:94`, `:132`) — which is the one that
   unblocks creating a till.

⚠ **One design constraint.** `Plutus.Tenancy` **does not reference `Plutus.Identity`** (deliberate —
see the note on `Pbkdf2` in `Crypto.cs`), so `ProvisioningService` cannot call `RbacSeeder`
directly. Put a small interface in `Plutus.SharedKernel`, implement it in `Plutus.Identity`, inject
it. Do **not** re-implement the role catalogue inside Tenancy — that is a C2-shaped drift waiting to
happen, with permissions as the thing that drifts.

⚠ **This is not throwaway work for a test tenant.** [`WP-signup.md`](To%20do/WP-signup.md) stage 5
provisions sandbox-first through this same service, and it will hit the same wall. Fixing it here
means signup inherits a working path rather than rediscovering this.

## Which token — ⚠ not the one the first version of this page named

`POST /api/v1/tenants` is `[Authorize(Policy = PlutusPolicies.PlatformAdmin)]`, and **`platform-admin`
is not a grantable RBAC permission** — it is not in the catalogue and no role carries it. So no
ordinary portal login can create a tenant, however senior.

Two things do:

- ⚠⚠ **Your own portal session**, if it carries the scope. **It evidently does** — the **Platform**
  tab only renders when it does (`App.tsx:244`), and you have been using Platform → Quarantine.
  The token is in `localStorage["plutus.portal.session"]` on `admin.plutus.huggett.dscloud.me`.
- A Keycloak realm role `platform-admin`, via the `operators` group (TOTP required).

⚠⚠ **A HAND-MINTED HMAC TOKEN DOES NOT WORK, AND I CHECKED RATHER THAN ASSUMING.** Minted one on the
Mac with the live `TEST_TOKEN_SECRET` and called a platform-admin endpoint: **403**, against **401**
with no token at all — so it authenticated and was refused. That is **WP18.1 working as designed**:
`AddScopes` drops `platform-admin` from HMAC tokens (`PlutusTokenAuthHandler:133`). Do not go
looking for the bug; there isn't one.

⚠ **`platform-admin` DOES satisfy every `perm:*` gate** (`PermissionPolicies.cs:42`) — but **not**
the scope policies. `portal.tills.enrol` is `RequireClaim("scope", …)` (`IdentityModule:56`), so
creating a till needs that scope specifically, and a platform admin does not have it. **That is the
second half of why the chain stalls**, and impersonation does not rescue it: an impersonation token
carries the *target's* scopes (`ImpersonationController:64`), and the target has none.

## The chain, once the fix is in

```bash
API=https://plutus.huggett.dscloud.me
AUTH="Authorization: Bearer $TOKEN"      # platform-admin, from your portal session
JSON="Content-Type: application/json"
```

### 1. The tenant — and this now yields a usable admin

```bash
curl -sX POST "$API/api/v1/tenants" -H "$AUTH" -H "$JSON" -d '{
  "name":          "Test Shop",
  "plan":          "standard",
  "adminEmail":    "test-admin@example.test",
  "adminPassword": "<a real password — this account can sign in>"
}'
```

Returns `tenantId`, `companyId`, **`storeId`** and `adminUserId`.

⚠ **There is no separate "create a store" step** — provisioning already makes one, address `"Main"`
with `"N/A"` placeholders it expects the tenant to edit. The first version of this page had a step 2
that created a *second*, redundant store.

⚠ **`adminPassword` is a real credential**, PBKDF2-hashed like any other, and nothing here expires on
its own.

### 2. Sign into the portal as that admin

With the fix in, they hold **Owner**, so everything below is ordinary portal work rather than API
calls. Tidy the store's placeholder address while you are there.

### 3. A till, and its enrolment code

Portal → **Tills**, or:

```bash
curl -sX POST "$API/api/v1/tills" -H "Authorization: Bearer $ADMIN_TOKEN" -H "$JSON" \
     -d '{"storeId": <storeId>, "name":"Test Till 1"}'
```

⚠ **`$ADMIN_TOKEN`, not `$TOKEN`** — this endpoint wants `portal.tills.enrol`, which the Owner has
and the platform admin does not. The code is single-use and short-lived; if it lapses,
`POST /api/v1/tills/{tillId}/enrol-code` mints another.

### 4. Somebody who can actually sell

⚠⚠ **THE STEP THAT GETS FORGOTTEN, AND THE TILL WILL NOT LET ANYONE IN WITHOUT IT.** The roster only
carries staff holding a `pos.*` permission — `TillOperatorsController` skips everyone else,
deliberately, because a name on a till with no capability is exposure for nothing.

Portal → **Users** → add a person → give them a role with till permissions (Cashier is enough).
The till's sign-in errors then name which of the four things is wrong: *"this till isn't connected
yet"*, *"nobody is assigned to this till"*, *"can't reach Plutus"*, or *"that account isn't on this
till's staff list"*.

### 5. Something to sell

Items are the portal's (*"Portal decides, till obeys"*). Add a handful under **Inventory**, each with
a barcode and a VAT band, or the till scans into an empty catalogue.

⚠ Check **Company → VAT periods** too: a tenant with no VAT settings reports oddly, and the till's
band names come from the platform.

### 6. Point the till at it

MAUI: **Settings → Till device → Connection, enrolment & diagnostics**, enter the code from step 3.

⚠⚠ **UN-ENROL FROM KAPOW FIRST**, or you are testing whichever tenant it is still enrolled to. The
till-name badge in the app bar (§G80a) is the fastest check — it should read "Test Till 1".

⚠ An **unpackaged** build keeps its data in an ordinary AppData path, separate from any installed
MSIX (runbook), so a test enrolment does not disturb an installed one — but **two unpackaged builds
share that path**, and re-enrolling swaps the tenant for both.

## After testing

`POST /api/v1/tenants/{id}/deletion` schedules removal with a grace period;
`.../deletion/{scheduleId}/cancel` reverses it while the grace lasts. ⚠ Leaving a half-configured
test tenant behind is how **Demo Store** came to exist.
