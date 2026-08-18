# Shrink the MAUI build

**Written:** 2026-08-18, from measurements taken against `D:\tmp\plutus-till-1.74.0`
(`1.74.0+1e456511`, the unpackaged Release publish).
**Status:** ⬜ **Nothing done.** Measured and sequenced only — deliberately not started, see §5.

> **Matt, 2026-08-18:** *"Is there anything we can do to clean up the folders within the build? It
> looks like everything is just dumped into the root folder unless its a language?"*
>
> Both halves of that observation are accurate. The answer is that **the visible mess and the real
> waste are different things**, and only one of them is worth touching:
>
> - The **119 language folders** are 4.6 MB — **1.7%** of the build. Tidying them buys nothing.
> - **~91 MB — 35% of the build — is a licensed dependency and an Excel library serving screens no
>   operator can open.** That is the actual prize.
> - The **flat root** cannot be tidied at all. It is the .NET loader's requirement, not our mess.

## ⚠ Where this sits relative to the other documents

**This is a packaging document, not a second MAUI status board.** The standing rule is that
[`MAUI-retrofit.md`](MAUI-retrofit.md) is the one MAUI document and nothing competes with it.

| For | Go to |
|---|---|
| Whether the L4 screens live or die, and Matt's ruling on it | [`MAUI-retrofit.md`](MAUI-retrofit.md) **§10 L4** — ⚠ **it wins on any disagreement with this page** |
| Which Syncfusion control was replaced by what, and the licence/trial-dialog hazard | [`../syncfusion-footprint.md`](../syncfusion-footprint.md) |
| What is deployed / built right now | [`MAUI-retrofit.md`](MAUI-retrofit.md) **§0.1** |
| **The size of the artefact, what is in it, and the order to remove things** | here |

⚠ **This page must never carry a status claim about a capability.** If it starts to, fold it into
`MAUI-retrofit.md` and delete it — that is what happened to the five documents §0 was consolidated
from, and the splintering began exactly this way: a document written to answer a question the others
could not.

---

## 1. The measurements

`D:\tmp\plutus-till-1.74.0` — **264 MB**, **299 files in the root**, **119 subfolders**.

### 1.1 Root files, by type

| Type | Count | Size |
|---|---|---|
| `.dll` | 179 | **252 MB** |
| `.winmd` | 43 | 2.7 MB |
| `.pdb` | 10 | 532 KB |
| `.png` | 51 | 254 KB |
| everything else (`json`, `pri`, `ttf`, `exe`, `ico`) | 16 | small |

### 1.2 The ten biggest files — and who needs them

| Size | File | Needed by |
|---|---|---|
| 24 MB | `Microsoft.Windows.SDK.NET.dll` | WinUI — **required** |
| 17 MB | `Syncfusion.XlsIO.NET.dll` | ⛔ hidden L4 export only |
| 17 MB | `Syncfusion.XlsIO.Portable.dll` | ⛔ hidden L4 export only |
| 16 MB | `Microsoft.WinUI.dll` | **required** |
| 15 MB | `DocumentFormat.OpenXml.dll` | ⛔ **nothing in the till** — see §3 |
| 15 MB | `Microsoft.ui.xaml.dll` | **required** |
| 14 MB | `Syncfusion.Pdf.Portable.dll` | ⛔ hidden L4 export only |
| 11 MB | `libSkiaSharp.dll` | MAUI rendering — **required** |
| 6.4 MB | `Microsoft.EntityFrameworkCore.dll` | till store — **required** |
| 6.0 MB | `Syncfusion.XlsIORenderer.Portable.dll` | ⛔ hidden L4 export only |

### 1.3 The totals that matter

| | Size | Share | Reachable from a screen an operator can open? |
|---|---|---|---|
| **Syncfusion** (24 DLLs) | **76 MB** | 29% | **No** |
| **DocumentFormat.OpenXml** | **15 MB** | 6% | **No — not used by the till at all** |
| Windows App SDK / WinUI / Skia | ~66 MB | 25% | Yes |
| EF Core (+ relational, + Sqlite) | ~12 MB | 5% | Yes |
| Everything else | ~95 MB | 36% | Yes |

---

## 2. The 119 language folders — measured, and NOT worth touching

**4.6 MB total.** None of them is ours:

| Folders | Contents | Comes from |
|---|---|---|
| ~85 | `Microsoft.ui.xaml.dll.mui`, `Microsoft.UI.Xaml.Phone.dll.mui` | Windows App SDK **native** resources |
| 34 | `Microsoft.Maui.Controls.resources.dll` | MAUI framework strings |
| 2 | `Syncfusion.Maui.Inputs/Buttons.resources.dll` | Syncfusion |

⚠⚠ **CHECKED BEFORE RECOMMENDING ANYTHING, BECAUSE DELETING THEM WAS THE OBVIOUS MOVE AND WOULD HAVE
BEEN WRONG IF THE TILL'S OWN TRANSLATIONS LIVED THERE.** They don't: `Plutus/Shared/I18N_L10N` has a
**single** `Resx/AppResources.resx`, declared `<EmbeddedResource>` — so it compiles into the main
assembly and there is no `I18N_L10N.resources.dll` anywhere in the output. The till is English-only
and satellite assemblies play no part in `.Translate()`.

**So `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` is safe** — it removes the 34 MAUI
and 2 Syncfusion folders (**~600 KB, 36 of the 119**). Framework exception text becomes English-only,
which it already effectively is.

⚠ The ~85 `.mui` folders are **native** Win32 resources shipped as package content by the Windows App
SDK. `SatelliteResourceLanguages` does not touch them and there is no supported switch that does.
Excluding them with a hand-written MSBuild target means fighting the SDK's own asset list for 4 MB —
**don't**.

**Verdict: one line for 600 KB and a less cluttered listing. Cosmetic. Do it alongside something
else, never as a job of its own.**

---

## 3. ⚠ `DocumentFormat.OpenXml` — 15 MB the till ships and never calls

**The lowest-risk 15 MB in the build.**

```
Plutus.Frontend.AppClient.csproj:102
  <PackageReference Include="DocumentFormat.OpenXml" Version="2.13.0" />
```

⚠⚠ **THERE ARE TWO `ExcelHandling` CLASSES IN THIS SOLUTION, AND THE TILL SHIPS BOTH LIBRARIES.**

| Class | Library | Called by |
|---|---|---|
| `Plutus/Commons/Plutus.Reports/ExcelHandling.cs` | `DocumentFormat.OpenXml` (15 MB) | the **BACKEND** — `src/Plutus.Sales/SaleController.cs` |
| `Plutus/Frontend/.../Helpers/FileIO/ExcelHandling.cs` | Syncfusion `XlsIO` (54 MB) | `SalesReportsViewModel.cs:512` — a **hidden L4 screen** |

Two writers, one class name, and the till carries the weight of both. **Nothing in the AppClient
references `DocumentFormat`, `OpenXml` or `SpreadsheetDocument`** — verified by grep across
`Plutus/` and `src/`; every hit is in `Plutus.Reports`, which the till does not reference.

⚠ Also worth noticing: it is pinned at **2.13.0** while the rest of the solution is current. That is
the signature of a reference nobody has looked at in years.

**Action:** delete line 102. Rebuild, confirm `DocumentFormat.OpenXml.dll` is gone from the publish
output and the app still starts. **~15 MB, one line, no screen changes.**

⚠ **Do not remove `Plutus.Reports` itself** — the backend needs it, and the class name collision
makes it easy to grep the wrong one.

---

## 4. Syncfusion — 76 MB, in three tiers of increasing risk

Full context in [`../syncfusion-footprint.md`](../syncfusion-footprint.md). **As of till 1.33.0 no
Syncfusion control sits on any screen an operator can reach** — verified again 2026-08-18: every
mention in `TillView.xaml`, `TillViewModel.cs`, `ViewAllView.xaml` and `InputMultiSelectAlert.cs` is
now a **comment** recording the removal. The genuine code references are exactly six:

| Site | What |
|---|---|
| `App.xaml.cs:44` | `SyncfusionLicenseProvider.RegisterLicense` |
| `MauiProgram.cs:8` | `ConfigureSyncfusionCore` |
| `Helpers/FileIO/ExcelHandling.cs:6` | `Syncfusion.Compression.Zip` |
| `SalesReportsViewModel.cs:9,10` | `Maui.Calendar`, `Maui.Charts` — **hidden L4** |
| `StockOuttakeViewModel.cs:6` | `Maui.Calendar` — **hidden L4** |

### 4.1 ⚠ Five packages are referenced by NOTHING

Removed at 1.33.0 with the controls; the `PackageReference` lines were never taken out.

| Package | Files referencing it |
|---|---|
| `Syncfusion.Maui.Inputs` | **0** |
| `Syncfusion.Maui.ListView` | **0** |
| `Syncfusion.Maui.Picker` | **0** |
| `Syncfusion.Maui.Popup` | **0** |
| `Syncfusion.Maui.SunburstChart` | **0** |

Still referenced, all by hidden screens: `Calendar` (4 files), `Charts` (2), `Core` (1), plus
`XlsIO` / `XlsIORenderer`.

⚠ **Measure the saving, do not assume it.** Some of those DLLs arrive transitively through packages
that stay — `Syncfusion.Maui.Sliders.dll` (748 KB) comes via `Syncfusion.Maui.Inputs` **and**
`Syncfusion.Maui.GridCommon`, per `Plutus.Frontend.AppClient.deps.json`. The honest method is: remove
the five lines, republish to a **scratch folder**, and diff the file list against 1.74.0's.

### 4.2 The tiers

| Tier | Work | Frees | Risk |
|---|---|---|---|
| **A** | Drop the 5 unreferenced `PackageReference` lines | measure — the 5 own DLLs are ~2 MB, but see §4.1 | Low. ⚠ Not zero — see §5 |
| **B** | Drop `XlsIO` + `XlsIORenderer` **and** the Excel export they serve | **~54 MB** (XlsIO 34 MB + Pdf 17 MB + Compression/Metafile) | Medium — deletes a feature of a hidden screen |
| **C** | Full removal: L4's three viewmodels + views, `ExcelHandling.cs`, the licence registration, `ConfigureSyncfusionCore`, all 10 packages | **76 MB** → build ≈ **173 MB, a third smaller** | ⛔ **Blocked — see below** |

### 4.3 ⛔ Why tier C is blocked, and the order that must not be got wrong

**Matt, 2026-08-17:** *"Do not drop anything. I have a more recent DB to import and will need to
translate where required and retain all legacy sales."* → `MAUI-retrofit.md` §10 L4: the screens stay
hidden and **stay in the build**, because they are the only reader of a migrated till's local
pre-cutover file.

⚠⚠ **AND THE ORDER IS LOAD-BEARING.** From `syncfusion-footprint.md`: **removing the licence
registration while a licensed control still exists in the assembly turns a dormant screen into a
trial-dialog screen** — worse than leaving it alone, because a modal with no way back on a till is
how a shop floor loses a counter. The sequence is: **delete the screens → delete `ExcelHandling.cs`
→ delete the packages → delete the licence registration and `ConfigureSyncfusionCore`.** Never the
reverse, never partially.

---

## 5. ⚠⚠ Why nothing here was done on 2026-08-18, and when to do it

**Till 1.74.0 was built for Matt to hand-test, and this work would change the artefact under him.**

⚠ **A green build does not prove a dependency removal is safe in this project**, and the csproj says
so itself at line 40: *"a bad property or a Syncfusion type that doesn't exist built clean and [only
failed when the screen was] actually opened."* XAML type resolution here is a **runtime** event. So
the only real proof is opening screens — which is precisely what the hand-run does.

**Therefore:**

1. **Run the hand-run on 1.74.0 first** ([`../Test Maui.md`](../Test%20Maui.md)) — especially §G and
   the new §W9d.
2. **Then** §3 (`DocumentFormat.OpenXml`, ~15 MB) and §4.1 tier A together, as one version bump.
3. **Re-verify the artefact**, not the build log: version string, then **open the Till, Inventory →
   View all items, and Statistics** — the three screens whose XAML once held Syncfusion controls.
4. Tier B only if Matt wants the hidden Excel export gone. Tier C only if L4's ruling changes.

**Expected end state:** ~249 MB after step 2 (from 264 MB), ~173 MB if C is ever unblocked.

---

## 6. What is NOT worth doing, so nobody spends a day on it

| Idea | Why not |
|---|---|
| **Move the 179 root DLLs into subfolders** | .NET's loader requires assemblies beside the app host. Doing it means `additionalProbingPaths` in `runtimeconfig.json` — a classic "works on the dev box, fails on the shop PC". **The flat root is the loader's requirement, not mess.** |
| **Delete the `.mui` language folders** | 4 MB, and they are Windows App SDK package content. Fighting the SDK's asset list for 1.5% of the build. |
| **Strip the `.pdb` files** | 532 KB — nothing. And they are what gives `CrashLog.Write` a stack trace with **file and line numbers**, which is how a hand-run finding becomes a fix. **Keep them.** |
| **`PublishTrimmed`** | Not supported for WinUI/MAUI Windows apps, and trimming reflection-driven XAML binding is how a screen renders blank in production and nowhere else. |
| **Chase the remaining ~95 MB of "everything else"** | It is the framework: 179 DLLs, each small. There is no single win in it. |
