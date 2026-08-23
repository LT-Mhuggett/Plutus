# Provisioning a second tenant, to test a till against something that isn't Kapow

> **Matt, 2026-08-23:** *"I then need to test MAUI with a different tennant to Kapow"*
>
> ⚠⚠ **DO NOT TEST AGAINST KAPOW.** It is a live shop with real sales, real staff and a real VAT
> position. Every basket rung up on it lands in `SalesV2` and on a return.

## ✅ The blocker is fixed and deployed — backend 1.28.0, 2026-08-23

`ProvisionAsync` used to create a tenant, a Business, a Store and an admin login **and no roles and
no role assignment**, so the new admin signed in holding `pos.sell` alone: unable to create a till,
add a user, or manage the company. **That is what made `Demo Store` an empty shell**, and it is now
fixed — provisioning seeds the built-in roles and assigns the admin **Owner** inside the same
transaction (`ITenantRoleProvisioner`, six tests, mutation-checked).

**Verified live after the 1.28.0 deploy:**

| Tenant | roles | assignments | employees | stores | tills |
|---|---:|---:|---:|---:|---:|
| Kapow Comics Ltd | 12 | 11 | 2 | 2 | 6 |
| Demo Store | 9 | **0** | **0** | **0** | **0** |

⚠ **`Demo Store` is not retrofitted by the fix** — it has roles (the boot reconciler seeds those for
every tenant) but nobody holding one. The fix changes what happens from here, not what already
happened. Provision a **new** tenant rather than trying to rescue that shell.

## ⚠⚠ THE ONE STEP THAT NEEDS MATT — and it cannot be automated from this repo

`POST /api/v1/tenants` is `[Authorize(Policy = PlatformAdmin)]`, and **`platform-admin` is not a
grantable RBAC permission** — it is not in the catalogue and no role carries it. So no ordinary
portal login can create a tenant, however senior.

⚠⚠ **AND A HAND-MINTED HMAC TOKEN CANNOT DO IT EITHER. Measured, not assumed:**

```
pos.sell    HMAC token → GET /api/v1/stores/1/info            → 200   ← scopes work fine
platform-admin HMAC    → GET /api/v1/platform/billing/catalogue → 403
(no token)             → same endpoint                          → 401
```

The cause is **WP18.1, working exactly as designed**: `plutus-ecosystem.config.js` line 21 sets
`OPERATOR_SSO_ENFORCED: "true"`, and `PlutusTokenAuthHandler.AddScopes` therefore drops
`platform-admin` from any HMAC token. ⚠ **Do not "fix" this by flipping that flag** — it is a live
security control on the login path, and the whole point of it is that a leaked signing secret must
not mint a platform administrator.

⚠ It is **not** the operator boundary, which was the other candidate: `/api/v1/platform` and
`/api/v1/tenants` are both on `OperatorBoundaryMiddleware`'s allow-list, and that middleware writes
a JSON `detail` the 403 above did not carry.

### So: run this from the portal, signed in as yourself

The **Platform** tab renders only when the session carries `platform-admin` (`App.tsx:244`), and you
have been using Platform → Quarantine — so your session has it. In the browser console on
`admin.plutus.huggett.dscloud.me`:

```js
// 1. your own session token — password-mode sessions live here
const s = JSON.parse(localStorage.getItem("plutus.portal.session") || "null");
const token = s?.token;
if (!token) throw new Error(
  "No stored session — this is an OIDC/Keycloak login and its token is in memory. " +
  "Copy the Authorization header from any API call in the Network tab instead.");

// 2. create the tenant
const r = await fetch("/api/v1/tenants", {
  method: "POST",
  headers: { "Authorization": `Bearer ${token}`, "Content-Type": "application/json" },
  body: JSON.stringify({
    name:          "Test Shop",
    plan:          "standard",
    adminEmail:    "test-admin@example.test",
    adminPassword: "CHANGE-ME-to-a-real-password"
  })
});
console.log(r.status, await r.json());
```

It returns `tenantId`, `companyId`, **`storeId`** and `adminUserId`. ⚠ `adminPassword` is a real
credential — PBKDF2-hashed like any other, and nothing here expires on its own.

⚠ **There is no separate "create a store" step.** Provisioning already makes one, address `"Main"`
with `"N/A"` placeholders the tenant is expected to edit.

## Then, as the new tenant's admin

Sign into the portal with `test-admin@example.test`. **They now hold Owner**, which carries every
portal permission — including `portal.tills.enrol`, the one that unblocks creating a till.

⚠ **That scope is why the Owner assignment mattered.** `portal.tills.enrol` is a
`RequireClaim("scope", …)` policy (`IdentityModule:56`), so **a platform admin does not satisfy it**
— only a real grant does. Impersonation would not have rescued it either: an impersonation token
carries the *target's* scopes, and before the fix the target had none.

### 1. A till, and its enrolment code

Portal → **Tills** → add "Test Till 1" against the store provisioning created. ⚠ The code is
single-use and short-lived; `POST /api/v1/tills/{tillId}/enrol-code` mints another.

### 2. Somebody who can actually sell

⚠⚠ **THE STEP THAT GETS FORGOTTEN, AND THE TILL WILL NOT LET ANYONE IN WITHOUT IT.** The roster only
carries staff holding a `pos.*` permission — `TillOperatorsController` skips everyone else,
deliberately: a name on a till with no capability is exposure for nothing.

Portal → **Users** → add a person → give them a role with till permissions (Cashier is enough).

### 3. Something to sell

Items are the portal's (*"Portal decides, till obeys"*). Add a handful under **Inventory**, each with
a barcode and a VAT band, or the till scans into an empty catalogue. ⚠ Check **Company → VAT
periods** too — a tenant with no VAT settings reports oddly, and the till's band names come from the
platform.

### 4. Point the till at it

MAUI: **Settings → Till device → Connection, enrolment & diagnostics**, enter the code.

⚠⚠ **UN-ENROL FROM KAPOW FIRST**, or you are testing whichever tenant it is still enrolled to. The
till-name badge in the app bar (§G80a) is the fastest check — it should read "Test Till 1".

⚠ An **unpackaged** build keeps its data in an ordinary AppData path, separate from any installed
MSIX (runbook), so a test enrolment does not disturb an installed one — but **two unpackaged builds
share that path**, and re-enrolling swaps the tenant for both.

## After testing

`POST /api/v1/tenants/{id}/deletion` schedules removal with a grace period;
`.../deletion/{scheduleId}/cancel` reverses it while the grace lasts. ⚠ Leaving a half-configured
test tenant behind is how **Demo Store** came to exist.
