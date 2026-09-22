#!/usr/bin/env bash
# Build the built-in mod menu (Ruinarch.ModMenu.dll).
#
# This assembly replaces the main-menu "Mods" window with a loader-native list of
# installed mods (enable/disable toggles, log view, open-folder). It references the
# game DLLs (for ModParentUI and TextMeshPro), Harmony, and the loader API
# (Ruinarch.Modding.dll), so it must be built after the loader. It ships in Mods/
# like 0Harmony and self-installs its Harmony patch on load.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

[ -d "$RUIN_MANAGED_DIR" ] || { echo "Managed/ not found: $RUIN_MANAGED_DIR (set RUIN_GAME_DIR)" >&2; exit 1; }
[ -f "$MOD_LIB_DIR/0Harmony.dll" ] || "$MOD_PROJECT_DIR/tools/get-harmony.sh"
[ -f "$MOD_BUILD_DIR/Ruinarch.Modding.dll" ] || { echo "Ruinarch.Modding.dll missing; run build.sh first" >&2; exit 1; }

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
  echo "-out:$MOD_BUILD_DIR/Ruinarch.ModMenu.dll"
  # Reference every game assembly (Assembly-CSharp, TextMeshPro, UnityEngine.UI, ...)
  # plus Harmony and the loader API.
  for dll in "$RUIN_MANAGED_DIR"/*.dll; do echo "-r:$dll"; done
  echo "-r:$MOD_LIB_DIR/0Harmony.dll"
  echo "-r:$MOD_BUILD_DIR/Ruinarch.Modding.dll"
  find "$MOD_PROJECT_DIR/src/Ruinarch.ModMenu" -name '*.cs' -print
} > "$rsp"
dotnet "$CSC" "@$rsp" || { echo "mod menu build FAILED" >&2; rm -f "$rsp"; exit 1; }
rm -f "$rsp"
echo "OK -> build/Ruinarch.ModMenu.dll"
