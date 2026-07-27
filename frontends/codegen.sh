#!/usr/bin/env bash
# Generate TypeScript API types from the backend OpenAPI spec (T0.3).
# Dev-time only — the output has no runtime dependency (consistent with the minimal-
# dependency discipline). Wired into the web POS in Phase 2 (T2.1); until then the
# generated file just has to compile.
#
# Refresh the spec from a running backend first:
#   curl -s http://<api-host>/swagger/v1/swagger.json -o openapi.json   # (repo root)
# Then, from anywhere:
#   frontends/codegen.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SPEC="$ROOT/openapi.json"
OUT="$ROOT/Plutus/Frontend/Plutus.Frontend.WebApp/src/api/types.gen.ts"   # webapp moves under frontends/ later
mkdir -p "$(dirname "$OUT")"
npx --yes openapi-typescript@7 "$SPEC" -o "$OUT"
echo "generated $OUT"
