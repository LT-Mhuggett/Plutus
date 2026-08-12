# Legacy removal register — what comes out of the MAUI till, and in what order

> ## 📦 ARCHIVED 2026-08-12 — this register now lives in [`Build/MAUI-retrofit.md`](../MAUI-retrofit.md) §10
>
> ⚠⚠ **NOT delivered — the deletions have NOT been done.** L1–L10 are still ahead, and Matt still does
> them last of all. The register was folded in **verbatim** (every L-row, both do-not-delete warnings
> and the "deleted rather than listed" table) because it belongs with the steps that unblock each row:
> L4 waits on step 26, L6 on step 11b, L7/L8 on step 21, L10 on step 22.
>
> ⚠ **Code comments in the MAUI app cite "`Build/legacy-removal.md` (L3)" and similar.** Those L-keys
> are unchanged and still findable — they now anchor in `MAUI-retrofit.md` §10 (`#l3--the-legacy-permission-gate`).
> Comments were repointed in the same commit; if you find one that was missed, the L-key is the thing
> to search for.

**Matt, 2026-08-10:** *"As part of bringing MAUI to Parity, the legacy stuff needs hiding and
eventually removing. Can you document what will eventually need removing and I will do that last of
all."*

This is that list. **Hiding has been done; deleting has not.** Every item below is code that is
still compiled and still shipped, but is either unreachable from the UI or reachable only in a way
that cannot affect the platform.

> ⚠ **Do the deletions LAST, and in the stated order.** Several of these items are load-bearing for
> each other — `Helpers/Database/Database.cs` cannot go until every screen above it has, and the
> legacy models cannot go until that does. Deleting bottom-up produces a build that will not
> compile and a diff nobody can review.

> ⚠ **The legacy `Database.db` file itself is NOT on this list and must not be deleted.** It is the
> shop's pre-cutover sales history and there is no server copy (binding default 3: archive, never
> delete). Removing the *code* that reads it is safe; removing the *file* is not.

---

## Status key

| | Meaning |
|---|---|
| 🙈 **Hidden** | No longer reachable from the UI. Code still ships. Safe to delete when its turn comes. |
| ⚠️ **Live** | Still reachable and still runs. Must be replaced before it can be removed — the WP is named. |
| 🔒 **Blocked** | Cannot be deleted yet; something else depends on it. The blocker is named. |

---

## L1 — The legacy-database archive path 🙈

| | |
|---|---|
| **Code** | `SettingsViewModel.ExecuteBackupDb` + `BackupDbCommand`; `Plutus.Client.Storage.Cutover.ArchiveLegacyDatabase`; `MetaKeys.LegacyArchivedAtUtc`; the archive gate in `EnrolmentFlow.BlockedReasonAsync` |
| **Hidden** | 2026-08-10 — the "Database" section of the settings screen is gone |
| **Order** | Any time |

⚠ **Read this before deleting.** `ExecuteBackupDb` is the only thing that can stamp
`LegacyArchivedAtUtc`, and that stamp is what `EnrolmentFlow.BlockedReasonAsync` looks for before
allowing a till holding an un-archived legacy file to enrol. **The gate is passed `null` today and
does not run**, so removing this changes nothing that currently works — but it permanently removes
the ability to switch that gate on. If a real shop is ever migrated off NatApp, its sales history
would have no on-ramp. Matt's call, taken 2026-08-10 on the basis that no such migration is
planned.

"Restore database" was deleted outright on 2026-08-10 rather than hidden: it overwrote the legacy
file from a user-chosen `.db`, which no screen reads any more, and it crashed on the legacy gate
before it could even do that.

---

## L2 — Till-side inventory CRUD 🙈

| | |
|---|---|
| **Code** | `ViewModels/MainTill/Inventory/Items/AddEditViewModel.cs` (whole file); `AddEditView.xaml` + `.cs`; `ViewAllViewModel`'s `OpenEditItemCommandArg`, `UpdateItemStockCommandArg`, `CreateNewCategoryCommand`, `OpenAddItemCommand` |
| **Hidden** | 2026-08-10 — "Add item" gone from Inventory; "Edit" and "Update stock" gone from the item list's context menu |
| **Order** | Before L5 |

It could not work in either direction. It writes items into the legacy local database, and the till
client has **no item-write endpoint at all** (`PlutusApiClient` reads the catalogue and nothing
more) — so a till-created item reaches no report, no other till and no VAT return. And since the
basket now resolves items from the v2 catalogue, a locally-created item cannot be **sold** on the
machine that made it either. An operator would type a full item in and then be unable to find it.

Items belong to the portal ("Portal decides, till obeys"). MAUI's inventory parity target is
**WP10**; when that lands it replaces this rather than reviving it.

---

## L3 — The legacy permission gate ⚠️

| | |
|---|---|
| **Code** | `Helpers/Security/Authorisation.cs` — `IsAuthorised` (both overloads), `LegacyCheck`, `RequestAuthorisedUserInput` |
| **Replaced by** | `Services/Security/TillGate.cs` (cutover step 12) |
| **Order** | After L2 and L4 — those hold the last callers |

It reads employees and an `AuthActions` table out of the legacy local database, which a
portal-provisioned till does not have. Until 2026-08-10 it did not merely fail — **it crashed the
app**: `GetEmployee` returned null and `emp.EmpAuths` threw, out of `async void` command handlers
with no `catch`. That is what took the till down when Matt pressed "Change printer". It now refuses
instead of throwing, and logs that it was reached at all.

`RequestAuthorisedUserInput` never assigns the id it returns, so its `do/while` re-prompts for ever
and only Cancel escapes. **Supervisor override on this till has never once succeeded.**

Remaining callers, all now unreachable from the UI but still compiled:
`AddEditViewModel` ×3, `ViewAllViewModel` ×1.

---

## L4 — Till-side reporting ⚠️

| | |
|---|---|
| **Code** | `ViewModels/MainTill/Statistics/SalesReportsViewModel.cs`, `StockOuttakeViewModel.cs` and their views |
| **State** | **Still reachable** — warned, not hidden (2026-08-10) |
| **Replaced by** | **Cutover step 26 / WP11** |
| **Order** | After step 26 ships |

Both read the legacy local database throughout. Since cutover step 11 sales go to the v2 store and
the platform, **not** there — so on a portal-provisioned till these report **zero** for everything
sold since. A report stating "£0.00 takings" about a day the shop took £2,000 is considerably worse
than one that will not open, which is why the Statistics tab now carries a warning saying so.

⚠ **Warned rather than hidden, deliberately.** A till migrated from a NatApp install still holds
real history in that file, and this is the only way to see it. Hiding it would remove the only
access to data that has no server copy.

⚠ **THESE TWO SCREENS ARE NOW THE ONLY THING KEEPING SYNCFUSION IN THE BUILD.** Matt, 2026-08-10:
*"I am not going to renew Syncfusion, it seems like it can be replaced."* As of till 1.33.0 every
Syncfusion control on a reachable screen is gone — quantity box, alterations picker, item list,
discount multi-select. What remains is `SfCartesianChart` and `SfCalendar` here, plus the `XlsIO`
export in `Helpers/FileIO/ExcelHandling.cs`, whose **only** caller is `SalesReportsViewModel`.

So deleting L4 also deletes: `ExcelHandling.cs`, `SyncfusionLicenseProvider.RegisterLicense` in
`App.xaml.cs`, `ConfigureSyncfusionCore` in `MauiProgram.cs`, and every Syncfusion package
reference. ⚠ **In that order, and not before** — removing the licence registration while a licensed
control still exists in the assembly turns a dormant screen into a trial-dialog screen, which is
worse than leaving it. Full detail in [`syncfusion-footprint.md`](../syncfusion-footprint.md).

---

## L5 — The legacy database layer 🔒

| | |
|---|---|
| **Code** | `Helpers/Database/Database.cs`; the `Plutus/Data/Database` project (55 files) |
| **Blocked by** | L2, L4, L7, L8 — and `TillViewModel`, which still uses legacy models for the basket |
| **Order** | After everything above |

Still referenced from 12 files. `TillStoreAccess`'s header records why the v2 store has a single
owner: this layer opens its own context at **37 call sites across 21 files**, and reproducing that
shape on a new schema was the thing to avoid.

⚠ `App.xaml.cs:59` calls `Helpers.Database.Database.LocalDbExist()` in the startup path. Check what
that decision does before removing it — a start-up branch is not a screen and will not announce
itself when it changes.

---

## L6 — The legacy models 🔒

| | |
|---|---|
| **Code** | `Plutus/Data/Database/Models/` — `ItemModel`, `SaleModel`, `StoreModel`, `EmployeeModel`, `PaymentMethodModel`, `TaxModel`, `TransactionModel`, `StockModel`, `AuthActions`, `Emp_AuthActions`, and the rest |
| **Blocked by** | L5, and the basket |
| **Order** | Last |

⚠ **`ItemModel` is load-bearing in the till screen today.** `TillViewModel.FindItem` maps v2
`CatalogueItem` rows *into* `ItemModel` because that is what the basket and the item list bind to —
and **MAUI bindings fail silently**, so swapping the bound type blanks the rows rather than failing
the build. These go when the basket is reshaped (**step 11b**), not before.

⚠ Money on these models is `decimal`; the platform is integer pence end-to-end. That divergence is
recorded in till-design **C2** and is closed by **WP2**, not by this list.

---

## L7 — `LoginViewModel.EnsureStoreAsync` ⚠️

| | |
|---|---|
| **Code** | `ViewModels/LoginViewModel.cs` — `EnsureStoreAsync` and its two legacy `Database` blocks |
| **State** | **Still runs on every sign-in** |
| **Order** | Cutover **step 21**, already planned |

A 2026-08-09 hotfix that writes API data into the legacy `Stores` table — exactly the bridge that
binding default 9 forbids. Step 14's Meta-cached store header replaces it. It is currently failing
with a null `StoreModel` primary key on every sign-in (visible in the crash log, harmless because
it is caught) — which is a fair summary of why it should go.

---

## L8 — Obsolete first-run screens 🙈

| | |
|---|---|
| **Code** | `SetupViewModel`, `TransferThirdPartyViewModel` (self-labelled LEGACY; throws from `async void`), `RecoveryViewModel`, their views, and their `<Compile Update>` items |
| **Order** | Cutover **step 21**, already planned |

⚠ **`RecoveryViewModel` was to be KEPT** by the step 21 plan, reframed as the cutover on-ramp
feeding `Cutover.ArchiveLegacyDatabase`. If L1 is deleted, that reason goes with it and Recovery can
go too — but decide L1 first, because this depends on it.

---

## L9 — `AppViewModel.EmployeeId` and `AppViewModel.Employees` ⚠️

| | |
|---|---|
| **Code** | `ViewModels/AppViewModel.cs` |
| **Replaced by** | `AppViewModel.SignedInOperator` |
| **Order** | With L3 |

`EmployeeId` is `Employees.Last().Id`, which **throws for every portal-roster operator** — the list
is empty. Every legacy gate call site reaches through it, which is the other half of why those
buttons crashed rather than refused. Nothing new may use it.

---

## L10 — `Plutus.Frontend.ClientUI` 🔒

| | |
|---|---|
| **Code** | `Plutus/Frontend/Plutus.Frontend.ClientUI/`, and its entry in `Plutus.slnx` |
| **Order** | Cutover **step 22** removes it from the solution (keeping the directory) |

The second MAUI frontend. Step 22 ports its `Colors.xaml` / `Styles.xaml` into the AppClient
verbatim first — **the theming port must land before the project is dropped**, or the till loses its
colour scheme.

---

## What was deleted rather than listed

For the record, so nobody hunts for them:

| Removed | When | Why not merely hidden |
|---|---|---|
| `SettingsViewModel.ExecuteDeleteDb` | Cutover step 21 | Destroyed the shop's entire sales history behind one "are you sure", with no undo and no server copy |
| `SettingsViewModel.ExecuteRestoreDb` | 2026-08-10 | Overwrote the legacy file from any `.db` a user could point at, from a settings menu |
| The five store-detail edit commands | Cutover step 20 (WP6.1) | Wrote the company's own VAT number locally, so it could differ on every till in the estate |
| The store logo | Cutover step 20 | No logo field on the store-info contract — it could only ever be one machine's opinion |
| `TillStore`'s local `Barcodes` fallback | 2026-08-09 | Nothing had ever written that table; its presence implied multi-barcode support the platform has no entity for |

---

## Related

- [`till-design.md`](../till-design.md) — Part B says which of these gaps is whose work package; C1 says
  where the replacement rule lives.
- [`archive/MAUI-Cutover-Plan-2026-08-09.md`](MAUI-Cutover-Plan-2026-08-09.md) — steps 21, 22
  and 26 own L7, L8, L10 and L4.
- [`HANDOVER.md`](../../HANDOVER.md) — the living state.
