# Plutus Till Agent (FE3)

A small tray app for a **Windows till PC**. The browser till cannot reach a receipt printer or a
cash drawer; this agent bridges `localhost` HTTP → ESC/POS hardware.

- Listens on **`http://127.0.0.1:9123`, loopback only** — nothing on the network can reach it.
- Every hardware action needs the **`X-Agent-Token`** pairing token. Loopback alone is not enough:
  any web page the cashier opens can also fetch localhost, and without the token that page could
  kick the drawer or waste a roll of paper.
- `/status` is unauthenticated on purpose: the till must be able to ask "is there an agent here?"
  before it is paired, and that is what tells the portal a till PC has an agent (FE3.0).

## Build

```bash
dotnet publish tools/Plutus.TillAgent/Plutus.TillAgent.csproj -c Release
# → tools/Plutus.TillAgent/bin/Release/net10.0-windows/win-x64/publish/PlutusTillAgent.exe
```

Self-contained single file — **no .NET install needed on the till PC**.

## Install on a till PC

1. ⚠⚠ **Copy `PlutusTillAgent.exe` to a permanent place and run it from THERE.**
   `%LOCALAPPDATA%\Plutus\Agent\PlutusTillAgent.exe` is the recommended path — it needs no admin,
   which matches the per-user auto-start below.
   **Do NOT run it from `Downloads`.** Auto-start records the path the agent was running from, and a
   Downloads copy gets cleaned up, renamed `… (1).exe` by a second download, or replaced — after
   which the till boots and starts nothing. That is a real fault, found on a till on 2026-08-17:
   the registration read `"…\Downloads\PlutusTillAgent (1).exe"` for a file that no longer existed.
   ⚠ Since **1.4.0** the agent repairs its own registration on every launch, so moving the exe and
   running it once is enough to fix a stale one — but starting in the right place avoids the question.
2. Run it. It appears in the notification area and opens Settings on first run.
3. Choose the **receipt printer** (the Windows printer the receipt printer is installed as) and the
   **paper width** (80mm = 42 columns, 58mm = 32).
4. Tick **Start automatically when somebody logs in to this PC**.
   ⚠⚠ **At LOGON, not at boot.** It is an `HKCU\…\Run` value, so it fires when a user signs in — a
   till PC that boots to a login screen starts no agent until somebody does. Where the till PC
   **auto-logs-in** this is invisible; where it does not, check that first before assuming the
   setting is broken. It costs nothing in practice, because the agent's only client is the till UI
   running in that same session. ⚠ It also means this can never start the agent *before* anyone signs
   in — that needs a Windows service, and a tray app cannot be one (session 0 has no desktop, so the
   icon and Settings window would be gone).
5. **Test print.** If the £ signs and the barcode look right, the printer is good.
6. **Copy token**, then in the till browser: **Settings → Hardware → Pairing token → Save token**.
7. Back in the till's Hardware card, use **Test print** and **Open drawer** to confirm the browser
   can drive the hardware.

That's it. The portal's **Locations** page will show `agent v1.0.0 · printer ✓` against that till.

### No installer yet — deliberately

v1 is a copy-and-run executable that registers its own auto-start (per user, no admin rights). An
MSI/WiX package is only worth authoring once the install is being repeated across many machines or
pushed by Group Policy; for a handful of tills it is ceremony without benefit. The self-contained
exe is the same artefact an MSI would carry, so packaging later changes nothing about the agent.

## Uninstall

Exit from the tray menu, untick auto-start first (or delete the `PlutusTillAgent` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), then delete the exe and
`%LOCALAPPDATA%\PlutusTillAgent`.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/status` | none | `{ agentVersion, printer: { name, online }, drawerSupported, paired, columns }` |
| POST | `/print` | token | Print a document (the op list the till builds — see `receiptDoc.ts`) |
| POST | `/print/test` | token | The built-in test receipt |
| POST | `/drawer/open` | token | Cash-drawer kick |

`503` means "the hardware isn't available" — the till treats that as a cue to fall back to the
browser/PDF receipt, never as an error worth blocking a sale for.

## How printing works, and the one open question

The agent writes **raw ESC/POS bytes to the Windows print queue** (spooler `RAW` datatype), which
bypasses the driver's rendering. This works with the ordinary vendor driver a shop printer is
normally installed with, needs no extra software, and can be tested against any queue.

⚠ **The MAUI/Xamarin tills instead use WinRT `Windows.Devices.PointOfService` (OPOS)**, which needs
a vendor UPOS service object and the printer registered as a POS device. If Kapow's printer only
exposes itself that way, the RAW path will fail and the OPOS path must be added — the transport sits
behind `IReceiptTransport` for exactly that reason, and
`Plutus/Frontend/Plutus.Frontend.ClientUI/Platforms/Windows/Services/PosHandeling/PosPrinter.cs`
is the port source. **Deciding this is the FE3.1 on-site spike** and is the one thing that cannot be
settled away from the hardware.

## What has and hasn't been verified

Verified on the dev box (2026-07-31):

- `/status` responds unauthenticated with the version and printer state.
- `/print` and `/drawer/open` **401** without the token.
- With the token and a deliberately absent printer, both **503** with the Windows error surfaced
  (`error 1801` = invalid printer name) — i.e. the P/Invoke path is genuinely reached and failures
  are reported rather than swallowed.
- ESC/POS byte generation is unit-tested (`tests/Plutus.Tests.Unit/EscPosTests.cs`): mode resets,
  CP437 `£`, Code 39, cut-feeds, drawer pulse.

**Not** verified — needs the FE3.1 visit: paper actually coming out of Kapow's printer model, the
drawer physically opening, and whether the HTTPS till page is allowed to fetch `http://127.0.0.1`
in the till's browser (Chromium and Firefox treat loopback as potentially trustworthy, so it is
expected to work, but it is a five-second check worth doing first).
