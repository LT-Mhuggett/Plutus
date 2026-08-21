# Shrink the MAUI build

**Written:** 2026-08-18, from measurements taken against `D:\tmp\plutus-till-1.74.0`
(`1.74.0+1e456511`, the unpackaged Release publish).
**Status:** ✅ **TIER C DONE 2026-08-20 — till 1.110.0. 264 MB → 169 MB.**

> ## ✅ DONE — SYNCFUSION AND `DocumentFormat.OpenXml` ARE OUT (2026-08-20, till 1.110.0)
>
> Matt: *"if the packaging of it removes all you see, what about removing syncfusion now? Worth it?"*
> — and the answer was yes, for the **licence hazard** rather than the megabytes. Measured, not estimated:
>
> | | 1.109.0 | 1.110.0 | |
> |---|---:|---:|---|
> | Publish size | 264 MB | **169 MB** | **−95 MB, −36%** |
> | Root files | 299 | **268** | −31 |
> | Subfolders | 121 | **88** | −33 |
> | Syncfusion DLLs | 24 | **0** | |
> | `DocumentFormat.OpenXml` | 15 MB | **0** | |
>
> **−95 MB beat the −91 MB this document predicted**, because five of the ten packages also dragged in
> transitive DLLs that left with them — which is exactly why §4.2 said *measure, do not assume*.
>
> ### ⚠⚠ WHY IT WAS UNBLOCKED — L4's condition had quietly been MET
>
> L4 kept these screens because they were *"the only reader of a migrated till's local pre-cutover
> file"*, on Matt's ruling: *"Do not drop anything. I have a more recent DB to import and will need to
> translate where required and retain all legacy sales."* **Both halves are now satisfied**: the 19_08
> import ran on 2026-08-20, and `salesv2` holds **21,914 sales going back to 2019-01-23** — the whole
> NatApp history, in the platform, where the portal reads it. The screens read only the local
> pre-Plutus file, so they showed **zero** for everything sold since cutover.
>
> ### ⚠⚠ AND §3 OF THIS DOCUMENT WAS WRONG — the correction matters more than the saving
>
> §3 claimed `DocumentFormat.OpenXml` was *"15 MB the till ships and never calls"*, that *"nothing in
> the AppClient references DocumentFormat, OpenXml or SpreadsheetDocument — **verified by grep**"*, and
> that the fix was *"delete line 102, one line, no screen changes."*
>
> **That grep result did not hold.** `Helpers/FileIO/ExcelHandling.cs` opened with
> `using DocumentFormat.OpenXml.Packaging` and was built on `SpreadsheetDocument`. **Deleting line 102
> alone would not have compiled.** The same table also credited that file to Syncfusion `XlsIO`; it used
> OpenXml for the document and Syncfusion only for `Compression.Zip`.
>
> The real shape: **`SalesReportsViewModel` had TWO independent Excel export paths** — `ExcelEngine`/
> `IWorkbook` at line 355 (Syncfusion XlsIO) and `new ExcelHandling()` at line 512 (OpenXml). One hidden
> screen, and the till carried **69 MB** for it. So OpenXml was never the low-risk standalone win; it was
> coupled to L4 exactly like XlsIO, and it came out with it.
>
> ⚠ **The lesson: "verified by grep" is a claim, and this one was false.** It was written down
> confidently enough that it was very nearly acted on as a one-line change.
>
> ### What was done, in the order that mattered
>
> **screens → `ExcelHandling.cs` → packages → licence registration.** Deleting the screens FIRST is what
> discharged the trial-dialog hazard: with no licensed control left in the assembly, the key had nothing
> to license, so removing `RegisterLicense` and `ConfigureSyncfusionCore` could not turn a dormant
> screen into a modal. The last two had to come out **with** the packages, being mutually dependent for
> compilation.
>
> 1. Deleted `SalesReportsView(Model)` and `StockOuttakeView(Model)` — ⚠ both **already unreachable**:
>    their `Open*Command`s existed but nothing bound them, the `buttons` list has held only *Reprint* since
>    2026-08-10. Also removed those two orphaned commands and rewrote the on-screen note, which told the
>    operator the reports were *"hidden"* — now a lie about something that does not exist.
> 2. Deleted `Helpers/FileIO/ExcelHandling.cs` and its 4 tests (`ExcelHandlingTests`). Suite 625 → **621**.
> 3. Removed **10** Syncfusion `PackageReference`s and `DocumentFormat.OpenXml`.
> 4. Removed `SyncfusionLicenseProvider.RegisterLicense` (**the stale key is deleted**) and
>    `ConfigureSyncfusionCore`.
> 5. Set `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` — **33 fewer folders**, 0 satellite
>    folders left (2 of the 35 also held `.mui` files and therefore remain).
>
> ### ✅ Verified, and how far that verification actually goes
>
> Release build **0 errors** · MAUI suite **621 pass / 3 skipped** · artefact carries **0** Syncfusion and
> **0** OpenXml DLLs · version **1.110.0** stamped (no stale 1.109.0 string) · **the app was launched and
> ran** for 25 s, its log opening `Plutus MAUI till v1.110.0` with **no licence error and no XAML or
> resource failure** — the only exception is the pre-existing `LoginViewModel.EnsureStoreAsync` fault
> already tracked in `MAUI-retrofit.md` §0.3.
>
> ⚠⚠ **A CLEAN BUILD PROVES ALMOST NOTHING HERE and a 25-second launch is not a shift.** XAML type and
> resource failures surface on **navigation**, not at compile time. The screens whose controls were
> swapped out in the 1.33.0 work (§4.5) have no automated coverage at all. **§G68 is the hand-run** and it
> is what turns this from *built* into *known good*.
>
> ### ⬜ What is still not done
>
> - **86 native `.mui` folders, 3.8 MB.** Not removed, and ⚠ **they cannot be MOVED into a `languages/`
>   folder either** — Matt asked. They appear **nowhere in `deps.json`**; the Win32 MUI loader probes
>   `<the DLL's own directory>\<culture>\<name>.dll.mui`, so there is no manifest to edit and no
>   redirect. Moving them would fail **silently** on a non-English Windows and look perfect here.
> - **The 268 root files, which is the thing Matt actually asked about.** See §6.

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
[`MAUI-retrofit.md`](../To%20do/MAUI-retrofit.md) is the one MAUI document and nothing competes with it.

| For | Go to |
|---|---|
| Whether the L4 screens live or die, and Matt's ruling on it | [`MAUI-retrofit.md`](../To%20do/MAUI-retrofit.md) **§10 L4** — ⚠ **it wins on any disagreement with this page** |
| Which Syncfusion control was replaced by what, and the licence/trial-dialog hazard | **§4 here** — ⚠ merged in 2026-08-20 from `syncfusion-footprint.md`, now [archived](../archive/syncfusion-footprint.md) |
| What is deployed / built right now | [`MAUI-retrofit.md`](../To%20do/MAUI-retrofit.md) **§0.1** |
| The hand-test to give a person | [`Build/Test Maui.md`](../Test%20Maui.md) |
| **The size of the artefact, what is in it, the Syncfusion footprint, and the order to remove things** | here |

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

## 3. ~~⚠ `DocumentFormat.OpenXml` — 15 MB the till ships and never calls~~ ✅ REMOVED 2026-08-20

> ⚠⚠ **EVERYTHING BELOW THIS LINE IS WRONG, AND IS KEPT ONLY AS THE RECORD OF A FALSE "VERIFIED BY
> GREP".** The till DID call it: `Helpers/FileIO/ExcelHandling.cs` was built on `SpreadsheetDocument`,
> so *"delete line 102, one line, no screen changes"* **would not have compiled**. The table below also
> credits that file to Syncfusion `XlsIO` — it used OpenXml for the document and Syncfusion only for
> `Compression.Zip`. `SalesReportsViewModel` had **two** export paths, not one.
>
> It came out on 2026-08-20 **with** the Syncfusion tier C work, which is where it always belonged. See
> the banner at the top of this document.

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

> ⚠ **Merged in 2026-08-20 from `syncfusion-footprint.md`**, which is now archived. It was a second
> document about the same 76 MB — what the controls held up and what replaced them — and the removal
> order in §4.3 depends entirely on its analysis. One subject, one document.

### 4.0 Why this is a removal and not a renewal

**Syncfusion licence keys are version-specific.** The packages are on **34.1.32**; the key registered
in `App.xaml.cs` predates that. A licensed control that renders therefore puts a **modal in front of
the page** — and on `SalesReportsView` that page had no way back, which is how a shop floor loses a
till.

⚠⚠ **THERE IS NO KEY COMING.** Matt, 2026-08-10, on reading what Syncfusion actually held up:
*"I am not going to renew Syncfusion, it seems like it can be replaced."* So the only route is
removal, and the licence hazard is why §4.3's order is load-bearing rather than a preference.

⚠ **Do not add a Syncfusion control to a live screen.** No key will license it, and the failure is a
modal on the shop floor, **not a build error**. `App.xaml.cs` carries the same warning.

### 4.1 What is left, and where

**As of till 1.33.0 no Syncfusion control sits on any screen an operator can reach** — verified again
2026-08-18: every mention in `TillView.xaml`, `TillViewModel.cs`, `ViewAllView.xaml` and
`InputMultiSelectAlert.cs` is now a **comment** recording the removal. The genuine code references are
exactly six:

| Site | What |
|---|---|
| `App.xaml.cs:44` | `SyncfusionLicenseProvider.RegisterLicense` |
| `MauiProgram.cs:8` | `ConfigureSyncfusionCore` |
| `Helpers/FileIO/ExcelHandling.cs:6` | `Syncfusion.Compression.Zip` |
| `SalesReportsViewModel.cs:9,10` | `Maui.Calendar`, `Maui.Charts` — **hidden L4** |
| `StockOuttakeViewModel.cs:6` | `Maui.Calendar` — **hidden L4** |

### 4.2 ⚠ Five packages are referenced by NOTHING

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

### 4.3 The tiers

| Tier | Work | Frees | Risk |
|---|---|---|---|
| **A** | Drop the 5 unreferenced `PackageReference` lines | measure — the 5 own DLLs are ~2 MB, but see §4.1 | Low. ⚠ Not zero — see §5 |
| **B** | Drop `XlsIO` + `XlsIORenderer` **and** the Excel export they serve | **~54 MB** (XlsIO 34 MB + Pdf 17 MB + Compression/Metafile) | Medium — deletes a feature of a hidden screen |
| **C** | Full removal: L4's three viewmodels + views, `ExcelHandling.cs`, the licence registration, `ConfigureSyncfusionCore`, all 10 packages | **76 MB** → build ≈ **173 MB, a third smaller** | ⛔ **Blocked — see below** |

### 4.4 ⛔ Why tier C is blocked, and the order that must not be got wrong

**Matt, 2026-08-17:** *"Do not drop anything. I have a more recent DB to import and will need to
translate where required and retain all legacy sales."* → `MAUI-retrofit.md` §10 L4: the screens stay
hidden and **stay in the build**, because they are the only reader of a migrated till's local
pre-cutover file.

⚠⚠ **AND THE ORDER IS LOAD-BEARING** (§4.0): **removing the licence registration while a licensed
control still exists in the assembly turns a dormant screen into a trial-dialog screen** — worse than
leaving it alone, because a modal with no way back on a till is how a shop floor loses a counter. The
sequence is: **delete the screens → delete `ExcelHandling.cs` → delete the packages → delete the
licence registration and `ConfigureSyncfusionCore`.** Never the reverse, never partially.

### 4.5 What each swap ALREADY cost — the reference half

The 1.33.0 removal is done and live. This table is here because **every one of these swaps moved a
rule out of a control's markup and into our code**, and that is the recurring hazard when the rest of
tier C is finally done.

| Was | Where | Now | ⚠ What the swap cost |
|---|---|---|---|
| `SfNumericEntry` | **`TillView` — the QUANTITY box**, the selling path | `Entry` + explicit − / + buttons | ⚠⚠ `Minimum="1"` lived in the CONTROL'S MARKUP. It now lives in `TillViewModel.Quantity`'s setter — **a quantity of 0 adds a basket line that charges nothing and looks exactly like a sale.** ⚠ **A rule that lives in a control's markup leaves with the control.** Digits-only is enforced in `Quantity_TextChanged`; an unparseable string would otherwise leave the binding showing one number and the viewmodel holding another |
| `SfPicker` | `TillView` — the alterations picker | `DisplayActionSheet` | The empty-list guard is KEPT even though an action sheet cannot repeat the `SelectedIndex == 0` crash — an empty sheet is still a dead end. ⚠ `IsBusy` is released before dispatching, or the discount silently does nothing |
| `SfListView` + `SfPopup` context menu | `ViewAllView` — the item list | `CollectionView` (`IsGrouped`) | Grouping moved into the VIEWMODEL (`ItemGroups`), which deletes the crash-on-open class of bug — there is no longer a shared mutable `DataSource` between view and viewmodel to get the assignment order wrong on. ⚠ The column header is now a fixed Grid ABOVE the list, so it no longer scrolls away. ⚠ `SelectedItem` is cleared on every tap: a `CollectionView` will not raise SelectionChanged twice for the same row, so without it the second tap does nothing and reads as a freeze. The right-click menu is gone — replaced by the **visible per-row Edit button** |
| `SfListView` (multi-select) | `InputMultiSelectAlert` — which basket lines a discount applies to | `CollectionView`, `SelectionMode.Multiple` | ⚠ `Header`/`Footer` take VIEWS, not DataTemplates — handing them a template renders nothing, **silently**. ⚠ `SelectedItems` must be **assigned, not mutated**: the notification comes from the setter, so adding in place updates the count and leaves the rows unhighlighted. The footer scrolls rather than sticking; on a handful of basket lines, nobody will see it |
| `SfCartesianChart`, `SfCalendar` | `SalesReportsView`, `StockOuttakeView` | **Already replaced** — both screens read the pre-cutover legacy database and are hidden; the till's figures come from `GET /api/v1/reports/summary` and the portal has the full suite | — |

⚠ **The basket list was never Syncfusion** — `TillView` uses a plain MAUI `ListView`. The most
critical screen in the app was already free of it.

### 4.6 What to watch when any of this is touched again

These screens have **no automated coverage** — that needs a running UI host and this repo has none, so
the swaps above were held by review alone. Ranked by likelihood × cost:

1. **The quantity box** — it is on the money path. Type `3`, scan, check three went in; then type `0`
   and check it becomes 1 rather than ringing a free line.
2. **The item list** — grouping, the A–Z headers, search narrowing, and tapping the SAME row twice.
3. **The discount multi-select** — Select all / Unselect all, and that the chosen lines are the ones
   that get the discount.
4. **The alterations sheet** — that picking one actually opens the amount prompt.

⚠ These are folded into [`Build/Test Maui.md`](../Test%20Maui.md); that document is what to hand a
tester. This list is here so the *reason* each one is risky stays next to the change that made it so.

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

## 6. ⚠⚠ THE ROOT FOLDER — the real answer to Matt's question, and it is not a size problem

**Matt, 2026-08-20:** *"I don't understand why there are 299 files in the root folder."*

**Because we hand you the unpackaged DEVELOPER layout, and that was forced rather than chosen.**
`Configuration != Debug` sets `WindowsPackageType=MSIX`, but nothing configures a signing certificate,
so the Release MSIX is **unsigned and Windows refuses to install it** — *"the package or bundle is not
digitally signed"*. The runbook's workaround is `-p:WindowsPackageType=None`, which is the flat folder.

Of the 179 root DLLs, **6 are ours.** The other 173 are WinUI, MAUI, Skia and EF Core, and .NET
requires them beside the app host. The 51 PNGs are all `appiconLargeTile.scale-*` / `appiconLogo.altform-*`
— **MSIX tile assets an unpackaged build can never use.**

⚠⚠ **SO THE FIX IS TO SIGN THE MSIX, NOT TO TIDY THE FOLDER.** Packaged, all 268 files and 88 folders
live inside the package, the operator installs an application instead of being handed a directory, and
the tile assets start doing their job. **Everything else in this document is tidying a layout that
should never have been user-facing.** ≈half a day: generate a certificate, wire up signing, fix
`Install.ps1` (which today trusts a certificate that was never generated).

### What is NOT worth doing, so nobody spends a day on it

| Idea | Why not |
|---|---|
| **Move the 179 root DLLs into subfolders** | .NET's loader requires assemblies beside the app host. Doing it means `additionalProbingPaths` in `runtimeconfig.json` — a classic "works on the dev box, fails on the shop PC". **The flat root is the loader's requirement, not mess.** |
| ⚠ **Move the language folders into a `languages/` folder** | **Asked for on 2026-08-20; it cannot be done.** Two mechanisms, neither redirectable. The 86 native `.mui` folders appear **nowhere in `deps.json`** — the Win32 MUI loader probes `<the DLL's own directory>\<culture>\<name>.dll.mui`, so there is no manifest to edit. The satellite folders **were** in `deps.json`, but those paths are the NuGet package layout (`lib/net10.0-windows.../ar/…`); the host maps them to `<appbase>\<culture>\` by convention and a prefix cannot be expressed. ⚠⚠ **And it would fail SILENTLY** — both mechanisms fall back to the neutral resources embedded in the main DLLs, so an English machine looks perfect and only a non-English Windows shows the loss. The satellites are gone instead (`SatelliteResourceLanguages`), which is supported and verifiable. |
| **Delete the `.mui` language folders** | 3.8 MB, and they are Windows App SDK package content. Fighting the SDK's asset list for 1.4% of the build. ⚠ A post-publish target *could* delete them, but it changes behaviour on non-English Windows for no measurable gain. |
| **Strip the `.pdb` files** | 532 KB — nothing. And they are what gives `CrashLog.Write` a stack trace with **file and line numbers**, which is how a hand-run finding becomes a fix. **Keep them.** |
| **`PublishTrimmed`** | Not supported for WinUI/MAUI Windows apps, and trimming reflection-driven XAML binding is how a screen renders blank in production and nowhere else. |
| **Chase the remaining ~95 MB of "everything else"** | It is the framework: 179 DLLs, each small. There is no single win in it. |
