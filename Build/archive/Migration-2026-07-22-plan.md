> **📦 SUPERSEDED — nothing outstanding for the go-forward app.**
> Workstreams A and B are done; F resolved itself (NatApp left the repo). C, D, E and G were all
> about **ClientUI**, which [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md)
> recommends harvesting and retiring — so they die with it. Re-verified 2026-08-07: AppClient has
> **zero** AppCenter references (workstream C.3 never applied to it), and AutoMapper was migrated
> to **Mapster** in both apps at `9f65772`, resolving C.4 differently from the plan below.
> ⚠ Its "retarget net7 → net10" instruction is spent — both MAUI projects are already on net10.

# Plutus — Modernization & Xamarin → MAUI Migration Plan

**Date:** 2026-07-22
**Status (re-verified 2026-08-07): PARTIALLY IMPLEMENTED — A/B done, C half-done, D–G open.**

| Workstream | State |
|---|---|
| **A** Solution-wide foundation | ✅ done |
| **B** Backend + shared libs → .NET 10 | ✅ done — the whole platform runs on net10 |
| **C** ClientUI net7 → net10 | 🟡 **retarget done** (`net10.0-android/ios/maccatalyst/windows`), but **AppCenter → Sentry is not** — 30 AppCenter references remain and there are 0 Sentry references. AutoMapper is all but gone (1 reference left). |
| **D** Syncfusion / control decisions | ⬜ open (the recommendation table below still stands) |
| **E** ClientUI feature parity (FTSU, Settings, StoreOptions, add-user) | ⬜ open — none of those pages exist |
| **F** NatApp freeze → retirement | ✅ effectively done — NatApp is no longer in the repo |
| **G** Import tools (reinvestigation) | ⬜ open — the requirement was never confirmed |

⚠ **The premise has shifted.** This plan's goal was "make ClientUI the go-forward frontend". Since
then the **web till became the primary client** and a second MAUI app (`Plutus.Frontend.AppClient`,
from upstream) arrived. Read
[MAUI-Backend-Sync-Plan-2026-08-01.md](MAUI-Backend-Sync-Plan-2026-08-01.md) before acting on
workstreams C–E — it may retire ClientUI rather than finish it.
**Goal:** Bring the whole solution up to the latest .NET, and make the MAUI **ClientUI** the go-forward frontend (feature-parity with the Xamarin **NatApp**).

## Decisions locked in (from stakeholder)

| Decision | Choice |
|----------|--------|
| Target framework | **.NET 10 (LTS)** across the whole solution |
| MAUI target platforms | **Windows, Android, iOS, macOS (Mac Catalyst)** |
| Syncfusion components | **Decide per-component** (inventory + recommendation below) |
| Old Xamarin NatApp | **Frozen, then retired.** Do the *minimum* to keep it building until removal — no modernization, no multi-target investment for its sake. |
| Telemetry (replaces dead AppCenter) | **Sentry** on the client (App Insights optional on the server) |
| AutoMapper | **Remove.** Replace the 2 trivial mappings with hand-written code (recommended); Mapster (MIT) only if mapping is expected to grow. |
| CoppperToCSV | **Reference dropped** (done). Legacy-import capability to be **reinvestigated as a separate "Import tools" effort.** |

> ### ⚠️ Caveat on "keep both long-term"
> **Xamarin.Forms reached end-of-life in May 2024 and cannot run on .NET 6+.** There is no path to put NatApp on .NET 10 that isn't itself the MAUI migration. So "keep both" realistically means:
> - **NatApp is frozen** on `net5.0`/Xamarin.Forms 5 — bug/security fixes only, no framework or feature modernization.
> - **ClientUI (MAUI)** is the modernized, go-forward app that receives all new work.
> - **NatApp will ultimately be retired.** So we do not invest in keeping it modern — only the minimum to keep it building against whatever it currently consumes, until it is removed (see Critical Finding #1).

> ### ✅ Environment (ready — with a PATH caveat)
> The toolchain is installed: **.NET 10.0.302 SDK**, the **MAUI workload** (`10.0.20/10.0.100`), and .NET 10 runtimes — all at `C:\Program Files\dotnet\`.
> **Caveat:** an **x86 `dotnet.exe` (`C:\Program Files (x86)\dotnet\`, no SDK) shadows it on PATH**, so a bare `dotnet` reports "No .NET SDKs were found." Until PATH is corrected, invoke the full path **`"C:\Program Files\dotnet\dotnet.exe"`** for all build/verify steps. Fixing PATH order (x64 before x86) is a quick machine-level cleanup. (iOS/Mac Catalyst heads still require a Mac build host; only Android/Windows can build on this box.)

---

## 1. Current-state inventory

### Frontends
| Alias | Project | Framework | UI stack | Notes |
|-------|---------|-----------|----------|-------|
| **NatApp** | `Frontend/OSs/NatApp.Plutus/*` | `net5.0` + Xamarin.Forms 5.0 | Android (`v10.0`), iOS, UWP heads | In-use app. Syncfusion.Xamarin, Rg.Plugins.Popup, Xam.Plugin.Iconize, Xamarin.Essentials, AppCenter. |
| **ClientUI** | `Frontend/Plutus.Frontend.ClientUI` | `net7.0` MAUI | Android/iOS/MacCatalyst/Windows | Migration target. CommunityToolkit.Maui + .Mvvm, Syncfusion.Maui.ListView, AppCenter, AutoMapper 12. |

### Shared / backend / other
| Project | Framework | Status |
|---------|-----------|--------|
| `Commons/CommonPOSLibrary` | `netstandard2.0` | OK (consumable by both) |
| `Commons/CoppperToCSV` | **`net472`** | ⚠️ Windows-only; referenced by cross-platform ClientUI (Critical Finding #2) |
| `Commons/Plutus.Authentication` | `netstandard2.1` | OK |
| `Commons/Plutus.Contracts` | `net7.0` | → net10.0 |
| `Commons/Plutus.Entities` | `net7.0` | → net10.0 |
| `Commons/Plutus.Reports` | `net7.0` | → net10.0 |
| `Commons/Plutus.Repository` | `net7.0` | → net10.0 (⚠️ referenced by net5.0 NatApp — Critical Finding #1) |
| `Database` | `netstandard2.0` | OK (consumable by both) |
| `Database.Migrations.Startup` | `net7.0` | → net10.0 |
| `Endpoints/Plutus.DBService` | `net7.0` | → net10.0 (server API) |
| `Frontend/Plutus.Frontend.WebUI` | **`netcoreapp3.1`** | ⚠️ EOL → net10.0 |
| `Plutus Database updater` | **`netcoreapp2.0`** | ⚠️ EOL → net10.0 |
| `Tests/*` (Entities, Repository) | `net7.0` | → net10.0 |

---

## 2. Critical findings (discovered during survey — read before scoping)

These materially change the effort and must be resolved early; several suggest the solution is already in an inconsistent mid-migration state.

**#1 — Cross-TFM reference: net5.0 NatApp references net7.0 shared libs.** *(resolved — strategy below)*
`NatApp.Plutus.csproj` (`net5.0`) references `Plutus.Repository.csproj` (`net7.0`). A `net5.0` project **cannot** consume a `net7.0` assembly, so NatApp either does not currently build against the present commons, or was last built against an older state — the shared layer is already inconsistent.
**Decision (NatApp will be retired — minimum effort):** do **not** invest in multi-targeting the shared libs to keep NatApp modern. Take the **cheapest freeze**: pin NatApp against a snapshot of the shared libs it currently builds against (a small fork on a `natapp-frozen` branch, or a pinned commit), and let `master`'s libs move freely to net10. NatApp receives only the bug fixes from `BugFix-2026-07-22-plan.md`, then is removed once ClientUI reaches parity. No spike needed — the retirement decision removes the multi-target question entirely.

**#2 — `CoppperToCSV` is `net472`, referenced by cross-platform ClientUI, but never used.** *(resolved)*
It is **not** report export. It's a standalone console **Exe** (~115 lines, pure BCL) that does a **one-time legacy-data import**: reads a "Copper" POS backup (folders of `key=value&` text files — `Items`, `Transactions`, `TransactionsDrafts`, `TransactionsRefunds`, `Logs`) and writes `.csv`. ClientUI has a `ProjectReference` to it but **calls none of it** — that dead reference is what drags Windows-only `net472` into the mobile build.
**Actions:** (1) **remove the dead `ProjectReference` from ClientUI** — ✅ **done** (fixes the cross-platform break); (2) **⚠️ reinvestigate "Import tools" as a separate effort** — before rebuilding/removing CoppperToCSV, confirm whether legacy-data import (from "Copper" or any third party) is still a needed capability, what formats real customers actually migrate from, and whether it belongs as an in-app import feature (tied to the `TransferThirdPartyView` parity screen) rather than a detached console tool. Do not silently drop the capability. Report *export* (Excel) is the separate `XlsIO` path — see Workstream D (→ ClosedXML/OpenXML).

**#3 — Microsoft App Center is retired (shut down 31 March 2025).** *(resolved — Sentry)*
Both apps depend on `Microsoft.AppCenter.Analytics/Crashes/Distribute`; those packages are dead (no ingestion endpoint) — telemetry currently goes nowhere.
**Decision: Sentry** on the client — first-class MAUI SDK, covers all four targets (Firebase doesn't do Windows/Mac well), crashes + analytics + performance, closest drop-in to the AppCenter pattern. **Azure Application Insights** optional on `Plutus.DBService` for unified server+client telemetry.

**#4 — AutoMapper: remove it (usage is trivial).** *(resolved)*
AutoMapper also moved to a commercial license (v14+/2025), but the point is moot: usage is **one config with 2 `CreateMap`s and 2 `.Map` calls** (`BasketItem ⇄ BasketReturnItem`), **ClientUI-only** (backend doesn't use it). `Core/AppState.cs:137-143`, `ViewModels/MainTill/TillViewModel.cs:238,265`, `Core/IAppState.cs:22`.
**Decision: drop AutoMapper**, replace with two hand-written mapping methods. NatApp's AutoMapper 10 stays as-is (frozen).
**On tracking:** any mapping *library* (Mapster included) is an ongoing dependency — version bumps, security advisories, and a compatibility recheck on every future .NET upgrade. Hand-written mapping for two sibling types has **nothing to track** and cannot break on a framework bump, which is why it's recommended here. If mapping later grows substantially, adopt **Mapster** — its version would then live in the central `Directory.Packages.props` (one line) alongside every other package.

**#5 — Two WebUI trees.** *(covered by the freeze decision)*
`Frontend/Plutus.Frontend.WebUI` (in the `.sln`, EOL `netcoreapp3.1`) *and* a bare `Plutus/Plutus.Frontend.WebUI/ClientApp`. Confirm which is live before spending effort; modernize the live one to net10 or retire the dead one.

---

## 3. Target-state architecture

- **One SDK pin:** add a root `global.json` pinning the .NET 10 SDK for reproducible builds/CI.
- **Central package management:** add `Directory.Packages.props` (CPM) so every project's package versions are defined once — essential given the number of projects and the duplicated version strings today.
- **Shared library policy:** libraries consumed by NatApp multi-target `netstandard2.0;net10.0`; everything else single-targets `net10.0`.
- **Frontends:**
  - ClientUI → `net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-windows10.0.x` MAUI.
  - NatApp → unchanged `net5.0` Xamarin, frozen.
- **Server (`Plutus.DBService`) → net10.0**, EF Core 10.

---

## 4. Workstreams

### Workstream A — Solution-wide foundation (do first, unblocks everything)
1. Verify actual build state of NatApp and ClientUI as-is (baseline).
2. Add `global.json` (.NET 10 SDK) + `Directory.Packages.props` (CPM) + `Directory.Build.props` (shared `LangVersion`, nullable, etc.).
3. **Toolchain is present** (.NET 10.0.302 SDK + MAUI workload). Use `"C:\Program Files\dotnet\dotnet.exe"` until the x86/x64 PATH shadowing is fixed. Establish the build baseline (Android + Windows heads on this box; iOS/Mac need a Mac host).
4. Fix **Critical Finding #2**: dead `CoppperToCSV` `ProjectReference` removed from ClientUI (✅ done). **Do not rebuild the tool yet** — the "Import tools" capability is a separate reinvestigation (Finding #2 / Workstream G).
5. Resolve **Critical Finding #1**: NatApp is frozen on a pinned snapshot of its current libs; `master` moves to net10 unhindered. No multi-target work (NatApp is being retired).

### Workstream B — Backend & shared libraries → .NET 10
1. Bump `Plutus.Contracts`, `Plutus.Entities`, `Plutus.Reports`, `Plutus.Repository`, `Database.Migrations.Startup`, `Plutus.DBService`, Tests → `net10.0`.
2. Upgrade **EF Core → 10** (from 3.1/7.x). Review migrations, provider packages (Sqlite on client, server provider), and breaking API changes.
3. Upgrade ASP.NET Core `Plutus.DBService` to net10 (minimal hosting, `Microsoft.AspNetCore.JsonPatch`, JWT/`Microsoft.Identity` packages → current).
4. Modernize the two EOL utilities: `Plutus.Frontend.WebUI` (`netcoreapp3.1`) and `Plutus Database updater` (`netcoreapp2.0`) → net10 (or retire if dead — see Finding #5).
5. Run the test suite (`Plutus.Entities.Tests`, `Plutus.Repository.Tests`) green on net10 before touching frontends.

### Workstream C — ClientUI MAUI net7 → net10
1. Retarget `net7.0-*` → `net10.0-*` for all four platform TFMs; bump `SupportedOSPlatformVersion`s to net10-supported minimums.
2. Upgrade MAUI workloads and packages: `CommunityToolkit.Maui` 3.1 → net10-compatible, `.Mvvm` 8.0 → current, `.Maui.Markup`.
3. Replace **AppCenter** with the **Sentry** MAUI SDK (Finding #3). AppCenter is woven through ~15 files — centralize the replacement behind the existing `Services/Analytics` abstraction so most call sites don't change. Concrete touch-points:
   - Init: `App.xaml.cs:29-30` (`AppCenter.Start(...)` → `SentrySdk.Init(...)` / `.UseSentry()` in `MauiProgram`); add DSN to `Configuration/appsettings*.json`.
   - Crash reporting: `Crashes.TrackError(...)` at `Services/Repository/BusinessRepository.cs:79`, `EmployeeRepository.cs:59,108,123,144`, `ViewModels/LoginViewModel.cs:67`, `ViewModels/MainTill/TillViewModel.cs:771`, `Platforms/Windows/Helpers/DeviceHelpers.cs:35` → `SentrySdk.CaptureException(...)`.
   - Analytics: `Services/Analytics/Logger.cs:30,39` (`Analytics.TrackEvent`) → Sentry breadcrumbs/`CaptureMessage`.
   - Remove `AppState.CheckAppCenter()` (`Core/AppState.cs:145-186`) and the install-id call, or reimplement against Sentry.
   - Remove all three `Microsoft.AppCenter.*` packages.
   - *Staged: apply once a .NET SDK is available to build (see Environment prerequisite).*
4. Remove **AutoMapper** (Finding #4): delete the package + `MapperConfiguration` in `Core/AppState.cs:137-143`, replace the 2 `BasketItem⇄BasketReturnItem` maps (`ViewModels/MainTill/TillViewModel.cs:238,265`) with hand-written methods, drop `IMapper Mapper` from `Core/IAppState.cs:22` and `Core/AppState.cs:119`.
5. Upgrade `Microsoft.Identity.Client`, `System.IdentityModel.Tokens.Jwt` (6.25 → 8.x), config binder packages.
6. Address MAUI net8/9/10 breaking changes (handler/lifecycle API changes, `[QueryProperty]`, disposal, Windows packaging).
7. Rebuild & smoke-test each platform head.

### Workstream D — Syncfusion / control decisions (per-component)
Inventory of every Syncfusion usage in NatApp and the recommended MAUI approach. ClientUI already uses `Syncfusion.Maui.ListView`, so a Syncfusion **Community/paid license is likely already required** — that tilts several rows toward "stay on Syncfusion.Maui."

| Component (NatApp) | Used in | MAUI options | Recommendation |
|--------------------|---------|--------------|----------------|
| **SfListView** | `ViewAllView`, `InputMultiSelectAlert`, context-menu behavior | Built-in `CollectionView` (free) **or** `Syncfusion.Maui.ListView` (already referenced) | Use **CollectionView** where possible (free, native, supports reorder — ties into Bug 2 fix); keep Syncfusion.Maui.ListView only where context-menu/swipe is needed. |
| **SfChart** (cartesian) | `SalesReportsView` sales charts | `Syncfusion.Maui.Charts`, `Microcharts.Maui` (free), `LiveChartsCore` (free) | **Syncfusion.Maui.Charts** if license already in play; else **LiveChartsCore** (best free feature set). |
| **SfSunburstChart** | `StockOuttakeView` | `Syncfusion.Maui` sunburst; no strong free equivalent | **Syncfusion.Maui** (free alternatives for sunburst are weak). |
| **SfCalendar** (range) | `SalesReportsView`, `StockOuttakeView` | Built-in `DatePicker`, `CommunityToolkit`, `Syncfusion.Maui.Calendar` | Two `DatePicker`s or **Syncfusion.Maui.Calendar** for range UX. |
| **SfNumericUpDown** | `TillView` (qty) | `Syncfusion.Maui.Inputs` `SfNumericEntry`, or `Stepper`+`Entry` | Built-in **`Stepper`/`Entry`** (trivial, free). |
| **SfPicker** | `TillView` | Built-in `Picker` | Built-in **`Picker`**. |
| **SfPopupLayout** | `AddEditView`, context-menu behavior | `CommunityToolkit.Maui` `Popup` (already referenced) | **CommunityToolkit Popup** (already a dependency). |
| **XlsIO / ExcelEngine** (Excel export) | `SalesReportsViewModel` | `Syncfusion.XlsIO.NET`, **`ClosedXML`** (free), `DocumentFormat.OpenXml` (free, already referenced!) | **ClosedXML** or **OpenXML** (free; OpenXML is already a NatApp dependency). |

Also replace Xamarin-only plugins in the parity work: `Rg.Plugins.Popup` → CommunityToolkit Popup; `Xam.Plugin.Iconize` → MAUI FontImageSource/font glyphs; `Xamarin.Essentials` → `Microsoft.Maui.Essentials` (built in); `Xamarin.Plugin.FilePicker` → `CommunityToolkit`/`FilePicker` essentials.

### Workstream E — Feature parity (build the missing ClientUI screens)
ClientUI is missing several screens that NatApp has. These block ClientUI becoming the primary app:
- **First-Time-Startup flow** — `FTSUMainView`, `SetupView`, `RecoveryView`, `TransferThirdPartyView` (onboarding/setup/data transfer). None exist in ClientUI.
- **Settings** — `SettingsView`. No ClientUI equivalent.
- **StoreOptions** — `StoreOptionsView`. No ClientUI equivalent.
- **Add-user / logged-users screen** — already tracked in `BugFix-2026-07-22-plan.md` (Bug 1); `AuthorisationPage` exists in DI but is never shown.

Fold the three existing bugs from `BugFix-2026-07-22-plan.md` into this workstream for ClientUI (they touch the same screens).

### Workstream F — NatApp freeze → retirement
- Pin NatApp against a snapshot of its current shared libs (frozen branch/commit); no framework or package modernization.
- Apply only the NatApp bug fixes from `BugFix-2026-07-22-plan.md`.
- Document it as frozen; all new features go to ClientUI only.
- **Retire** it once ClientUI reaches parity (Workstream E) and is validated in production.

### Workstream G — Import tools (reinvestigation)
- Separate, deferred effort triggered by dropping CoppperToCSV. **Do not silently lose the legacy-import capability.**
- Determine whether legacy-data import (from "Copper" or other third-party POS) is still required, and which source formats real customers actually migrate from.
- Decide the right home: an **in-app import feature** tied to the `TransferThirdPartyView` parity screen (Workstream E), vs a standalone modern console/CLI tool. Prefer in-app if customers self-serve.
- Rebuild on `net10.0` with free libraries (CSV/OpenXML) — only after the requirement is confirmed.

---

## 5. Suggested sequencing (phases)

1. **Phase 0 — Baseline & foundation (A):** confirm current build state; add `global.json`/CPM; fix `CoppperToCSV` TFM; decide the shared-lib multi-target strategy. *Gate: NatApp still builds.*
2. **Phase 1 — Backend to net10 (B):** shared libs + server + EF Core 10 + tests green. Lowest UI risk, unblocks both frontends.
3. **Phase 2 — ClientUI to net10 (C + D):** retarget MAUI, replace dead deps (AppCenter, AutoMapper decision), resolve Syncfusion per the table. *Gate: all four heads build & smoke-test.*
4. **Phase 3 — Parity (E):** build FTSU, Settings, StoreOptions, add-user; fold in the ClientUI bug fixes.
5. **Phase 4 — NatApp freeze (F):** apply NatApp bug fixes, document freeze.
6. **Phase 5 — Cutover:** ClientUI becomes primary; NatApp retained as fallback per the "keep both" decision.

Each phase branches off `master`; small, reviewable commits.

---

## 6. Risks & testing

- **Cross-TFM shared libs (Finding #1)** — highest risk; can break NatApp the moment commons move to net10. Mitigate via multi-targeting; verify NatApp build at every commons change.
- **EF Core 3.1/7 → 10** — migration/behavioral breaking changes; run migrations against a copy of prod data.
- **Dead telemetry (AppCenter)** — silent data loss today; verify replacement actually ingests before cutover.
- **MAUI multi-head build** — iOS/Mac require a Mac build host; Windows packaging cert (`.pfx` referenced in csproj) must be valid.
- **Licensing** — Syncfusion + AutoMapper both have license implications; confirm before shipping.
- **Testing:** unit tests green on net10 (Phase 1 gate); per-platform manual smoke test of ClientUI (Phase 2 gate); parity acceptance test each rebuilt screen against NatApp behavior (Phase 3).

---

## 7. Open decisions to confirm before implementation

Resolved by stakeholder: NatApp **frozen → retired** with minimum effort (#1/#5); telemetry → **Sentry** (#3); AutoMapper → **remove, hand-write** (#4); CoppperToCSV → **dropped; import capability deferred to Workstream G** (#2).

Still open:
1. **PATH cleanup (minor):** put x64 `C:\Program Files\dotnet\` ahead of the x86 entry so a bare `dotnet` resolves the SDK. Not a blocker — full-path invocation works today.
2. **Sentry DSN / account** — need a Sentry project + DSN before the rewire can be wired and tested.
3. **Syncfusion license:** is a Community/paid license already held? (Decides several Workstream D rows — ClientUI already uses `Syncfusion.Maui.ListView`, so likely yes.)
4. **Import tools (Workstream G):** is legacy third-party import still a required capability, and from which formats?
5. **WebUI (Finding #5):** which WebUI tree is live; modernize to net10 or retire?
6. **Platform build infra:** is a Mac build host / Windows signing cert available for the four-platform target?
7. **Server telemetry:** add App Insights on `Plutus.DBService`, or Sentry there too for one vendor?
