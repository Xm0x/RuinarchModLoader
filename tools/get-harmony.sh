#!/usr/bin/env bash
# Fetch Lib.Harmony (0Harmony.dll) from NuGet into lib/.
# MIT-licensed runtime-patching library used by mods. Small, but kept out of git
# so the repo carries no binaries; this script makes it reproducible.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

mkdir -p "$MOD_LIB_DIR"
dest="$MOD_LIB_DIR/0Harmony.dll"
if [ -f "$dest" ]; then
	echo "0Harmony.dll already present in lib/ (delete to refetch)."
	exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
url="https://www.nuget.org/api/v2/package/Lib.Harmony/${HARMONY_VERSION}"
echo "Fetching Lib.Harmony ${HARMONY_VERSION} ..."
curl -fsSL "$url" -o "$tmp/harmony.nupkg"

# nupkg is a zip; the net472 build is Mono/Unity-compatible.
unzip -o -q "$tmp/harmony.nupkg" -d "$tmp/harmony"
src="$(find "$tmp/harmony" -ipath '*net472*/0Harmony.dll' | head -1)"
[ -n "$src" ] || src="$(find "$tmp/harmony" -iname '0Harmony.dll' | sort | head -1)"
[ -n "$src" ] || { echo "0Harmony.dll not found in package" >&2; exit 1; }
cp "$src" "$dest"
echo "OK -> lib/0Harmony.dll ($(du -h "$dest" | cut -f1))"
