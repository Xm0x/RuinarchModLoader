#!/usr/bin/env bash
# Build the content-injection framework (Ruinarch.ModContent.dll).
#
# This assembly lets mods register genuinely NEW content (new buildable structures,
# etc.) against the STOCK game via Harmony - it references Assembly-CSharp + 0Harmony,
# unlike the minimal loader (Ruinarch.Modding.dll). It ships in Mods/ like 0Harmony and
# self-installs its patches the first time a mod calls the ModContent API.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

[ -d "$RUIN_MANAGED_DIR" ] || { echo "Managed/ not found: $RUIN_MANAGED_DIR (set RUIN_GAME_DIR)" >&2; exit 1; }
[ -f "$MOD_LIB_DIR/0Harmony.dll" ] || "$MOD_PROJECT_DIR/tools/get-harmony.sh"

CSC="$(ls -1 /usr/lib64/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || CSC="$(ls -1 "$(dirname "$(command -v dotnet)")"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || { echo "Roslyn csc.dll not found under the dotnet SDK" >&2; exit 1; }

mkdir -p "$MOD_BUILD_DIR"
rsp="$(mktemp)"
{
  echo "-target:library"
  echo "-nostdlib"
  echo "-noconfig"
  echo "-langversion:latest"
  echo "-nowarn:0169,0649,0067,0414,0108,0114,0436,2023"
  echo "-out:$MOD_BUILD_DIR/Ruinarch.ModContent.dll"
  # Reference every game assembly (resolves all Unity/base deps) + Harmony.
  for dll in "$RUIN_MANAGED_DIR"/*.dll; do echo "-r:$dll"; done
  echo "-r:$MOD_LIB_DIR/0Harmony.dll"
  find "$MOD_PROJECT_DIR/src/Ruinarch.ModContent" -name '*.cs' -print
} > "$rsp"
dotnet "$CSC" "@$rsp" || { echo "framework build FAILED" >&2; rm -f "$rsp"; exit 1; }
rm -f "$rsp"
echo "OK -> build/Ruinarch.ModContent.dll"
