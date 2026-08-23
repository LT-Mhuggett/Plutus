# Provisioning a second tenant, to test a till against something that isn't Kapow

> **Matt, 2026-08-23:** *"I then need to test MAUI with a different tennant to Kapow"*
>
> ⚠⚠ **DO NOT TEST AGAINST KAPOW.** It is a live shop with real sales, real staff and a real VAT
> position. Every basket rung up on it lands in `salesv2` and on a return. That is the whole reason
> for this document.

## What exists today

| Tenant | State |
|---|---|
| **Kapow Comics Ltd** | ⚠ **LIVE.** Not for testing. |
| **Demo Store** | Flagged `IsSandbox`, and **an empty shell** — checked 2026-08-23: **0** stores, **0** tills, **0** employees, **0** items, **0** role assignments. A tenant row and nothing else. |

So Demo Store cannot be enrolled against as it stands: a till needs a **till record** to get an
enrolment code, and an operator needs a **role assignment** to sign in. Since 2026-08-23 there is no
fallback — the legacy local login is gone and *"a till needs to enrol and sync first"* (Matt).

## ⚠ Why this is a document and not a script I ran

Provisioning writes to the **live platform**: a tenant, an admin account with a password, a store, a
till. It is not reversible from the portal — tenant deletion is a *scheduled* operation with a grace
period (`POST /api/v1/tenants/{id}/deletion`). That is Matt's call to make and Matt's credentials to
make it with; a platform-admin token is not something this repo holds.

## The chain

Every step is an existing endpoint. `$TOKEN` is a **platform-admin** bearer token — the one the
portal already uses when signed in as an operator.

```bash
API=https://plutus.huggett.dscloud.me
AUTH="Authorization: Bearer $TOKEN"
JSON="Content-Type: application/json"
```

### 1. The tenant

```bash
curl -sX POST "$API/api/v1/tenants" -H "$AUTH" -H "$JSON" -d '{
  "name":          "Test Shop",
  "plan":          "standard",
  "adminEmail":    "test-admin@example.test",
  "adminPassword": "<a real password — this account can sign in>"
}'
```

⚠ **`adminPassword` is a real credential.** It is PBKDF2-hashed like any other and the account can
sign into the portal. Use something you would be content to have on a test tenant for months,
because nothing here expires on its own.

### 2. A store

```bash
curl -sX POST "$API/api/v1/stores" -H "$AUTH" -H "$JSON" -d '{
  "name":"Test Shop — Counter","adLine":"1 Test Street","city":"Testville",
  "postCode":"TE1 1ST","country":"GB","contactNumber":"-"
}'
```

Note the **numeric `storeId`** it returns — the next step needs it.

### 3. A till, and its enrolment code

```bash
curl -sX POST "$API/api/v1/tills" -H "$AUTH" -H "$JSON" \
     -d '{"storeId": <storeId>, "name":"Test Till 1"}'
```

Returns `tillId` and **`enrolmentCode`**. ⚠ The code is single-use and short-lived; if it lapses,
`POST /api/v1/tills/{tillId}/enrol-code` mints another.

### 4. Somebody who can actually sell

⚠⚠ **THIS IS THE STEP THAT GETS FORGOTTEN, AND THE TILL WILL NOT LET ANYONE IN WITHOUT IT.** The
roster only carries staff holding a `pos.*` permission — `TillOperatorsController` skips everyone
else, deliberately, because a name on a till with no capability is exposure for nothing.

In the portal, signed in as the new tenant's admin: **Users → add a person → give them a role that
includes till permissions** (Cashier is enough). Then, on the till, the sign-in errors are precise
about which of the four things is wrong — *"this till isn't connected yet"*, *"nobody is assigned to
this till"*, *"can't reach Plutus"*, or *"that account isn't on this till's staff list"*.

### 5. Something to sell

Items are the portal's (*"Portal decides, till obeys"*). Add a handful under **Inventory**, each with
a barcode and a VAT band, or the till will scan into an empty catalogue.

⚠ Check **Company → VAT periods** too: a tenant with no VAT settings reports oddly, and the till's
band names come from the platform.

### 6. Point the till at it

On the MAUI till: **Settings → Till device → Connection, enrolment & diagnostics**, enter the
enrolment code from step 3.

⚠⚠ **UN-ENROL THE TILL FROM KAPOW FIRST**, or you will be testing whichever tenant it is still
enrolled to. The Plutus tab shows which till it thinks it is; **§G80a's till-name badge in the app
bar is the fastest check** — it should read "Test Till 1", not a Kapow till.

⚠ An **unpackaged** build keeps its data in an ordinary AppData path, separate from any installed
MSIX (runbook). So a test enrolment on the unpackaged build does not disturb an installed one — but
two unpackaged builds DO share that path, and re-enrolling swaps the tenant for both.

## After testing

The tenant does not clean itself up. `POST /api/v1/tenants/{id}/deletion` schedules removal with a
grace period, and `.../deletion/{scheduleId}/cancel` reverses it while the grace lasts. ⚠ Leaving a
half-configured test tenant in place is how the **Demo Store** shell above came to exist.
