# Publishes the Plutus Till Agent with the version stamped into the artefact name, plus the
# latest.json manifest the till's Settings -> Hardware reads to render the download button and
# the "update available" hint.
#
#   .\publish-agent.ps1              -> publish-out\PlutusTillAgent-<version>.exe + latest.json
#
# Release procedure: bump <Version> in Plutus.TillAgent.csproj, run this, then copy BOTH output
# files to the build Mac at ~/PLUTUS/Plutus.Frontend.WebApp/public/agent/ (delete the previous
# versioned exe there), rebuild the till and deploy dist. The exe is NOT in git (60MB+).
param([string]$OutDir = "$PSScriptRoot\publish-out")

$ErrorActionPreference = "Stop"
$csproj = Join-Path $PSScriptRoot "Plutus.TillAgent.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csproj" }

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
