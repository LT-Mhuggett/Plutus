# WP-SIGNUP — self-serve tenancy, and the DPA that must come with it

**Product:** Plutus platform — the front door
**Author:** Matt Huggett (Leading Talent) with Claude
**Date:** 22 August 2026
**Status:** ⚠ **PLAN ONLY. Nothing here is built.** The DPA sections (§4–§6) were folded in from
Matt's question of 2026-08-22 and are the reason this document exists now rather than later.

> ## ⚠⚠ NOTHING HERE IS IMPLEMENTED — verified against the code 2026-08-22
>
> - **There is no way to sign up.** `TenantLifecycleService` provisions a tenant, but nothing
>   unauthenticated reaches it. Every tenant on the platform was created by hand.
> - **There is no way for a client to accept a DPA.** The only writer is
>   `PUT /api/v1/tenants/{id}/compliance`, gated on `PlatformAdmin` — so today the **operator ticks
>   it on the client's behalf**, which is exactly what Matt objected to.
> - **There is no DPA document.** No text, no version, no store. ⚠ **This is the one thing this plan
>   cannot supply** — see §4.3.
>
> **What DOES exist and is reused rather than rebuilt:** `TenantLifecycleService` (provisioning),
> the `IsSandbox` flag and `resetSandbox`, `Tenant.DataRegion` / `DpaSignedAtUtc` / `DpaRef`,
> the `dpa-missing` signal and its sweep (`CommercialOps.ComplianceSweep`), Keycloak for identity,
> and the audit trail.

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

| # | Stage | Est. | Ships |
|---|---|---|---|
| 1 | **Applications, not tenants** | 1d | A `TenantApplication` row and an unauthenticated `POST`. No tenant is created. |
| 2 | **Prove the email** | 0.5d | A verification link; an application is inert until clicked. |
| 3 | **Abuse controls** | 1d | Rate limit, disposable-domain block, name reservation, an operator queue. |
| 4 | **The DPA gate** | 1.5d | Versioned document, acceptance record, the client-facing screen. |
| 5 | **Provision, sandbox-first** | 1d | Application → tenant via `TenantLifecycleService`, `IsSandbox = true`. |
| — | **The form** | 0.5d | Part of WP-LANDING, which depends on this. |

**≈5–5.5 days**, up from the ≈3–4d in `plutus-platform-architecture.md` §12c — that estimate
predates the DPA work being folded in here.

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

### 4.1 What is wrong today

`PUT /api/v1/tenants/{id}/compliance` is `[Authorize(PlatformAdmin)]`. The operator types a date
into **Platform → Subscribers → the tenant → the compliance box**, and the `dpa-missing` signal
clears. ⚠⚠ **An operator-entered date is evidence of nothing.** It records that Matt believes a DPA
was signed; it does not record that the client agreed to anything.

⚠ And it re-raises for ever while unset: Kapow's `dpa-missing` signal had fired **561 times since
31 July 2026** by the time it was noticed, which is what a permanently-red signal trains people to
ignore.

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

### 4.3 ⚠⚠ THE BLOCKER: there is no DPA text

**This plan builds the mechanism. It cannot write the document.** A Data Processing Agreement is a
legal instrument between Plutus and the customer; its content is Matt's (or his solicitor's) to
supply. Building around a placeholder is fine for stages 1–3, but **shipping a signup that records
acceptance of placeholder text would be worse than having no DPA at all** — it manufactures evidence
that a client agreed to something nobody wrote.

**What is needed to unblock:** the DPA body, and a version string to stamp it with.

### 4.4 Where it appears

1. **In signup** (stage 4, this plan) — the applicant reads and accepts before the application can
   be approved. Recorded against the application, copied onto the tenant at provisioning.
2. **In the portal, for tenants that already exist** — every tenant predating this has no
   acceptance, and Kapow is one. A banner on first sign-in, and a permanent entry under Company.
   ⚠ **Blocking versus nagging is Matt's call** and is deliberately left open here: blocking an
   existing paying customer out of their own till over a document they have not seen is a decision,
   not a default.

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

## 7. Definition of done

- [ ] An unauthenticated `POST` creates an application and nothing else.
- [ ] An unverified application cannot be approved by any route.
- [ ] Rate limits hold under a scripted flood; the test asserts the 429, not just the happy path.
- [ ] A DPA acceptance records version, user, time and IP; publishing a new version re-raises
      `dpa-missing` for tenants that have not accepted it.
- [ ] An operator-recorded DPA is visibly distinct from a client-accepted one, in the API and on screen.
- [ ] Approving provisions exactly one tenant, `IsSandbox = true`, idempotent on the application id.
- [ ] Every state change is audited.
- [ ] ⚠ The signup path is tested with the mail sender **failing** — a bounced verification email
      must leave a recoverable application, not a dead row.

---

## 8. What this does NOT cover

- **WP-LANDING** — the public site. Separate, depends on this, ≈2–3d. See
  `plutus-platform-architecture.md` §12c: a separate app, not a route in the portal or the till, and
  its "login" is a **link** to the two that exist, never a third auth implementation.
- **Billing on signup.** A sandbox tenant pays nothing; taking a card at signup is a different work
  package and a different set of obligations.
- **Self-serve deletion.** ⚠ Deliberately: a tenant that can delete itself can delete its own sales
  history, and default 3 is archive, never delete.
