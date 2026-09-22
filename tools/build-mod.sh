#!/usr/bin/env bash
# Compile a mod against the game's assemblies + the modding API + Harmony.
#
# Usage:
#   tools/build-mod.sh <mod-src-dir> [target-Mods-dir]
#
# With no target, the mod is staged under build/mods/<name>/. With a target
# (e.g. a game copy's Mods/ folder) it's installed there, ready to run.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

src="${1:?usage: build-mod.sh <mod-src-dir> [target-Mods-dir]}"
src="$(cd "$src" && pwd)"
name="$(basename "$src")"
target="${2:-$MOD_BUILD_DIR/mods}"
outdir="$target/$name"

[ -f "$MOD_BUILD_DIR/Ruinarch.Modding.dll" ] || { echo "build the loader first: tools/build.sh" >&2; exit 1; }
[ -f "$MOD_LIB_DIR/0Harmony.dll" ] || "$MOD_PROJECT_DIR/tools/get-harmony.sh"

CSC="$(ls -1 /usr/lib64/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || CSC="$(ls -1 "$(dirname "$(command -v dotnet)")"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$CSC" ] || { echo "Roslyn csc.dll not found under the dotnet SDK" >&2; exit 1; }

mkdir -p "$outdir"
rsp="$(mktemp)"
{
  echo "-target:library"
  echo "-nostdlib"
  echo "-noconfig"
  echo "-langversion:latest"
  echo "-nowarn:0169,0649,0067,0414,0108,0114,0436"
  echo "-out:$outdir/$name.dll"
  # Reference every game assembly, plus the modding API and Harmony.
  for dll in "$RUIN_MANAGED_DIR"/*.dll; do echo "-r:$dll"; done
  echo "-r:$MOD_BUILD_DIR/Ruinarch.Modding.dll"
  echo "-r:$MOD_BUILD_DIR/Ruinarch.ModContent.dll"
  echo "-r:$MOD_LIB_DIR/0Harmony.dll"
  find "$src" -name '*.cs' -print
} > "$rsp"
dotnet "$CSC" "@$rsp" || { echo "mod build FAILED" >&2; rm -f "$rsp"; exit 1; }
rm -f "$rsp"

[ -f "$src/mod.json" ] && cp "$src/mod.json" "$outdir/"
# Ensure Harmony is present in the Mods root so the loader resolves it.
cp "$MOD_LIB_DIR/0Harmony.dll" "$target/0Harmony.dll"
# Ensure the content framework is present in the Mods root so mods that add new
# content resolve it (and it self-installs its patches on first use).
cp "$MOD_BUILD_DIR/Ruinarch.ModContent.dll" "$target/Ruinarch.ModContent.dll"

echo "OK -> $outdir/$name.dll"
echo "     $target/0Harmony.dll"
