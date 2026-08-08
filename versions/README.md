# Component versions

**One file per deployable thing. Bump the one you shipped, and only that one.**

Matt, 2026-08-08:

> *"I need a backend version (the portal). And each till needs a specific version as they will end
> up diverging when you have specific Windows or Linux challenges for example."*

That is the whole design. A single platform-wide number was the first attempt and it was wrong: a
Windows-only printer fix in the MAUI till would have forced the web till to claim a new version it
had no changes in, and a shop reporting a bug against "1.4.2" would leave you asking *which* 1.4.2.
Components ship on their own schedules, so they version on their own schedules.

| File | Component | Deployed how |
|---|---|---|
| `backend.txt` | The API — `Plutus.DBService` | pm2 on the Mac |
| `portal.txt` | Operator + client portal — `Plutus.Frontend.Portal` | static assets via Caddy |
| `till-web.txt` | **Till:** browser — `Plutus.Frontend.WebApp` | static assets via Caddy |
| `till-maui.txt` | **Till:** Windows/Android/iOS — `Plutus.Frontend.AppClient` | installed on till hardware |
| `agent.txt` | Hardware helper — `tools/Plutus.TillAgent` | installed alongside the browser till |
| `platform.txt` | The shared libraries (`SharedKernel`, `Contracts.Client`, `Client.Core`, `Client.Storage`) | ships *inside* the others |

## X.Y.Z — what to bump

**`MAJOR.FEATURE.FIX`**, per Matt's instruction.

| Position | Bump when | Example |
|---|---|---|
| **X** — major | A breaking change, or a release you want to talk about as a version in its own right | wire contract changes shape; a till's local schema needs a cutover |
| **Y** — feature | New capability, backwards compatible. **Resets Z to 0.** | the Cash tab lands on MAUI; gift cards reach a till |
| **Z** — fix | A bug fix or a tweak with no new capability | the Windows drawer-kick timing fix; a label wording change |

Bump **in the same commit as the change**, so `git log versions/till-maui.txt` reads as that till's
release history and a bug report naming a version can be pointed at a commit.

## Adding a till

A new surface gets its own file — `versions/till-linux.txt`, `versions/till-macos.txt` — and points
at it from its project. Nothing else to wire. See `Build/till-design.md` D2.

## How a component reads its own number

- **.NET** — the project sets `<PlutusVersionFile>versions/backend.txt</PlutusVersionFile>`;
  `Directory.Build.targets` turns it into `InformationalVersion`; `PlutusVersion.Current` reads it
  back at runtime.
- **TypeScript** — `vite.config.ts` reads its file into `__APP_VERSION__` at build time.

⚠ **No component may hardcode its own version string.** One nobody remembers to bump names the
wrong build in every report filed against it, with total confidence. `ComponentVersionTests` fails
the build if a version file goes missing, stops being X.Y.Z, or if a surface grows a literal.
