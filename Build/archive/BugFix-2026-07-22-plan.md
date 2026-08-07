> **📦 SUPERSEDED — implemented where it matters.** All three bugs are **fixed in AppClient**, the
> go-forward app: the add-user crash is a guarded dialog, `Basket` is a plain `ObservableCollection`
> with no reversing copy, and `Models/BasketAlteration.cs` carries `[JsonConstructor]` plus setters.
> They remain unfixed in **ClientUI**, which [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md)
> recommends retiring — so they will never need fixing there. The original NatApp is no longer in
> this repo at all.

# Plutus — Bug Fix Plan

**Date:** 2026-07-22
**Scope:** Three reported UI/interface bugs, to be fixed in **both** frontends.
**Status (re-verified 2026-08-07): PARTIALLY IMPLEMENTED — kept open for `ClientUI`.**

| App | Bug 1 (add-user crash) | Bug 2 (item order) | Bug 3 (discount + save) |
|---|---|---|---|
| **AppClient** (the NatApp lineage, now MAUI) | ✅ stopgap dialog + try/catch, `LoginViewModel.cs` | ✅ `Basket` is a plain `ObservableCollection`, no reversing copy | ✅ `[JsonConstructor]` + setters on `Models/BasketAlteration.cs` |
| **ClientUI** | ❌ no add-user screen | ❌ `TillViewModel.cs:51` still returns `_basket.Reverse()`, `Core/Settings.cs:142` still defaults `true`, setter guard at `:145` still inverted | ❌ `Domain/Models/BasketAlteration.cs` still has two ctors, get-only props, no `[JsonConstructor]` |

⚠ The original **NatApp** (`Plutus/Frontend/OSs/NatApp.Plutus`) is **no longer in this repo** —
nothing under it is tracked by git. The fixes landed in `Plutus.Frontend.AppClient`, its MAUI
successor. Whether the ClientUI column is ever closed depends on
[MAUI-Backend-Sync-Plan-2026-08-01.md](MAUI-Backend-Sync-Plan-2026-08-01.md) — if ClientUI is
retired in favour of AppClient, this document can be archived as-is.

## Context: two frontends

Plutus has two coexisting frontends (mid-migration). The same three bugs exist in both because logic was carried forward.

| Alias | Project | Notes |
|-------|---------|-------|
| **NatApp** | `Plutus/Frontend/OSs/NatApp.Plutus` | Older Xamarin app. **This is the app currently in use** (screenshot, "Ver. 2.1.13.0"). Uses top tabs. |
| **ClientUI** | `Plutus/Frontend/Plutus.Frontend.ClientUI` | Newer .NET MAUI (net7.0) app. Migration target. Left-flyout shell. Some features (add-user screen) not yet built. |

Reported bugs:
1. "Unable to add a new user without the system crashing."
2. "When purchasing a new item, new items are added to the top of the page instead of the bottom; also want the ability to (re)order items."
3. "If you apply a discount to a transaction and then save the basket for later, it crashes."

---

## Bug 1 — People icon crashes ("add a new user")

**Root cause:** the top-right "Users" (people) toolbar command is an unimplemented stub that throws `NotImplementedException` on tap. It is a synchronous command with no try/catch, so the exception crashes the app immediately (before any screen appears).

### NatApp (this is the crash you see)
- `ViewModels/LoginViewModel.cs:68-72` — `ExecuteShowLoggedUsers()` body is `throw new NotImplementedException();`
- Wired to the toolbar at `ViewModels/LoginViewModel.cs:118-124` (`IconImageSource = "md-people"`, `Command = ShowLoggedUsersCommand`).

**What to do:**
- **Immediate stopgap (stops the crash):** replace the `throw` with a user-facing "feature not yet available" dialog, and wrap the command body in try/catch.
- **Full fix:** implement the logged-in-users / add-user page the command is meant to open, then persist via the existing DB/employee layer.

### ClientUI (feature not built yet)
- There is **no** add-user screen wired up; `EmployeeRepository.Create` is never called and `AuthorisationPage` is registered in DI but never shown.
- Latent defects to fix while building it:
  - `Plutus/Commons/Plutus.Entities/Models/Employee.cs:28` — `FullName => string.Format("{0} {1}", LName.ToUpper(), FName)` throws NRE if `LName` is null. Fix: `=> $"{(LName ?? string.Empty).ToUpper()} {FName}".Trim();`
  - `ViewModels/PopupViewModels/AuthorisationViewModel.cs:31` — `e.Email.ToLower()` NRE if any employee email is null; also `CanAuthorise()` at `:39` has inverted enable logic.
  - `Services/Repository/EmployeeRepository.cs:426` — `.First(...)` in a try/finally with no catch → `InvalidOperationException` when the user isn't in the local DB. Fix: `.FirstOrDefault(...)` (callers already null-check).
  - `Services/Repository/EmployeeRepository.cs:73` — `message.Headers.Add("businessId", AppState.ToString())` sends the class name, not the GUID. Fix: `AppState.Business.Id.ToString()`.
  - Server: `Plutus/Endpoints/Plutus.DBService/Controllers/EmployeeController.cs:32` and `:150` use `.First(...)` on claims → 500 if the claim is absent. Fix: `.FirstOrDefault()` + guard.

---

## Bug 2 — New items add to the top instead of the bottom (+ reordering)

**Root cause (identical in both):** items are appended to the backing collection correctly (`_basket.Add(...)`), but the property the UI binds to (`Basket`) returns a **reversed copy** when the setting `TillListViewOrderReversed` is on — and that setting **defaults to `true`**. The reversed getter also returns a throwaway collection, so other `Basket.Add/Remove/Clear` calls operate on a discarded copy and reordering is impossible.

### NatApp
- Reverse: `ViewModels/MainTill/Till/TillViewModel.cs:57-59`
- Default `true`: `ViewModels/Settings.cs:93`

### ClientUI
- Reverse: `ViewModels/MainTill/TillViewModel.cs:46`
- Default `true`: `Core/Settings.cs:142`
- Inverted setter guard (toggle never persists): `Core/Settings.cs:145`

**What to do (both):**
1. Bind `Basket` directly to `_basket` (remove the reversing copy). New items then land at the bottom, and all `Basket.*` mutations hit the real collection.
2. ClientUI: also correct the inverted setter guard at `Core/Settings.cs:145` (`if(!Equals(...))`) and add the missing braces.
3. **Reordering feature:** once bound to the real collection, enable list reorder and persist the new order back into `_basket`:
   - ClientUI: switch the `ListView` at `Pages/MainTill/TillPage.xaml:124` to a `CollectionView` with `CanReorderItems="True"`.
   - NatApp: use the equivalent Xamarin `ListView` reorder support.

---

## Bug 3 — Discount + save for later crashes (crash is on RECALL)

**Root cause (identical in both):** the basket is persisted as a JSON blob. Plain `BasketItem`/`BasketNote` have a single constructor and round-trip fine, but the discount object `BasketAlteration` has **two constructors, get-only properties, and no `[JsonConstructor]`**, so Newtonsoft cannot choose a constructor when deserializing → `JsonSerializationException`. This is why only *discounted* saved baskets crash. The crash surfaces when the saved basket is **recalled** (the deserialize step), not on the save write. Both commands are `async void` with no catch, so the exception crashes the process.

### NatApp
- Serialize (save): `ViewModels/MainTill/Till/TillViewModel.cs:729`
- Deserialize (recall — crash): `ViewModels/MainTill/Till/TillViewModel.cs:801`
- Model: `Models/BasketAlteration.cs` (two ctors, get-only `Discount` / `ItemsAssocitated`, no `[JsonConstructor]`)
- Extra defect: off-by-one `StoredTransactions.RemoveAt(StoredTransactions.Count())` at `:745` throws `ArgumentOutOfRangeException` if a save fails.

### ClientUI
- Serialize (save): `ViewModels/MainTill/TillViewModel.cs:427`
- Deserialize (recall): `ViewModels/MainTill/TillViewModel.cs:373`
- Model: `Domain/Models/BasketAlteration.cs` (same shape; also property/param name mismatch `ItemsAssocitated` vs `itemsAssociated`)
- Latent: computed getters `BasketItem.Tax => Item.Tax.Name` (`Domain/Models/BasketItem.cs:55`) and `BasketNote.Name => Note.Text` NRE if navigation is unloaded.

**What to do (both):**
1. Make `BasketAlteration` round-trippable: add a `[JsonConstructor]` (on the `IEnumerable<BasketItem>` ctor) and give `Discount`/`ItemsAssocitated` setters; fix the `ItemsAssocitated`/`itemsAssociated` name mismatch (ClientUI).
2. Persist a lightweight discount DTO (Id, Name, Amount, Type) instead of the full EF `DiscountModel`/`Discount` graph — decouples the saved blob from EF navigation graphs and shrinks it.
3. Wrap both `SerializeObject`/`DeserializeObject` calls in try/catch with a user-facing error (defensive: these are `async void`).
4. NatApp: fix the off-by-one `RemoveAt` in the save-failure path.
5. ClientUI: `[JsonIgnore]` or null-guard the computed `Tax`/`Name` getters.

> Caveat: this assumes the save *write* succeeds and the crash is on recall — which is what the code shows (plain baskets round-trip; only `BasketAlteration` fails to deserialize). If the crash occurs the instant "Save Transaction" is pressed, capture the exact exception text to confirm; the deserialize ambiguity is the clear discount-specific defect regardless.

---

## Suggested sequencing (when implementation is approved)

1. **Bug 2** in both apps — lowest risk, immediately visible. Validate on NatApp first.
2. **Bug 3** in both apps — models + DTO + try/catch (+ NatApp off-by-one).
3. **Bug 1** — NatApp stopgap guard first (kills the crash), then build the real users page in both apps (+ ClientUI latent employee/server fixes).

Each is a small, self-contained change: branch off `master`, one bug per commit, per app.

## Risk / effort summary

| Bug | App(s) | Effort | Risk |
|-----|--------|--------|------|
| 2 — item ordering | Both | Low | Low |
| 3 — discount + save | Both | Medium | Medium |
| 1 — add user (NatApp stopgap) | NatApp | Low | Low |
| 1 — add user (full page + latent fixes) | Both | Medium | Medium |
