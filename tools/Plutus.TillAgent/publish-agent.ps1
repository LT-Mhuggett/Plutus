# Publishes the Plutus Till Agent with the version stamped into the artefact name, plus the
# latest.json manifest the till's Settings -> Hardware reads to render the download button and
# the "update available" hint.
#
#   .\publish-agent.ps1              -> publish-out\PlutusTillAgent-<version>.exe + latest.json
#
# Release procedure: bump versions/agent.txt, run this, then copy BOTH output files to the build Mac
# at ~/PLUTUS/Plutus.Frontend.WebApp/public/agent/ (delete the previous versioned exe there), rebuild
# the till and deploy dist. The exe is NOT in git (60MB+).
param([string]$OutDir = "$PSScriptRoot\publish-out")

$ErrorActionPreference = "Stop"
$csproj = Join-Path $PSScriptRoot "Plutus.TillAgent.csproj"

# ⚠⚠ THE VERSION COMES FROM versions/agent.txt, NOT FROM PARSING THE CSPROJ. This script used to
# read <Version> as literal XML text, which worked only while that element held a literal. It now
# holds an MSBuild expression that reads versions/agent.txt — so the old parse yielded the
# EXPRESSION as a string and built an exe called
# "PlutusTillAgent-$([System.IO.File]::ReadAllText('…/versions/agent.txt').Trim()).exe", which
# failed on the path separators inside it.
#
# ⚠ It had been broken since the version moved into versions/agent.txt, and silently: nobody
# noticed because the last successful run (2026-08-07, agent 1.3.3) predated the change, and its
# stale latest.json went on looking like a current release. Found 2026-08-17.
$versionFile = Join-Path $PSScriptRoot "..\..\versions\agent.txt"
if (-not (Test-Path $versionFile)) { throw "versions/agent.txt not found at $versionFile" }
$version = (Get-Content $versionFile -Raw).Trim()
if (-not $version) { throw "versions/agent.txt is empty" }

# ⚠ A version that is not three dot-separated numbers means the file has been edited by hand into
# something the artefact name and latest.json would carry onward silently.
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "versions/agent.txt is not MAJOR.FEATURE.FIX: '$version'" }

# Prefer the Program Files SDK install — a PATH hit can be a runtime-only dotnet that
# fails publish with "SDK not found" (seen on this repo's dev box).
$dotnet = if (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe") { "$env:ProgramFiles\dotnet\dotnet.exe" }
          else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
if (-not $dotnet) { throw "dotnet SDK not found" }

& $dotnet publish $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$src = Get-ChildItem (Join-Path $PSScriptRoot "bin\Release") -Recurse -Filter PlutusTillAgent.exe |
    Where-Object { $_.FullName -like "*\publish\*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $src) { throw "published exe not found under bin\Release" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$exeName = "PlutusTillAgent-$version.exe"
Copy-Item $src.FullName (Join-Path $OutDir $exeName) -Force
Set-Content -Path (Join-Path $OutDir "latest.json") -Encoding utf8 -Value (
    ConvertTo-Json ([ordered]@{ version = "$version"; file = $exeName })
)
"published $exeName ($('{0:N1}' -f ($src.Length / 1MB)) MB) + latest.json -> $OutDir"
