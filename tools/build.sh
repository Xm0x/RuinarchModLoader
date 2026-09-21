#!/usr/bin/env bash
# Build the loader assembly (Ruinarch.Modding.dll) and the Cecil patcher, and
# stage them together in build/ ready to run or package.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

[ -d "$RUIN_MANAGED_DIR" ] || { echo "Managed/ not found: $RUIN_MANAGED_DIR (set RUIN_GAME_DIR)" >&2; exit 1; }
"$MOD_PROJECT_DIR/tools/get-harmony.sh"

mkdir -p "$MOD_BUILD_DIR"

# --- Loader assembly: compile against the game's own Unity/base DLLs ---
CSC="$(ls -1 /usr/lib64/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || CSC="$(ls -1 "$(dirname "$(command -v dotnet)")"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || { echo "Roslyn csc.dll not found under the dotnet SDK" >&2; exit 1; }

refs=(mscorlib System System.Core netstandard UnityEngine.CoreModule UnityEngine.JSONSerializeModule)
rsp="$(mktemp)"
{
  echo "-target:library"
  echo "-nostdlib"
  echo "-noconfig"
  echo "-langversion:latest"
  echo "-nowarn:0169,0649,0067,0414"
  echo "-out:$MOD_BUILD_DIR/Ruinarch.Modding.dll"
  for r in "${refs[@]}"; do
    dll="$RUIN_MANAGED_DIR/$r.dll"
    [ -f "$dll" ] && echo "-r:$dll" || { echo "missing reference $dll" >&2; exit 1; }
  done
  find "$MOD_PROJECT_DIR/src/Ruinarch.Modding" -name '*.cs' -print
} > "$rsp"
dotnet "$CSC" "@$rsp" || { echo "loader build FAILED" >&2; rm -f "$rsp"; exit 1; }
rm -f "$rsp"
echo "OK -> build/Ruinarch.Modding.dll"

# --- Patcher: normal SDK build ---
dotnet build "$MOD_PROJECT_DIR/src/Patcher/Patcher.csproj" -c Release -o "$MOD_BUILD_DIR/patcher" -v quiet
echo "OK -> build/patcher/RuinarchModLoader.Patcher.dll"

# --- Stage loader + Harmony next to the patcher so install can find them ---
cp "$MOD_BUILD_DIR/Ruinarch.Modding.dll" "$MOD_BUILD_DIR/patcher/"
cp "$MOD_LIB_DIR/0Harmony.dll"           "$MOD_BUILD_DIR/patcher/"
echo "Staged: build/patcher/ (patcher + Ruinarch.Modding.dll + 0Harmony.dll)"
