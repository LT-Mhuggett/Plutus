# WP-SIGNUP — self-serve tenancy, and the DPA that must come with it

> ## 📦 ARCHIVED 2026-08-23 — BUILT, DEPLOYED, and all eight DoD lines closed
>
> **Matt:** *"Can anything 'Not finished' in WP-signup.md be moved to the WP-Landing.md please. Then
> archive the WP-Signup.md."*
>
> Backend **1.31.0**, portal **1.25.0**. Five stages, the operator queue, the client-facing
> acceptance, and a control that was not in the original plan: the front door is **closed by
> default** (`signup.public`, Platform → Flags).
>
> ### ⚠⚠ THE TWO UNFINISHED ITEMS LEFT THIS DOCUMENT RATHER THAN BEING ARCHIVED WITH IT
>
> An open decision inside an archived plan is a decision nobody takes. Both were lifted first — the
> same rule `index.md` states and that `MAUI-retrofit.md` was archived under:
>
> | What | Now lives in | Why there and not the other place |
> |---|---|---|
> | **The DPA text** — parties reversed, 18 placeholders, "for review by a solicitor before use" | [`WP-landing.md`](../To%20do/WP-landing.md) **§0b** | The landing page fronts the signup flow, and that flow cannot COMPLETE without a published agreement. It is that plan's blocking dependency — ⚠ it blocks stage 3 from being *finished*, not from being *built* |
> | **Blocking versus nagging** for tenants that already exist | [`plutus-platform-architecture.md`](../plutus-platform-architecture.md) **§12d** | It is about tenants that already exist, in the portal. A landing page has nothing to do with it, and filing it there would bury it — the same reasoning that moved WP-SL out of the MAUI document |
>
> ⚠ **Nothing else was outstanding.** The three DoD lines this page listed as open on 22–23 August
> are closed, each with the test that earns it (§7).
>
> ### Where to go instead
>
> | You want | Open |
> |---|---|
> | To build the public site | [`WP-landing.md`](../To%20do/WP-landing.md) — and §0b for what blocks it |
> | The blocking-vs-nagging decision | [`plutus-platform-architecture.md`](../plutus-platform-architecture.md) §12d |
> | To open or close signup | **Platform → Flags → `signup.public`**. Default closed; both directions audited |
> | To publish the agreement | **Platform → DPA**. The text is data, not code — no deploy |
> | What was built and why | Everything below, unedited |
>
> ⚠⚠ **DO NOT REOPEN THIS PAGE FOR NEW WORK.** The signup API surface is settled and tested; if it
> needs to grow, that is a new work package with its own name.
>
> ⚠ **One lesson worth carrying out of here.** This document's banner read *"NOTHING HERE IS
> IMPLEMENTED"* for a day after all five stages shipped, and Matt caught it. A status line written in
> prose goes stale silently while the code moves on — which is why every claim in the block below
> names the test or the endpoint that proves it.

---

**Everything below is the document as it stood on 2026-08-23, with its status corrected.**


**Product:** Plutus platform — the front door
**Author:** Matt Huggett (Leading Talent) with Claude
**Written:** 22 August 2026 · **Built:** 23 August 2026
**Status:** ✅ **BUILT AND DEPLOYED** — backend 1.30.2, portal 1.25.0. Five of the eight DoD lines
are met; three are open and named below. ⚠ **Signup cannot complete yet, and that is by design** —
no DPA is published. See §4.3.

> ## ✅ BUILT 2026-08-23 — all five stages, deployed and smoke-tested against the live server
>
> ⚠⚠ **THIS BANNER USED TO SAY "NOTHING HERE IS IMPLEMENTED", AND IT SAID SO FOR A DAY AFTER IT
> STOPPED BEING TRUE.** Matt caught it: *"Right at the start it says 'NOTHING HERE IS IMPLEMENTED —
> verified against the code 2026-08-22' Which is wrong."* It is the same failure `index.md` and
> `MAUI-retrofit.md` §6 both warn about — a status line written in prose goes stale silently while
> the code it describes moves on — and it is why every claim below carries what proves it.
>
> | Stage | State | Proof |
> |---|---|---|
> | **1. Applications, not tenants** | ✅ | A signup writes ONE `TenantApplication`. Pinned by a test asserting **zero** tenants, companies, stores, credentials and role assignments afterwards |
> | **2. Prove the email** | ✅ | Hashed, single-use, 48h. The token is never stored; an unverified application is inert |
> | **3. Abuse controls** | ✅ | Per-IP `enrol` limiter, a per-application send cap the IP limiter cannot see, a config-driven disposable-domain list (subdomains count), and a unique index reserving the folded business name |
> | **4. The DPA gate** | ✅ mechanism · ⛔ **no text published** | Versioned, immutable once published, body SHA-256'd. §4.3 is the blocker and it is Matt's, not code |
> | **5. Provision, sandbox-first** | ✅ | `IsSandbox = true` always, idempotent on the application id |
> | **§6 the operator queue** | ✅ | **Platform → Applications** and **Platform → DPA** |
> | **§4.4(2) existing tenants** | ✅ | **Company → DPA** — the client accepts it themselves |
>
> **Verified against the live server, not just the suites:**
>
> ```
> GET  /api/v1/signup/dpa   → 409  "No data processing agreement has been published yet"
> POST /api/v1/signup       → 202  + application id
> POST /api/v1/signup       → 400  (disposable address)
> POST /api/v1/signup       → 202  same name, different email — byte-identical response
> ```
>
> ⚠ That last one is deliberate: distinguishing "new" from "name taken" would turn an
> unauthenticated endpoint into an oracle for who is a Plutus customer.
>
> ### ✅ ALL EIGHT DoD LINES CLOSED, 2026-08-23 (backend 1.31.0)
>
> ⚠ This block used to list three open lines. They are done, and §7 names the test that earns each:
>
> | Was open | Closed by |
> |---|---|
> | The rate-limit flood test | `A_flood_of_signups_is_throttled_with_429`, plus `SignupRateLimitPolicyTests` — because the flood test **cannot** prove WHICH policy throttled (`PlutusAppFactory` sets `RATE_LIMIT_ENROL_PER_MIN = 100000`), so the attribute is pinned separately |
> | The `dpa-missing` re-raise | `Publishing_a_new_version_re_raises_dpa_missing_at_sweep_level` — runs the real `ComplianceSweep` twice |
> | "Every state change is audited" | Six audit actions, written **inside the services** so the row shares the mutation's `SaveChanges` |
>
> ⚠ **And a control that was NOT in the plan:** the front door is now **closed by default** —
> `signup.public` in Platform → Flags. The signup API was public from the moment stage 1 shipped,
> with no landing page in existence. See §7b.
>
> ### ⛔ What remains — and neither item is code
>
> Both moved out of this document when it was archived, because an open decision in an archived plan
> is a decision nobody takes:
>
> | What | Moved to | Why there |
> |---|---|---|
> | **The DPA text** — parties reversed, 18 placeholders | [`WP-landing.md`](../To%20do/WP-landing.md) §0b | The landing page fronts the signup flow, and the flow cannot COMPLETE without a published agreement. It is that plan's blocking dependency |
> | **Blocking versus nagging** for existing tenants | [`plutus-platform-architecture.md`](../plutus-platform-architecture.md) §12d | It is about tenants that ALREADY EXIST, in the portal. Nothing to do with a landing page — filing it there would bury it |
>
> ### ➖ Out of scope, per §8
>
> **The public form is WP-LANDING** (≈2–3d) — a separate app, not a route in the portal or the till.
> So signup is API-reachable today and has **no page**. Billing on signup and self-serve deletion
> are excluded too, the latter deliberately: a tenant that can delete itself can delete its own
> sales history.
>
> **What was reused rather than rebuilt:** `ProvisioningService`, the `IsSandbox` flag and
> `resetSandbox`, `Tenant.DataRegion` / `DpaSignedAtUtc` / `DpaRef`, the `dpa-missing` signal and its
> sweep, the `enrol` rate-limit policy, `IMessageSender`, Keycloak for identity, and
> `Crockford32`/`CompactToken.Sha256` for tokens.
>
> ### ⚠ Three faults found by checks rather than by review — kept because the shapes recur
>
> - **The live smoke test 500'd:** `ObjectIdMissingException("CurrentUser not defined!")`. Anonymous
>   requests have no audit user and `RepositoryContext` refuses to write without one. ⚠⚠ **The unit
>   tests missed it because their context helper set `CurrentUser = "test"` — the double was better
>   configured than production.** There is now a test that builds the context with none.
> - **A mutation run caught a test passing for the wrong reason.** *"An unpublished draft is neither
>   served nor acceptable"* survived deleting the `PublishedAtUtc` guard entirely, because a draft is
>   not `IsCurrent` either. It now forces the actually-dangerous state: current BUT unpublished.
> - **The DPA seeder raced the auto-applied migration and lost**, so the first deploy created the
>   tables and seeded nothing. Its catch degraded safely by design — but a seeder that only works on
>   the second boot is one nobody can rely on. It retries now.

---

## 0. Why this is not a form

> **Matt, 2026-08-22:** *"I need to be able to add a new tennant, I don't think there is anyway to
> sign up at the moment?"* — and, on the DPA: *"is there somewhere that the end user can sign or
> agree to this? So that I am not 'Ticking it for them?'... Does this need to be part of signing up
> to use the service at the start?"*

⚠⚠ **AN UNAUTHENTICATED ENDPOINT THAT CREATES A TENANT IS AN OPEN DOOR.** It writes rows, sends
mail, and consumes a name in a shared namespace, all before anyone has proved they are a person.
The form is the last day of this work, not the first.

⚠ **The DPA is not a checkbox bolted on afterwards.** A tenant that can process personal data
before accepting the terms under which it may is a tenant whose first act is a compliance gap. That
is the answer to Matt's question: **yes, it belongs in signup**, and §4 says how.

---

## 1. Shape

Five stages, each of which can ship and be tested alone. ⚠ **In this order** — every later stage
assumes the abuse controls of an earlier one.

| # | Stage | Est. | State | Ships |
|---|---|---|---|---|
| 1 | **Applications, not tenants** | 1d | ✅ | A `TenantApplication` row and an unauthenticated `POST`. No tenant is created. |
| 2 | **Prove the email** | 0.5d | ✅ | A verification link; an application is inert until clicked. |
| 3 | **Abuse controls** | 1d | ✅ | Rate limit, disposable-domain block, name reservation, an operator queue. |
| 4 | **The DPA gate** | 1.5d | ✅ mechanism · ⛔ no text published | Versioned document, acceptance record, the client-facing screen. |
| 5 | **Provision, sandbox-first** | 1d | ✅ | Application → tenant via `ProvisioningService`, `IsSandbox = true`. |
| — | **The form** | 0.5d | ➖ | Part of WP-LANDING, which depends on this. **Not built** — signup is API-reachable and has no page. |

~~**≈5–5.5 days**~~ → **built in one day, 2026-08-23**, with ~1–1½d of DoD tail left (§7).

⚠ **The estimate was not wrong so much as differently shaped.** Almost all of it was reused rather
than written: `ProvisioningService` and the Owner-role fix from the day before, the `enrol`
rate-limit policy, `IMessageSender`, `Crockford32`/`CompactToken.Sha256` for tokens, the
`dpa-missing` signal and its sweep, and `Tenant.DataRegion`/`DpaSignedAtUtc`/`DpaRef`. The parts
that took the time were the ones with no precedent to copy — the versioned DPA and its two
distinguishable acceptance routes.

⚠⚠ **AND `ProvisioningService`, NOT `TenantLifecycleService`.** This table named the wrong service:
`TenantLifecycleService` handles status, plans and scheduled deletion; provisioning is
`ProvisioningService`. ⚠ It could not have been called as written on 22 August anyway — provisioning
created a tenant with **no roles and no role assignment**, so every self-serve tenant would have
arrived unusable. That was found and fixed on 2026-08-23 (see `Build/provision-test-tenant.md`);
this plan depended on it without knowing.

---
---

## 2. Stage 1 — an application is not a tenant

⚠⚠ **THE SINGLE MOST IMPORTANT DECISION IN THIS PLAN.** A signup writes a `TenantApplication`, and
nothing else. No tenant, no Keycloak realm entry, no schema, no rows in any table a live tenant
touches.

**Why:** everything that makes self-serve dangerous — abuse, duplicates, spam, a name someone else
wanted — becomes a row an operator can delete instead of a tenant somebody has to unpick. Provisioning
is a **decision**, taken in stage 5, against an application that has already earned it.

```
TenantApplication
  Id, BusinessName, ContactName, ContactEmail, Phone?
  RequestedRegion            -- feeds Tenant.DataRegion
  Status                     -- Pending | EmailVerified | Approved | Provisioned | Rejected
  EmailVerifyTokenHash, EmailVerifiedAtUtc
  DpaVersionAccepted?, DpaAcceptedAtUtc?, DpaAcceptedByEmail?, DpaAcceptedIp?
  CreatedAtUtc, CreatedFromIp
  ProvisionedTenantId?, RejectedReason?
```

⚠ **`EmailVerifyTokenHash`, never the token.** A leaked applications table must not be a set of
working verification links.

---

## 3. Stages 2–3 — proving a person, and surviving a bot

### 3.1 Email verification
An application is **inert** until the link is clicked: it cannot be approved, cannot be provisioned,
and does not appear in the operator's default queue. ⚠ Single-use, hashed, and **expiring** — 48h.

### 3.2 The abuse controls, and why each is here

| Control | Stops |
|---|---|
| **Per-IP and per-email rate limit** | The obvious flood. ⚠ Both: one IP with many emails, one email retried from many IPs. |
| **Disposable-domain blocklist** | Verification becoming a formality. ⚠ A list, not a regex — and it will need updating, so it is config, not code. |
| **Business-name reservation** | Two applications claiming one name, resolved by whoever is provisioned first. |
| **An operator queue** | ⚠ **The backstop for everything above.** No self-serve tenant goes live unseen — see §6. |

⚠ **No CAPTCHA in v1.** It buys less than the rate limit plus verification, and it puts a
third-party script on the front door of a payments product. Revisit if the queue fills with junk.

---

## 4. Stage 4 — the DPA · ⚠ **the part Matt asked for**

> **Matt:** *"is there somewhere that the end user can sign or agree to this? So that I am not
> 'Ticking it for them?' This needs to be in the portal."*

### 4.1 What was wrong — the problem this section existed to fix

`PUT /api/v1/tenants/{id}/compliance` is `[Authorize(PlatformAdmin)]`. The operator types a date
into **Platform → Subscribers → the tenant → the compliance box**, and the `dpa-missing` signal
clears. ⚠⚠ **An operator-entered date is evidence of nothing.** It records that Matt believes a DPA
was signed; it does not record that the client agreed to anything.

⚠ And it re-raises for ever while unset: Kapow's `dpa-missing` signal had fired **561 times since
31 July 2026** by the time it was noticed, which is what a permanently-red signal trains people to
ignore.

> ✅ **STILL TRUE OF THE OLD ROUTE, WHICH IS WHY IT WAS KEPT RATHER THAN DELETED — see §4.2.**
> `PUT /api/v1/tenants/{id}/compliance` is untouched: some clients sign on paper and an operator
> must be able to record that. What changed is that it can no longer MASQUERADE as the client's own
> act. ⚠ And Kapow still has no acceptance — it can now give one itself from Company → DPA, once a
> DPA exists to accept (§4.3).

### 4.2 What replaces it

**A versioned document, an acceptance record, and a gate.**

```
DpaDocument       Version (e.g. "2026-01"), Title, BodyMarkdown, PublishedAtUtc, IsCurrent
DpaAcceptance     TenantId, Version, AcceptedByUserId, AcceptedByEmail,
                  AcceptedAtUtc, AcceptedIp, UserAgent
```

⚠⚠ **VERSIONED, AND THE VERSION IS PART OF THE ACCEPTANCE.** Accepting `2026-01` is not accepting
`2027-04`. Publishing a new current version must re-raise `dpa-missing` for every tenant that has
not accepted *that* version — otherwise a re-issued DPA is silently treated as already agreed, which
is worse than never having asked.

⚠ **IP and user-agent are recorded because this is evidence.** A dispute about whether a DPA was
accepted is settled by who, when, and from where.

⚠ **The operator field survives as a SECOND, LABELLED route** — some clients will sign on paper.
It becomes `AcceptedByEmail = null, RecordedByOperator = <operator>`, and the portal shows it as
*"recorded manually by …"*, never as *"accepted by the client"*. **Do not delete it and do not let
it masquerade as the client's own acceptance.**

### 4.3 ⛔⛔ THE BLOCKER: the DPA text — supplied 2026-08-23, and **not usable as it stands**

**This plan builds the mechanism. It cannot write the document.** A Data Processing Agreement is a
legal instrument between Plutus and the customer; its content is Matt's (or his solicitor's) to
supply. Building around a placeholder is fine for stages 1–3, but **shipping a signup that records
acceptance of placeholder text would be worse than having no DPA at all** — it manufactures evidence
that a client agreed to something nobody wrote.

> ⚠ **UPDATE 2026-08-23 — Matt supplied `Build/To do/Data_Processing_Agreement_Leading_Talent.docx`.**
> It was read rather than wired in, and it cannot be published for two reasons.

**1. ⚠⚠ THE PARTIES ARE THE WRONG WAY ROUND.** The document names *"LEADING TALENT [LIMITED] (the
**Controller**)"* and `[PROCESSOR NAME]` as the Processor. That is the shape for Leading Talent
**engaging a supplier** — a payroll bureau, say. In Plutus signup the relationship is the other way:

| | Who | Why |
|---|---|---|
| **Controller** | **the shop** | It decides why and how its customers' and staff's personal data is processed |
| **Processor** | **Plutus / Leading Talent** | It processes that data on the shop's documented instructions |

Published unchanged it would ask every shop to agree that Leading Talent controls their data and
that they process it — the opposite of the truth, and worth nothing if it were ever relied on.

**2. It is an unfilled template.** Its own first page says *"TEMPLATE — for review by a qualified
solicitor before use"*, and **18 distinct `[…]` placeholders** remain: company number, registered
address, the date, the breach-notification window (`[24/48]` hours), the audit notice period, the
retention period, the whole of Schedule 3's sub-processor list, and an unmade either/or on whether
sub-processor authorisation is *specific* or *general*.

**Where it is now:** seeded as **`DRAFT-2026-08`** — 15,664 bytes, `PublishedAtUtc` NULL,
`IsCurrent` 0 — so it is readable and editable in **Platform → DPA**. ⚠ A draft is never served to
an applicant and can never be accepted: `GetCurrentAsync` filters on published, `AcceptAsync` throws
409, and `/api/v1/signup/dpa` answers 409 with the reason. Both `DpaSeeder` and the seeded row's
internal note carry these two blockers.

**What is needed to unblock — and none of it is engineering.** Correct the parties, fill the
brackets, have a solicitor read it, then save it as a **new version** in Platform → DPA and publish
that. ⚠ The text is **data, not code**: no deploy, no release, and each revision keeps its own
acceptance records.

### 4.4 Where it appears

1. **In signup** (stage 4, this plan) — the applicant reads and accepts before the application can
   be approved. Recorded against the application, copied onto the tenant at provisioning.
2. **In the portal, for tenants that already exist** — every tenant predating this has no
   acceptance, and Kapow is one. ✅ **BUILT: Company → DPA**, which opens itself when something is
   outstanding. ⚠ It is a self-opening panel rather than the global banner this line first imagined —
   a banner on a page the owner may not visit is a banner that gets scrolled past.
   ⚠⚠ **Blocking versus nagging is still open, and it MOVED when this plan was archived** — it is
   now `plutus-platform-architecture.md` **§12d**, because it is a decision about tenants that
   already exist and would have been buried in a landing-page plan. **Nagging ships.**

---

## 5. Stage 5 — provisioning, sandbox-first

Approving an application calls the existing `TenantLifecycleService`. ⚠⚠ **`IsSandbox = true`,
always.** A self-serve tenant that starts live is one taking real money before anyone has looked at
it. The Demo Store row already carries this flag, so the path is proven.

Promotion out of sandbox stays an operator action.

⚠ Provisioning must be **idempotent on the application id** — a double-clicked Approve must not
produce two tenants, and the operator queue is exactly where a double click happens.

---

## 6. What the operator sees

A **Platform → Applications** screen, beside Subscribers:

- the queue, newest first, filtered by status
- what they typed, when, from what IP, whether the email is verified
- **whether the DPA was accepted, which version, by whom**
- **Approve** (→ provision, sandbox) and **Reject** (with a reason, which is emailed)

⚠ It follows **Platform → Quarantine** (built 2026-08-22) as the model: a list, a reason, an action,
and a note recorded against whoever took it.

---

## 7. Definition of done — ✅ **ALL EIGHT, 2026-08-23**

⚠ Ticked only where a test proves it, and each line names the test. Two of the three that were open
were missing TESTS rather than missing behaviour; the third was missing behaviour and said so.

- [x] An unauthenticated `POST` creates an application and nothing else.
      → `Applying_creates_an_application_and_absolutely_nothing_else` asserts **zero** tenants,
      companies, stores, credentials and role assignments after an apply.
- [x] An unverified application cannot be approved by any route.
      → `An_unverified_application_cannot_be_approved` (409, and no tenant afterwards).
- [x] Rate limits hold under a scripted flood; the test asserts the 429, not just the happy path.
      → `A_flood_of_signups_is_throttled_with_429` (40 concurrent, asserts a 429 **and** that
      requests still get through — without the second half a broken endpoint would look throttled).
      ⚠⚠ **AND IT DOES NOT PROVE WHICH POLICY THROTTLED.** `PlutusAppFactory` sets
      `RATE_LIMIT_ENROL_PER_MIN = 100000` ("don't throttle the test IP"), so in that host the enrol
      window is off and the 429 comes from the per-tenant limiter. Found by reading the factory, not
      by the test failing. The specific control is pinned separately by
      `SignupRateLimitPolicyTests`, which asserts `[EnableRateLimiting("enrol")]` sits at **class**
      level so new actions inherit it.
- [x] A DPA acceptance records version, user, time and IP …
      → `Approving_provisions_one_sandbox_tenant_with_the_dpa_carried_across` checks all four survive
      onto the tenant.
- [x] … **publishing a new version re-raises `dpa-missing`** for tenants that have not accepted it.
      → `Publishing_a_new_version_re_raises_dpa_missing_at_sweep_level` runs the real
      `ComplianceSweep` twice: clear while up to date, raised after a newer version is published.
      ⚠ The earlier `StatusAsync` test only proved the version comparison; that is not the same as
      proving the signal fires.
- [x] An operator-recorded DPA is visibly distinct from a client-accepted one, in the API and on screen.
      → `An_operator_recorded_dpa_is_distinguishable…` plus
      `The_client_route_and_the_operator_route_audit_differently` — two different audit ACTIONS
      (`dpa.accepted` vs `dpa.recorded-manually`), because a trail that flattens them has destroyed
      the distinction this work package exists to make.
- [x] Every state change is audited.
      → `Approving_and_rejecting_are_audited` reads the `AuditLogs` table for
      `signup.application.approved`, `signup.application.rejected`, `signup.dpa.accepted` and
      `dpa.published`. ⚠⚠ **The writes live in the SERVICES, not the controllers**, because
      `AuditExtensions.Audit` only ADDS to the change tracker — *"callers save it in the SAME
      SaveChanges as the mutation, so the trail can never disagree with the data."* Auditing from a
      controller after the service returned would leave the row unsaved: the mutation lands and its
      record does not, which is worse than no audit at all.
- [x] ⚠ The signup path is tested with the mail sender **failing** — a bounced verification email
      must leave a recoverable application, not a dead row.
      → `A_failing_mail_sender_leaves_a_recoverable_application` exercises **both** failure shapes
      (a sender that throws and one that refuses), then proves the token still works.

### ⚠⚠ 7b. THE FRONT DOOR IS CLOSED BY DEFAULT — added 2026-08-23, and it was not in the plan

> **Matt:** *"I would like to build out the landing page, but not have it externally facing for now.
> Can I have this as an operator portal setting? Or does it have to be done via Caddy?"*

**It can be a portal setting, it should be, and it had to be — because the API was already public.**

⚠⚠ **THE PREMISE NEEDED CORRECTING FIRST.** "Not externally facing" was already untrue of the API the
moment stage 1 shipped. Caddy proxies `/api/*` on `plutus.huggett.dscloud.me` straight to the
backend and the signup routes are `[AllowAnonymous]`, so a POST from outside created an application
on 2026-08-23 — **with no landing page in existence**. Waiting for WP-LANDING to think about exposure
would have been waiting for the wrong event.

| Control | What it gates | Why it is the wrong/right tool |
|---|---|---|
| **`signup.public` flag** (Platform → Flags) | **the API** | ✅ The right tool. One switch, audited, no deploy, no SSH, visible where the rest of the platform's switches live |
| **Caddy** | **the page** (a future landing vhost) | ✅ For the page — simply not adding the vhost keeps it unreachable. ⛔ **Wrong for the API**: the till, web till and portal all share `/api/*` on that host, so gating signup means a path carve-out, in a file needing SSH to change, invisible from the portal, and a second place to remember |

⚠ **CLOSED IS THE DEFAULT, AND IT IS THE OPPOSITE OF EVERY OTHER `PlatformFlag`.** The rest are kill
switches: absent means the feature works. A front door read that way is OPEN until somebody
remembers to close it — the exact state this ended. **Absent means closed**; the flag must be created
and enabled deliberately.

⚠ **A closed door answers 404, not 403.** A 403 tells a stranger a signup API exists and is merely
switched off, which is an invitation to come back later.

Pinned by `With_the_flag_off_every_signup_route_is_404`, `A_closed_door_writes_nothing` (a 404 that
still recorded the attempt would be a closed door with an open window), and
`Every_signup_action_checks_the_open_closed_flag`, which COUNTS the gates against the actions —
a gate on three of four routes is an open route, and the one left out will be the newest.

**To open it:** Platform → Flags → `signup.public` → enable. **To close it:** disable. Both audited.

---
---

## 8. What this does NOT cover

- **WP-LANDING** — the public site. Separate, depends on this, ≈2–3d. See
  `plutus-platform-architecture.md` §12c: a separate app, not a route in the portal or the till, and
  its "login" is a **link** to the two that exist, never a third auth implementation.
- **Billing on signup.** A sandbox tenant pays nothing; taking a card at signup is a different work
  package and a different set of obligations.
- **Self-serve deletion.** ⚠ Deliberately: a tenant that can delete itself can delete its own sales
  history, and default 3 is archive, never delete.
