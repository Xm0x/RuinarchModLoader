#!/usr/bin/env bash
# Publish the graphical installer as a self-contained, single-file binary for
# Windows and Linux. Each output folder holds the installer plus the files it
# drops into the game (Ruinarch.Modding.dll, 0Harmony.dll, Ruinarch.ModContent.dll),
# ready to zip and ship.
#
# Usage: tools/build-gui.sh [out-dir]   (default: build/gui)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

OUT="${1:-$MOD_BUILD_DIR/gui}"
GUI="$MOD_PROJECT_DIR/src/Installer.GUI/Installer.GUI.csproj"

# The loader must exist to bundle it. Build it (and fetch Harmony) if needed.
[ -f "$MOD_LIB_DIR/0Harmony.dll" ] || "$MOD_PROJECT_DIR/tools/get-harmony.sh"
"$MOD_PROJECT_DIR/tools/build.sh" >/dev/null
[ -f "$MOD_BUILD_DIR/Ruinarch.Modding.dll" ] || { echo "loader build failed" >&2; exit 1; }

for rid in linux-x64 win-x64; do
	dest="$OUT/$rid"
	echo ">>> publishing $rid"
	rm -rf "$dest"
	dotnet publish "$GUI" -c Release -r "$rid" --self-contained true \
		-p:PublishSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-p:PublishTrimmed=false \
		-p:DebugType=none \
		-o "$dest" >/dev/null
	rm -f "$dest"/*.pdb "$dest"/*.runtimeconfig.json
	cp "$MOD_BUILD_DIR/Ruinarch.Modding.dll" "$MOD_LIB_DIR/0Harmony.dll" "$MOD_BUILD_DIR/Ruinarch.ModContent.dll" "$dest"/
	exe=$(find "$dest" -maxdepth 1 -name 'RuinarchModLoader.Installer*' ! -name '*.dll' | head -1)
	echo "    -> $exe ($(du -h "$exe" | cut -f1))"
done
echo "Done. Installer builds in $OUT/{linux-x64,win-x64}"
