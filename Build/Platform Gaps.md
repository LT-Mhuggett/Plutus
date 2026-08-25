# Platform Gaps — what is missing, blocked, or switched off

> **Matt, 2026-08-25:** *"Take platform gaps in the repo-runbook.md and handover and put them in a
> 'Platform Gaps.md' document. The repo-runbook.md should tell you how things work, not what is
> missing."*
>
> So: [`repo-runbook.md`](repo-runbook.md) is **how the platform works** — build, test, migrate,
> deploy, and the traps that bite while you do. **This page is what is not there yet.** If you catch
> the runbook describing a hole rather than a mechanism, the hole belongs here.

⚠ **Every row carries the date it was last checked against reality.** A gap list nobody re-verifies
becomes a to-do list of things already done — which is exactly how two items sat at the top of the
old handover's "do first" list for three days after being closed. **Verify before you schedule work
against a row here.** Items marked *verified 2026-08-25* were checked against the live database, the
live Caddy config or the code on that date.

⚠ **What is NOT here:** per-capability till state (that is [`till-design.md`](till-design.md) A0 and
Part B), how to prove a MAUI build works ([`Test Maui.md`](Test%20Maui.md)), and design rulings
(`till-design.md` **Part E**).

---

## 1. Blocked on a decision — nothing moves until Matt says

| What | Why it blocks | Where |
|---|---|---|
| **The DPA wording** | No DPA is published, so **signup cannot legally go public**. The supplied `Data_Processing_Agreement_Leading_Talent.docx` names the parties **backwards** for SaaS and carries **18 `[…]` placeholders** (company number, registered address, …). Its own text says *"to be reviewed by a solicitor before use"*. | [`To do/WP-landing.md`](To%20do/WP-landing.md) §0b, §90 |
| **The landing page price** | The pricing block is a **conspicuous placeholder**, chosen deliberately over inventing a number. | `WP-landing.md` §4, §329 |
| **The landing domain** | Then: a Caddy vhost, drop the `noindex`, and set `VITE_TILL_URL` / `VITE_PORTAL_URL`. A temporary URL in a marketing page outlives every intention. | `WP-landing.md` §278 |
| **Blocking vs nagging** | Whether an unsigned DPA / unpaid subscription blocks use or merely nags. | `plutus-platform-architecture.md` §12d |
| ⚠ **Whether to rotate the MySQL password** | The backend printed it in plaintext to `~/.pm2/logs/plutus-backend-out.log` on **every boot** from at least 2026-08-12 until it was found and fixed on 2026-08-25 (`Redact.ConnectionString`, backend 1.34.1). The logs have been masked in place and no live copy remains on disk — but it was readable to anything with the admin user's file access for ~2 weeks, and it was surfaced in a session transcript on 2026-08-25. **Matt's call.** ⚠ If rotating: the `caching_sha2_password` trap — a rotation breaks every plain-TCP client, which is why the backend and the nightly backup both use the unix socket. Check `~/PLUTUS/secrets/mysql.env`, the pm2 ecosystem env, and anything else holding the credential before turning it. | `repo-runbook.md` |
| **WP10 — an item editor on a till at all** | Arguably not a gap: MAUI has no full editor *deliberately*, because a till-created item reaches no report, no other till and no VAT return. Recorded as Matt's call rather than as work. | `till-design.md` Part B |

## 2. Needs a person at a screen — the largest remaining risk

⚠⚠ **This is the highest-value thing anybody can do on this platform.** Only somebody at a screen
turns a 🟡 into a ✅, and **every** hand-run so far has found faults no automated test here could
see: 14 findings on 2026-08-11 (6 invisible to automation), 5 on 2026-08-13, 10 in the 2026-08-18
parity review.

| What | Count | Where |
|---|---|---|
| **MAUI A0 rows never exercised by a person** | **41 🟡** | [`Test Maui.md`](Test%20Maui.md) |
| **Web till A0 rows never exercised by a person** | **21 🟡** | same |
| **The two 🟠** — park/recall's retrieve is a toolbar item nobody finds; the portal theme paints only ONE screen | 2 | `Test Maui.md` §5c items 1 and 10 |
| **Web till stock adjust** — built, gated and mutation-checked 2026-08-25, never used by a person | 1 🟡 | `till-design.md` A0 |
| **The landing page** — nobody has looked at it | — | `http://10.1.1.40:5275` |

*(Counts re-derived from `till-design.md` A0 with `awk` on 2026-08-25: MAUI 44 ✅ / 41 🟡 / 0 ⬜ / 3 ➖ / 2 🟠; web 68 ✅ / 21 🟡 / 0 ⬜ / 3 ➖. The old handover said "36 🟡" and was stale.)*

## 3. Built but switched off — live features doing nothing

⚠ These are the cruellest gaps, because the code is deployed and the feature simply does not happen.

| What | State | Fix |
|---|---|---|
| ⚠⚠ **Every discount rule is inert** | **Verified 2026-08-25:** all 6 rows in `Discounts` have `AutoApply = 0` and `DaysOfWeekMask = NULL`. The scheduled/automatic discount engine is live on both tills and **applying nothing** — correctly, because nothing is configured. | Portal → Prices → Discounts → e.g. **Warhammer Wednesday Discount** (id 5, already 15%, already targets a category) → Edit → tick *Apply it automatically*, tick *Wed*, Save. This is `Test Maui.md` **§G65b**. |
| **Test Business counts toward MRR** | Flagged `sandbox = 0`, so a tenant created for testing is counted as a paying subscriber and inflates the revenue figure. | Portal → the subscriber detail → the sandbox toggle. |
| **Test Business store 5 has no name** | `Stores` id 5 shows `(no name)` at address "Main" — the placeholder provisioning made before it named the first store after the business (fixed 2026-08-24, new tenants only). Store 6 "Test Store" is the real one. | Name it in the portal, or delete it. |

## 4. Infrastructure

| What | State | Detail |
|---|---|---|
| ⚠⚠ **Keycloak's H2 is still in the container layer** | **One command outstanding, needs a human** | The volume `plutus-keycloak-h2` is created, populated and **verified restorable** (2026-08-25), and `ops/keycloak/run-keycloak.sh` now creates, chowns and mounts it. But adding a volume means **recreating the container**, which stops the live IdP for ~30s — so it needs running by hand: `zsh ~/PLUTUS/bin/kc-move-to-volume.sh`. It renames the old container rather than removing it, so rollback is instant. **Until it runs, `docker rm` still erases every operator account and TOTP enrolment.** Then prove it: `docker rm -f plutus-keycloak && zsh ops/keycloak/run-keycloak.sh`, and log in as `matt` with the existing TOTP. |
| **Keycloak is dev-mode H2** | Open work | `start-dev` with an embedded H2 file. Postgres is already running on that host for other stacks. The volume closes the data-loss hole; it does not make this a production IdP. |
| **Keycloak's `plutus-webpos` client still names the OLD till host** | Latent — bites the day the web till uses OIDC | Its `rootUrl`, redirect URIs and web origins are all `https://plutus.huggett.dscloud.me`, which since 2026-08-25 is the landing page; the till is on `till.plutus…`. Nothing breaks today because **the web till runs in password mode** and never reaches Keycloak. The moment `VITE_AUTH_MODE=oidc` is used for the till, every redirect is rejected. Fix via `kcadm.sh` on the running container — and ⚠ the same edit must reach `ops/keycloak/plutus-realm.json`, which has *already* drifted (row below). |
| **The committed realm JSON has drifted** | Open work | Repo `ops/keycloak/plutus-realm.json` is **8853 bytes**; the Mac's copy — the one actually bind-mounted and imported — is **7162**, and they differ in content. Reconcile before any re-import. ⚠ **Do not reconcile by committing a real realm export**: exports embed password hashes and TOTP secrets and must never enter git. Account recovery comes from the backup, not the repo. |
| **MSIX signing** | Open work | A cert in the store plus `PackageCertificateThumbprint`. Needed before anyone installs the MAUI till on a shop PC; **not** needed to test it. |
| ⚠ **The till still shares a host with the landing page** | **Cutover written and staged 2026-08-25, to run out of hours** | `plutus.huggett.dscloud.me` serves the till today and becomes the landing page; the till moves to `till.plutus.huggett.dscloud.me`. ⚠⚠ **It cannot be a single step**: the till is an installed PWA whose service worker is network-first and **re-caches whatever `/` returns as its offline shell**, so flipping `/` would make each till adopt the marketing page as the thing it shows when the network drops. Two-step sequence, re-enrolment of both web tills (new origin = new `localStorage`), and a tombstone `sw.js` are all in [`till-subdomain-cutover.md`](till-subdomain-cutover.md). ⚠ DNS needs nothing — the DDNS wildcard already resolves any depth, verified. |
| **MAUI source runs ahead of every artefact** | By design, but it has cost a test run | Source **1.121.0**; newest artefact **1.120.0** (`D:\tmp\plutus-till-1.120.0\`). MAUI builds **only when Matt asks** (2026-08-16). ⚠ On 2026-08-20 a fault was reported for the second time while the fix sat in an undeployed bundle: **a fix that is built and not shipped is indistinguishable from a fix that was never made.** |

## 5. Known holes in the code — found, deliberately not closed

| What | Where | Why it is still here |
|---|---|---|
| ⚠⚠ **`Device` has NO global tenant filter — every listing must scope by hand** | `MySqlDbContext.TenantOwned` (2026-08-25) | It is the only tenant-owned table on the portal dashboard without a filter, and it leaked: *"when I log into the portal as test business I still see 5 active tills… That looks like Kapow?"* — it was. Every other figure was right (`People`, `Stores`, `StockLocation`, `WebStoreDetails` are all filtered), which is what made it read as a data problem rather than a scoping one. The dashboard is fixed and pinned by `DeviceTenantScopeTests`; `TillsController` already hand-scoped its own queries. ⚠⚠ **The real fix is adding `Device` to `TenantOwned` — but not on its own**: the device-auth paths (`SalesIngestService`, `HeartbeatController`, `CashModule`, `EnrolmentService`) look a device up by id with **no tenant context**, because the caller *is* the device presenting a secret. A bare global filter resolves those to the fallback tenant and **stops every non-Kapow till getting a token** — an estate-wide outage traded for a dashboard fix. Do it with `IgnoreQueryFilters()` on each auth path, in daylight, with the device-token probe verified after. |
| **No way to edit what a ROLE may do** | `RbacRoleGrants` is written in exactly one place — `RbacSeeder`, at startup. There is no API endpoint. | You can control **who holds which role** (portal → Users & Roles, with scope, day-mask and time windows). You cannot change **what a role means** without a code change — and ⚠ `EnsureBuiltInRolesAsync` **only ever ADDS** grants, so editing the seeder changes new tenants and not existing ones. Removing a permission from an existing tenant needs a deliberate data change. A portal role editor is a real work package, security-sensitive enough to build on purpose. |
| **Adjusting stock in the portal is hard to find** | Portal → Inventory → **Stock ledger** → row → **Detail**. | Reported **twice** (finding Z5 on 2026-08-13; again 2026-08-25). The Items sub-tab shows Stock as a read-only number with no affordance, and the sub-tab actually *named* "Stock adjustments" is a **read-only report**. Suggested fix: wire the Items tab's Stock column through to the adjust dialog. |
| **`ItemDialog` has no Escape handler** | `Plutus.Frontend.WebApp/src/InventoryPage.tsx` | A **D4 rule 2** gap (Escape must always cancel). The dialog has a ✕, a Cancel and a backdrop click. Noticed 2026-08-25 while adding the stock dialog next to it, which does handle Escape; not retrofitted. |
| **`parseInt` on the create-item stock field** | `InventoryPage.tsx:457` | Typing `2.5` as *Initial stock* silently creates **2**. Same class as the trap `stockAdjust.ts` was written to avoid (`parseInt("2.5") === 2`), one line — but it changes existing create behaviour, so it was left. |
| **The dialog↔commit seam on the MAUI till** | old step 11b | The orchestration was closed 2026-08-21; what remains is the wiring between the dialogs and the commit — the seam a hand-run broke while `TenderLoop`'s 19 tests stayed green. **Only `Test Maui.md` §G58 reaches it.** ≈1–2 days. |

## 6. Accepted — not going to be fixed

| What | Why |
|---|---|
| **`origin` cannot be pushed to** | A **151 MB** zip in old history exceeds GitHub's 100 MB limit, and fixing it means rewriting history `origin` already has. **`upstream` is the off-machine copy** and is current. |
| **`dotnet build` of the whole `Plutus.slnx` reports 6 errors** | They are the **box, not the code**: no Android SDK and no .NET Framework 4.7.2 targeting pack on this Windows machine. Build the projects you need. |
| **No node on the Windows dev box** | Matt's preference. Every TypeScript gate (`tsc`, `eslint`, `vitest`) therefore runs **on the Mac** — see `repo-runbook.md`. Not a gap to close, a constraint to work with. |

## 7. Tenancy — the audit of 2026-08-25, and what it left open

The full audit is in `plutus-platform-architecture.md` **§3.1**; the mechanism is
`TenancyInvariantTests`, which fails on any entity carrying a `TenantId` without a query filter.
These are the rows it left open.

| What | State |
|---|---|
| ⚠ **`MemberNoCounter` — UNTRIAGED** | A per-tenant counter for member numbers, with **no query filter**. If it is read unfiltered, two tenants could advance or collide on one counter — and member numbers are supposed to be unique per tenant, with a check character that assumes it. ⚠ The audit found **no `_db.MemberNoCounters` call site**, so it is reached some other way (raw SQL, or a differently-named set) and needs eyes on it. Exempted in the test **only to record it honestly**, not because it is safe. |
| ⚠ **`TenantSendingIdentity` — UNTRIAGED** | Per-tenant email sending identities, no filter. Plausibly operator-managed like the other `Tenant*` rows, but nobody has checked. Same caveat as above. |
| **`Device` — hand-scoped, by necessity** | See §5. The device-auth paths look a device up with no tenant context, so a bare filter would stop every non-Kapow till getting a token. The proper fix is the filter **plus** `IgnoreQueryFilters()` on each auth path, verified with the device-token probe. |
| ⚠ **The integration suite is flaky under parallelism** | Observed twice on 2026-08-25: one run failed `ImpersonationE2eTests`, the next failed four unrelated `E2eTests`, the next passed **241/241**. The failures are `EnsureCreated()` during host start-up, i.e. fixture contention, not logic. **This matters more than it looks:** a suite you re-run until it goes green is a suite where a real failure gets waved through — which is exactly how the three tenancy bugs above survived. |
