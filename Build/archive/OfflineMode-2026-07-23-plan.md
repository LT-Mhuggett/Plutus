> **📦 SUPERSEDED — one live decision carried forward.**
> The offline *goal* was met on the web till (PWA + IndexedDB outbox, 2026-07); the MAUI half is
> now WP3 of [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md).
> **§4.3 (the credential fork — local PIN vs synced password hashes) is still open** and is
> decision #2 in that plan; the evidence for Option B recorded here is why it is the recommendation.
>
> ⚠ **§7's seven replay bugs are ClientUI's, not AppClient's.** They matter only if ClientUI's
> repository *implementation* is ported. The new plan says to port the interface shape and leave
> the bugs behind.

# Offline / Local-DB Mode — design & plan

**Date:** 2026-07-23
**Scope:** MAUI `Plutus.Frontend.ClientUI` (Windows head; applies to all heads once migrated).
**Status (re-verified 2026-08-07): NOT IMPLEMENTED for MAUI — but the *goal* was met elsewhere.**
The **web till** now has offline trading (PWA, IndexedDB catalogue, checkout outbox with replay),
delivered under `WebApp-2026-07-23-plan.md` Phase 5. Nothing in §5's phased plan was built in
`ClientUI`. This document is kept because it is **live input** to
[MAUI-Backend-Sync-Plan-2026-08-01.md](MAUI-Backend-Sync-Plan-2026-08-01.md), which cites §4.3
(the credentials fork) as an open decision and §7's bug list as known defects to fix.
**Companion docs:** [HANDOVER.md](../../HANDOVER.md), [Migration-2026-07-22-plan.md](Migration-2026-07-22-plan.md), [BugFix-2026-07-22-plan.md](BugFix-2026-07-22-plan.md).

---

## 1. Goal (TL;DR)

Let the app run against the **local SQLite database as the source of truth**, without a backend or Azure AD B2C connection — the capability the old NatApp had via its "Local login". Concretely: after **Restore Database** (Settings), a restored `Database.db` should actually drive the app's screens.

The good news from the architecture review: **the data layer is already local-first.** Most of the work is *centralising an existing decision* and *adding a non-B2C session*, not building a sync engine from scratch.

---

## 2. How data access works today (findings)

Three abstract bases implement a local-first + best-effort-API pattern; concrete repos are thin subclasses:
- `Services/Repository/Base/RepositoryBase.cs` (single-key entities)
- `Services/Repository/Base/CompositeRepositoryBase.cs` (two-key: Item, Category, Transaction)
- `Services/Repository/Base/TriCompositeRepositoryBase.cs` (three-key: Stock)
- `Services/Repository/EmployeeRepository.cs` — **does not inherit the base**; re-implements the pattern itself (has drifted — see §7).

**Writes (Create/Update/Delete)** — consistent rule:
1. Write to local SQLite first (`base.Create/Update/Delete` + `SaveChangesAsync`).
2. If online → call the API (Update = GET-then-PATCH diff; Create = POST; Delete = DELETE). On non-success → queue a `DBAction`.
3. If offline → queue a `DBAction` directly.
4. On `HttpRequestException`/`TaskCanceledException` → queue a `DBAction`.
→ **Local DB is always the write source of truth; the API is best-effort.**

**Reads (GetAll/FindById/FindAllByCondition)** — consistent rule:
- If online → hit API, upsert results into local SQLite (wrapped in `SetSyncState(true)/false`).
- Online **or** offline → **always return from local SQLite**.
→ **Offline reads silently return the local cache.**

**Outbox / sync (already exists, rudimentary):**
- `DBAction` table (type + record id + Created/Modified/Deleted) lives in `SqliteDbContext` (`Plutus.Entities/SqliteDbContext.cs`).
- `SaveDBAction(...)` (`RepositoryBase.cs:616`) enqueues; each repo ctor subscribes to `Connectivity.ConnectivityChanged` and calls `LocalToServerSync()` when internet returns; success removes the `DBAction`.

**The online/offline decision** is the literal `Connectivity.NetworkAccess == NetworkAccess.Internet`, **copy-pasted across ~11 sites**:
`RepositoryBase.cs` (ctor:53, Create:87, Update:176, Delete:284, FindById:445, HttpGet:530, LocalToServerSync:662); `CompositeRepositoryBase.cs` (52,78,157,317,404,532); `TriCompositeRepositoryBase.cs` (51,76,149,307,391,517); `EmployeeRepository.cs` (39,70,156,266,407,487,563); `TillRepository.cs:28`; `StockRepository.cs:36`; `BusinessRepository.cs:27`.

**Persistence lifetime:** `AddDbContext<SqliteDbContext>` (Scoped) is captured by a **singleton** `RepositoryWrapper` (`ServiceExtensions.cs:17`) → effectively **one long-lived `SqliteDbContext`** for the whole app, guarded by a single static `SemaphoreSlim(1,1)`. `RepositoryWrapper` ctor runs `Database.Migrate()`.

**Auth coupling:** the bearer token is `AppState.CurrentActiveUser.Value`, attached manually in each `CreateHttpMessage`. It is set **only at login** (`LoginViewModel.cs:57-66`). Local data ops need **no token** — they only need `RepositoryContext.CurrentUser` set (else `SaveMethods` throws `ObjectIdMissingException`). There is no token refresh/interceptor on the request path.

---

## 3. Why offline doesn't "just work" today

1. **No way to declare "operate locally."** With Wi-Fi on but no valid token (e.g. dev bypass), repos believe they're online, call the API with a bad/expired token, and fail — instead of using the local cache.
2. **No local login.** Auth is B2C-only; nothing establishes a session (`CurrentActiveUser.Key` + `RepositoryContext.CurrentUser`) without a successful B2C round-trip.
3. **Offline-write replay is partially broken** (fine for read-only local use, not for editing offline) — see §7.

---

## 4. Proposed design

### 4.1 Central connectivity gate
Introduce `IConnectivityGate` (single source of truth for "should we talk to the backend?"):

```csharp
public interface IConnectivityGate
{
    bool IsOnline { get; }            // realConnectivity && Mode != Local
    AppConnectivityMode Mode { get; } // Auto | Local
    void SetMode(AppConnectivityMode mode);
    event EventHandler ModeChanged;
}
public enum AppConnectivityMode { Auto, Local }
```
- `IsOnline` = `Connectivity.NetworkAccess == NetworkAccess.Internet` **AND** `Mode == Auto`.
- Register singleton; inject into `RepositoryWrapper` and pass into every repo (same way the `SemaphoreSlim` is threaded today).
- **Replace all ~11 `NetworkAccess.Internet` literals** with `_gate.IsOnline`. In `Local` mode every repo takes the existing offline branch → no API calls attempted, pure local SQLite. This is the core change and reuses machinery that already works for reads.
- The `ConnectivityChanged`→`LocalToServerSync` subscriptions become gate-aware (don't push while in `Local` mode).

### 4.2 Local session (non-B2C)
A session needs two things set — **neither requires a token**:
- `AppState.CurrentActiveUser = (employeeId, "")`
- `RepositoryWrapper.SetCurrentUser(employeeId.ToString())` (sets `RepositoryContext.CurrentUser`).

Plus `AppState.Business/Store/Till` loaded **from the local DB** (repos already return local data when `IsOnline` is false). A new `LocalLoginViewModel`/flow selects/authenticates a local employee and populates these, then navigates to `MainPage`.

### 4.3 Credential decision (the real fork — needs a decision)
The MAUI `Plutus.Entities.Models.Employee` has **no `HashedPassword`/`Salt`** (those live only on the old `Plutus.Database.Models.EmployeeModel`). So NatApp-style local password auth isn't directly portable. Options:

| Option | What it is | Cost | Security |
|---|---|---|---|
| **A. Local-user picker (dev/kiosk)** | Pick from employees already cached in the local DB; no password. | Low | Low — fine for dev/single-operator kiosk, not multi-user. |
| **B. Sync credentials down** | Add `HashedPassword`/`Salt` to the synced Employee payload; verify locally with existing `Core/Security/Password.Verify`. | Medium (backend + entity change) | Good — true offline password auth. |
| **C. Cached B2C token** | Rely on MSAL `AcquireTokenSilent` + `offline_access` refresh token; only works until refresh needs the network. | Low | N/A — not truly offline; a bridge at best. |

**Recommendation:** **A** for Phase 2 to unblock local operation now; **B** as the production answer when backend work is scheduled.

*Supporting evidence for B (2026-07-23):* the live NatApp till backup (`Kapow Comics ltd - Database - 23_07_2026 15_57_23.db`, see WebApp plan §2.4) confirms the old schema's `Employees` table carries `HashedPassword` + `Salt` and the production data has a real credentialed user — Option B is a restoration of the original design, not a new invention, and `Core/Security/Password.Verify` already implements the matching hash check client-side.

### 4.4 Where the mode is chosen
- A toggle in **Settings** ("Work offline / Local mode") calling `IConnectivityGate.SetMode`.
- The DEBUG dev-bypass sets `Mode = Local` automatically (so bypass = a working local session, and Restore drives the app).
- Optional: auto-fall-back to a read-only local view when `Auto` mode detects no connectivity (already the de-facto behaviour once the gate is centralised).

---

## 5. Phased plan

### Phase 1 — Local mode seam (small, high value)
- Add `IConnectivityGate` + `AppConnectivityMode`; register singleton; thread into `RepositoryWrapper` + repos.
- Replace the ~11 `NetworkAccess.Internet` literals with `_gate.IsOnline`.
- Make `ConnectivityChanged`→sync gate-aware.
- Set `Mode = Local` in the dev-bypass; establish the local session (`CurrentActiveUser` + `CurrentUser`) and load `Business/Store/Till` from local DB.
- **Outcome:** a restored `Database.db` drives the app in bypass/local mode; no bogus API calls.
- **Touch-points:** the 3 base classes, `EmployeeRepository.cs`, `RepositoryWrapper.cs`, `ServiceExtensions.cs`, `LoginViewModel.cs`.

### Phase 2 — Real local login + Settings toggle (medium)
- `LocalLoginViewModel` + page (Option A picker first).
- Settings toggle bound to `IConnectivityGate.SetMode`; show current mode in the UI.
- Decide + (if B) schedule the credential-sync backend change.

### Phase 3 — Make offline *writes* reliably sync back (as needed)
Only required for entities edited while offline. Fix the replay bugs in §7.

---

## 6. What already works vs. needs building

| Capability | State |
|---|---|
| Read from local DB offline | ✅ Already works (once the gate stops forcing API calls) |
| Local-first writes to SQLite | ✅ Already works |
| Outbox queue of offline changes | ✅ Exists (`DBAction`) |
| Declare "operate locally" | ❌ Phase 1 |
| Non-B2C session | ❌ Phase 1 (session) / Phase 2 (login UI) |
| Reliable offline-write replay | ⚠️ Phase 3 (bugs in §7) |

---

## 7. Known bugs to fix before relying on offline *writes* (Phase 3)

1. **`ItemRepository` id-converters throw** (`ItemRepository.cs:19-27`, `ConvertIdToString`/`ConvertStringToId` = `NotImplementedException`) → queued Item writes **cannot replay**. Blocker for offline Item edits.
2. **Composite/Tri `Delete` throws `NotSupportedException`** (`CompositeRepositoryBase.cs:237`, `TriCompositeRepositoryBase.cs:227`) → Items/Categories/Transactions/Stock can't be deleted at all.
3. **`EmployeeRepository` divergence** — hand-rolled copy of the base; `Create` sets `message.Headers.Add("businessId", AppState.ToString())` (`EmployeeRepository.cs:73`) — stringifies the whole AppState. Should inherit the base or be reconciled.
4. **`StockUpdateByQuantityChange`** queues a `DBAction` only when offline (`StockRepository.cs:60`); an online PATCH failure is **not** queued → lost change.
5. **`RepositoryWrapper.SetSyncState`** calls `SaveChangesAsync(syncState)` (`RepositoryWrapper.cs:83`) — that overload is `acceptAllChangesOnSuccess`, not the sync flag; wrapper-level sync-state toggling is a no-op.
6. **Lazy repos don't drain their outbox** — repos are created on first use (`??=`); a repo never touched has no live `ConnectivityChanged` subscription, so its queued `DBAction`s never replay. Needs a startup drain across all types.
7. **No cross-type ordering / retry / conflict resolution** in `LocalToServerSync`; offline-created rows use client ids with no server-id reconciliation.

---

## 8. Risks & open questions

- **DbContext file locking on Restore:** the single long-lived `SqliteDbContext` may hold `Database.db`; restoring the file cleanly likely needs an app restart (Settings already advises this). A proper implementation would dispose/recreate the context after restore.
- **Captive singleton context** is not thread-safe; the global semaphore masks it. Formalising the context lifetime is worth doing alongside this work but is not strictly required for read-only local mode.
- **Credential story (§4.3)** is a product/security decision, not just engineering — needs sign-off before Phase 2 Option B.
- **Multi-head:** all touch-points are cross-platform C# (no per-platform code), so this applies to Android/iOS once those heads are migrated.

---

## 9. Recommended first step

Phase 1 only. It is contained (one new interface + a mechanical find/replace of the connectivity literal + the dev-bypass session), reuses proven local-first machinery, and directly delivers "a restored database drives the app." Phases 2–3 can follow once the credential decision and offline-write needs are settled.
