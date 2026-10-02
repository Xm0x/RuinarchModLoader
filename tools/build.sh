#!/usr/bin/env bash
# Build the loader assembly (Ruinarch.Modding.dll) and the Cecil patcher, and
# stage them together in build/ ready to run or package.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

[ -d "$RUIN_MANAGED_DIR" ] || { echo "Managed/ not found: $RUIN_MANAGED_DIR (set RUIN_GAME_DIR)" >&2; exit 1; }
"$MOD_PROJECT_DIR/tools/get-harmony.sh"

mkdir -p "$MOD_BUILD_DIR"

# --- Mono.Cecil: the loader reads package DLL metadata before loading anything.
# Same package/version as the patcher (src/Patcher/Patcher.csproj); the netstandard2.0
# build runs on the game's Mono and ships in Mods/ like 0Harmony.
CECIL_VERSION=0.11.5
if [ ! -f "$MOD_LIB_DIR/Mono.Cecil.dll" ]; then
  dotnet restore "$MOD_PROJECT_DIR/src/Patcher/Patcher.csproj" -v quiet >/dev/null
  mkdir -p "$MOD_LIB_DIR"
  cp "${NUGET_PACKAGES:-$HOME/.nuget/packages}/mono.cecil/$CECIL_VERSION/lib/netstandard2.0/Mono.Cecil.dll" "$MOD_LIB_DIR/"
fi

# --- Loader assembly: compile against the game's own Unity/base DLLs ---
CSC="$(ls -1 /usr/lib64/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || CSC="$(ls -1 "$(dirname "$(command -v dotnet)")"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || { echo "Roslyn csc.dll not found under the dotnet SDK" >&2; exit 1; }

refs=(mscorlib System System.Core netstandard UnityEngine.CoreModule UnityEngine.JSONSerializeModule Newtonsoft.Json)
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
  echo "-r:$MOD_LIB_DIR/Mono.Cecil.dll"
  find "$MOD_PROJECT_DIR/src/Ruinarch.Modding" -name '*.cs' -print
} > "$rsp"
dotnet "$CSC" "@$rsp" || { echo "loader build FAILED" >&2; rm -f "$rsp"; exit 1; }
rm -f "$rsp"
echo "OK -> build/Ruinarch.Modding.dll"

# --- Content framework: references the game DLL + Harmony (unlike the loader) ---
"$MOD_PROJECT_DIR/tools/build-framework.sh"

# --- Built-in mod menu: references the game DLL + Harmony + the loader API ---
"$MOD_PROJECT_DIR/tools/build-modmenu.sh"

# --- Verify every Harmony patch target resolves against the real game DLLs ---
"$MOD_PROJECT_DIR/tools/check-patches.sh" "$MOD_BUILD_DIR/Ruinarch.ModContent.dll" "$MOD_BUILD_DIR/Ruinarch.ModMenu.dll"

# --- Patcher: normal SDK build ---
dotnet build "$MOD_PROJECT_DIR/src/Patcher/Patcher.csproj" -c Release -o "$MOD_BUILD_DIR/patcher" -v quiet
echo "OK -> build/patcher/RuinarchModLoader.Patcher.dll"

# --- Stage loader + Harmony next to the patcher so install can find them ---
cp "$MOD_BUILD_DIR/Ruinarch.Modding.dll" "$MOD_BUILD_DIR/patcher/"
cp "$MOD_LIB_DIR/0Harmony.dll"           "$MOD_BUILD_DIR/patcher/"
cp "$MOD_LIB_DIR/Mono.Cecil.dll"         "$MOD_BUILD_DIR/patcher/"
cp "$MOD_BUILD_DIR/Ruinarch.ModContent.dll" "$MOD_BUILD_DIR/patcher/"
cp "$MOD_BUILD_DIR/Ruinarch.ModMenu.dll"    "$MOD_BUILD_DIR/patcher/"
echo "Staged: build/patcher/ (patcher + Ruinarch.Modding.dll + 0Harmony.dll + Mono.Cecil.dll + Ruinarch.ModContent.dll + Ruinarch.ModMenu.dll)"
