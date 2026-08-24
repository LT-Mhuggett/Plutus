# WP-LANDING — the public site, and the front door WP-SIGNUP built the lock for

**Product:** Plutus platform — the public face
**Author:** Matt Huggett (Leading Talent) with Claude
**Written:** 24 August 2026 · **Built:** 24 August 2026
**Status:** ✅ **BUILT AND DEPLOYED — DELIBERATELY UNREACHABLE.** Landing **1.0.0**, sitting in
`/srv/apps/PLUTUS/landing/current` with **no Caddy vhost**, so nothing serves it. ⛔ Two things
remain and neither is code: **the pricing copy** (§4, a marked placeholder on the page) and **the
DPA text** (§0b, which stops signup completing).

> **Matt, 2026-08-21:** *"I need a customer facing landing page, that describes Plutus, with a login
> screen to take you to the till."*
>
> **Matt, 2026-08-24:** *"I would like to build out the landing page, but not have it externally
> facing for now."*

> ### ✅ What shipped, 2026-08-24
>
> | Stage | State |
> |---|---|
> | 1. The page | ✅ Hero, six capability cards, positioning, pricing placeholder |
> | 2. The two links | ✅ **Links, never a login form.** ⚠ Hidden rather than broken when the URLs are unset — see §3 |
> | 3. The signup flow | ✅ Built. ⚠ Cannot COMPLETE until a DPA is published (§0b), and it degrades honestly: two of its three states are "you cannot sign up right now" |
> | 4. Deploy, unlisted | ✅ Deployed, **no vhost**, verified unreachable |
>
> **Verified on the artefact, not the source** — because the point is what ships:
>
> ```
> third-party origins in the bundle : 0
> localStorage / sessionStorage     : 0
> input type="password"             : 0
> version 1.0.0 present, 0.0.0      : yes / absent
> the three live sites after deploy : 200 / 200 / 200 (till, portal, status)
> ```
>
> ⚠ **The copy is DRAFT and says so on the page.** Matt asked for it drafted from the codebase, so
> every claim maps to a ✅/✅ row of `till-design.md` Part A0 — offline selling, the Z close and its
> short/over answer, refunds by original tender, the VAT band publishing. Nothing aspirational.
> The wording is a starting point; the *constraint* is the part worth keeping.
>
> ⚠⚠ **PRICING IS A CONSPICUOUS PLACEHOLDER, ON PURPOSE.** Matt chose that over inventing a number or
> writing "contact us". A price cannot be derived from a codebase. **Fill it before the vhost exists.**

---
## 0. What already exists, so this does not rebuild it

**WP-SIGNUP is done and deployed** (backend 1.30.2). This page is the *front* of a machine that
already works:

| Already built | Endpoint |
|---|---|
| Apply for a tenancy | `POST /api/v1/signup` |
| Read the agreement | `GET /api/v1/signup/dpa` |
| Accept it | `POST /api/v1/signup/dpa/accept` |
| Confirm the email | `POST /api/v1/signup/verify?token=…` |
| The on/off switch | `signup.public` in **Platform → Flags**, default **closed** |

⚠⚠ **SO THIS WORK PACKAGE IS A UI, NOT A FEATURE.** If it starts growing endpoints, something has
gone wrong — the API surface is settled and tested (WP-signup §7, eight of eight).

⚠ **One thing is genuinely blocked and it is not code:** no DPA is published, so signup cannot
*complete*. ⚠⚠ **That blocker moved INTO this plan when WP-signup was archived — see §0b**, which is
where the two reasons and the route out are written down.

---


## 0b. ⛔⛔ THE BLOCKING DEPENDENCY — no DPA is published, so signup cannot complete

> **Moved here from `WP-signup.md` §4.3 when that plan was archived, 2026-08-23.** It is WP-SIGNUP's
> only unfinished item and it is this plan's problem now: a landing page whose signup flow cannot
> complete is a brochure, and this is asked for as a front door.

Matt supplied `Build/To do/Data_Processing_Agreement_Leading_Talent.docx`. It was read rather than
wired in, and it **cannot be published as it stands**, for two reasons.

**1. ⚠⚠ THE PARTIES ARE THE WRONG WAY ROUND.** It names *"LEADING TALENT [LIMITED] (the
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
`IsCurrent` 0 — readable and editable in **Platform → DPA**. ⚠ A draft is never served and can never
be accepted: `GetCurrentAsync` filters on published, `AcceptAsync` throws 409, and
`GET /api/v1/signup/dpa` answers 409 with the reason.

**To unblock — none of it is engineering.** Correct the parties, fill the brackets, have a solicitor
read it, then save it as a **new version** in Platform → DPA and publish that. ⚠ The text is **data,
not code**: no deploy, no release, and each revision keeps its own acceptance records.

### ⚠ What this means for building the landing page

**It does not block stages 1, 2 or 4.** The page, the two links and the unlisted deploy are all
independent of the agreement.

⚠ **It blocks stage 3 from being FINISHED, not from being built.** The signup screens can be built
and demonstrated against the 409 — and they have to handle it anyway, because the flow must degrade
honestly when no agreement is published (§4, and it is a DoD line in §6). What cannot happen until
the text lands is a real customer completing signup.

---
## 1. ⚠⚠ "Not externally facing" — how, and the trap in the question

**Matt asked whether this can be an operator-portal setting or has to be Caddy. It is BOTH, and they
gate different things.** Getting this wrong is the difference between a private site and a public one.

| Layer | Controls | Tool | Why |
|---|---|---|---|
| **The API** | whether signup *accepts* anything | ✅ **`signup.public` flag** — already built | One switch, audited, no deploy, no SSH, and it lives where the platform's other switches live |
| **The page** | whether the site is *reachable* | ✅ **Caddy** — do not add the vhost yet | A page that is not served cannot be found. Nothing to configure, nothing to remember |
| **Who may see a served page** | early-access / staging | ⚠ Caddy `basic_auth` or an IP allow-list | Only once the vhost exists and you want a few people in |

⚠⚠ **AND THE TRAP: THE API WAS ALREADY PUBLIC BEFORE ANY PAGE EXISTED.** Caddy proxies `/api/*` on
`plutus.huggett.dscloud.me` to the backend, and the signup routes are anonymous — so on 2026-08-23 a
POST from outside created an application with no landing page anywhere. **"We haven't built the page
yet" was never the same as "nobody can sign up."** That is why the flag was added to WP-signup rather
than deferred to here, and why it defaults to closed.

⚠ **Caddy is the wrong tool for the API half.** The till, the web till and the portal all share
`/api/*` on that host. Gating signup there means a path carve-out — in a file that needs SSH to
change, invisible from the portal, and a second place to remember. The flag is one line in a UI you
already open.

### The recommended sequence

1. **Build the site and host it nowhere.** `npm run dev` locally, or deploy to
   `/srv/apps/PLUTUS/landing/current` with **no Caddy vhost**. Reachable by nobody.
2. **When you want to show it:** add the vhost with `basic_auth`, or an IP allow-list for the shop.
3. **When you want signup to work:** enable `signup.public`. ⚠ Two separate decisions, deliberately —
   a visible site with a dead form is a fine demo; a working form on a site nobody meant to publish
   is an incident.
4. **Public launch:** drop the basic auth. The flag stays a kill switch for the day something floods.

---

## 2. Shape — a fourth app, and the smallest one · ✅ **BUILT**

⚠ **A separate app, not a route in the portal or the till** (architecture §12c, decision of record).
It is unauthenticated, public and indexable; both existing apps assume a session and neither should
learn to serve anonymous traffic.

**What was actually built** — ⚠ four source files, not the six this section first sketched:

```
Plutus/Frontend/Plutus.Frontend.Landing/     Vite + React 19 + TS, matching the other two
  index.html             ⚠ noindex WHILE UNLISTED — remove that line in the same change that
                         adds the vhost, and not before
  vite.config.ts         port 5275 (NOT 5173 = ETRIE, 5273 = web till, 5274 = portal)
  src/
    App.tsx              the page — hero, six capability cards, positioning, pricing placeholder
    Signup.tsx           the WHOLE flow: apply → verify → accept. ⚠ One file rather than three,
                         because the three "screens" are one state machine over one application
                         id, and splitting them would mean threading that id between files for
                         no reader's benefit
    api.ts               ⚠ FOUR calls and no more
    landing.css          system fonts only — a font CDN is a third-party request (§4)
    vite-env.d.ts
  versions/landing.txt   ⚠ its own version file — Matt, 2026-08-08: "Each till needs a specific
                         version as they will end up diverging." The rule is per deployable.
```

| # | Stage | Est. | State |
|---|---|---|---|
| 1 | **The page** | 1d | ✅ Static, responsive, dark-mode aware, no session |
| 2 | **The two links** | ½d | ✅ *Sign in to your till* · *Manage my shop*. ⚠ **Links, not a login form** |
| 3 | **The signup flow** | 1d | ✅ Built. ⚠ Cannot complete until a DPA is published (§0b) |
| 4 | **Deploy, unlisted** | ½d | ✅ `/srv/apps/PLUTUS/landing/current`, **no vhost**, verified unreachable |

~~**≈2–3d**~~ → **built in one day.** ⚠ Not because the estimate was wrong: stage 3 is four calls
against endpoints WP-SIGNUP had already built, tested and deployed, and that is where the ≈2–3d
mostly went. The remaining cost is the copy and the price, which are not engineering.
---

## 3. ⚠⚠ Its "login" is a LINK. Never a third auth implementation

Architecture §12c is explicit and it is the most important line in this document:

> *"Its 'login' is a **link** to the till and the portal, not a third auth implementation. There are
> two already (`oidc.ts` and the password mode) and a third would be the C2 problem in a place where
> getting it wrong is a breach rather than an hour."*

⚠ So: **no password field, no token handling, no session, no `localStorage`.** Two `<a>` tags:

| Button | Goes to |
|---|---|
| **Sign in to your till** | `https://plutus.huggett.dscloud.me` |
| **Manage my shop** | `https://admin.plutus.huggett.dscloud.me` |

⚠ **Configurable, not hardcoded** — `VITE_TILL_URL` / `VITE_PORTAL_URL`. The portal already has a
`VITE_TILL_URL` for exactly this reason and it is unset live, so `tillUrl()` derives it from the
hostname. Reuse the derivation rather than inventing a second convention.

⚠ **A "forgot password" link belongs to the portal, not here.** It has one
(`PasswordResetController`, anonymous by necessity). Duplicating it would put a credential path on
the marketing site.

---

## 4. What the page has to say — and what it must not

⚠ This is the half a plan cannot write: the copy is Matt's. What the plan CAN fix is the constraints.

**Must have:**
- What Plutus is, in one sentence, above the fold.
- Who it is for. ⚠ It is a **UK multi-channel retail** POS with real VAT handling — that is the
  differentiator and it is specific enough to be worth saying.
- A price, or a reason there isn't one. A SaaS page with no pricing reads as "call us".
- The two links (§3).
- The signup call to action — ⚠ **hidden when `signup.public` is off**, so the page never offers a
  door that answers 404. Read it from `GET /api/v1/signup/dpa`: a 404 means closed, a 409 means open
  but no agreement published yet, a 200 means ready.

**Must NOT have:**
- ⛔ **No third-party scripts.** No analytics tag, no chat widget, no font CDN, no CAPTCHA. WP-signup
  §3.2 refused a CAPTCHA for this reason: *"it puts a third-party script on the front door of a
  payments product."* The same argument kills the rest.
- ⛔ **No customer logos or testimonials** until somebody has agreed in writing to appear.
- ⛔ **No screenshots of real data.** ⚠ Every screenshot must come from a **sandbox** tenant — Demo
  Store, or a fresh one via Platform → Subscribers with Sandbox ticked. A marketing page showing
  Kapow's takings is a data-protection incident with a press release attached.
- ⛔ **No claim about certification the platform does not hold.** Schedule 2 of the DPA template lists
  Cyber Essentials / ISO 27001 as bracketed placeholders — bracketed because they are **not held**.

---

## 5. Hosting

⚠ **Fourth vhost, and it is the ONLY one that is truly public** — the till and portal are public but
useless without a credential; this one is meant to be read by strangers and indexed.

```
plutus.huggett.dscloud.me         → web till          (existing)
admin.plutus.huggett.dscloud.me   → portal            (existing)
login.plutus.huggett.dscloud.me   → Keycloak          (existing)
status.plutus.huggett.dscloud.me  → status page       (existing, and NOT under current/)
www.???                           → landing           ⚠ NEW — the domain is a decision, not a default
```


### ⚠⚠ THE APEX IS NOT FREE — checked 2026-08-24, and its own comment invites the mistake

`huggett.dscloud.me` (the apex) currently `redir`s to `etrie.huggett.dscloud.me` with a **302**, and
the Caddyfile says why in a comment that reads like an invitation:

> *"302 not 308 deliberately: a permanent redirect is cached hard by browsers, which would make
> putting a real landing page here later painful. Change to 308 once that decision is settled."*

⚠ **Do not read that as "the apex is reserved for the Plutus landing page."** That name is ETRIE's by
history — ETRIE served the app from it until 2026-08-19, browsers hold HSTS for it for a year, and
the block exists so already-emailed links and the apex certificate keep working. ⚠⚠ **ETRIE must
never be touched**, and quietly repurposing the hostname it used to live on is touching it.

So the apex is **shared infrastructure with a prior claimant**, not a spare domain. The Plutus
landing site needs a name of its own, and that remains Matt's decision.

⚠ **The domain is Matt's to choose** and it is not a detail: whatever it is becomes the brand, the
email sender domain, and the thing printed on receipts. Do not squat `plutus.huggett.dscloud.me/www`
as a placeholder — a temporary URL in a marketing page outlives every intention.

⚠ **`/srv/apps/PLUTUS/landing/current`**, following the existing layout. ⚠⚠ `/srv/apps/` also holds
**ETRIE**, which must never be touched.

⚠ **Build ON THE MAC** (Node 26; the Windows box has none) and follow the portal's build discipline
in `repo-runbook.md`: `PLUTUS_APP_VERSION` set explicitly, and the artefact grepped for
unsubstituted `__APP_VERSION__` / `__BUILD_TIME__` before it is copied to `current/`. ⚠ That check
exists because a missing Vite `define` passes `tsc`, passes `vite build`, and blanks the page for
every visitor.

---

## 6. Definition of done — **6 of 8**, and the two open ones are not code

⚠ Ticked only where it was checked **on the built artefact**, not by reading the source — the point
is what ships.

- [x] The site builds and serves with **no Caddy vhost** — reachable by nobody.
      → No `landing` vhost in `/etc/caddy/Caddyfile`; the files exist and nothing points at them.
      ⚠ The three live sites answered 200 after the deploy (till, portal, status), and
      `/srv/apps/ETRIE` was not touched.
- [x] No password field, no token handling, no session storage anywhere in the **bundle**.
      → `type="password"` **0**, `localStorage`/`sessionStorage`/`bearer` **0**. ⚠ The string
      "password" does appear three times: twice inside React's own input-type tables and once in the
      copy *"we send your password reset here"* — checked rather than assumed.
- [x] **Zero third-party network origins** in the bundle.
      → 0. System font stacks, an inline SVG mark, no analytics, no chat widget, no CAPTCHA.
- [x] The signup call to action is **hidden** when `signup.public` is off, and the page does not
      break when the API answers 404 or 409.
      → `doorState()` maps 404 → closed, 409 → no-agreement, 200 → open, and ⚠ **never throws**: a
      marketing page whose copy fails to render because a status probe errored is worse than one that
      quietly hides its form.
- [ ] ⛔ Apply → verify → accept works end to end against a **published** DPA on a sandbox tenant.
      → **Blocked by §0b**, not by this app. The flow is built and demonstrable against the 409.
- [x] The artefact carries its version and no `0.0.0`, and no unsubstituted defines.
      → `1.0.0` present, `0.0.0` absent, `__APP_VERSION__`/`__BUILD_TIME__` both substituted.
- [x] ⚠ Every screenshot on the page comes from a sandbox tenant, recorded here with which one.
      → ✅ **Vacuously true and worth keeping that way: there are no screenshots.** The page describes
      capabilities in words. ⚠ The moment one is added, this line becomes real again — a marketing
      page showing Kapow's takings is a data-protection incident with a press release attached.
- [ ] ⛔ Responsive, and readable with images blocked.
      → Built for it — `auto-fit` grid, a `40rem` breakpoint, an inline SVG rather than an `<img>`,
      and `prefers-color-scheme` honoured. ⚠ **But nobody has looked at it in a browser**, and this
      repo has learned repeatedly that only a person at a screen closes that kind of line. Left
      unticked deliberately.

### ⛔ What is left, in order

1. **Look at it.** `npm run dev` on the Mac (port 5275) or serve `dist/` locally. The two ticks above
   that say "built for it" are the ones a person closes.
2. **The pricing copy** (§4). A conspicuous placeholder today.
3. **Rewrite the draft copy** in Matt's voice. Every claim is grounded; the words are a start.
4. **The DPA text** (§0b) — then signup completes end to end.
5. **Choose the domain** (§5), then add the vhost, drop `noindex`, and set `VITE_TILL_URL` /
   `VITE_PORTAL_URL` so the two sign-in links appear.
---

## 7. What this does NOT cover

- **The DPA wording itself.** ⚠ The BLOCKER is §0b of this document — in scope as a *dependency*,
  out of scope as a *task*: correcting a legal instrument is Matt's and a solicitor's work, not code's.
- **Billing / taking a card at signup.** A different work package and a different set of obligations.
- **SEO beyond a title, description and sitemap.** Not a work package; a later afternoon.
- **A blog, docs site or status page.** ⚠ A status page ALREADY EXISTS and is live:
  `status.plutus.huggett.dscloud.me`, served from `/srv/apps/PLUTUS/status/` (⚠ no `current/`
  subdirectory, unlike the till and portal) with a `status.json` that is being written to daily.
  **Do not rebuild it, and do not fold it into this page.**
