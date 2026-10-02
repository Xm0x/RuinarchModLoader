#!/usr/bin/env bash
# Build everything and assemble a distributable release zip under dist/.
# The zip contains ONLY our code + MIT Harmony: the patcher, the loader assembly,
# the content framework, 0Harmony, install notes, and the example mod source.
# No game files.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

VERSION="${1:-$(grep -oP 'public static string Version => "\K[0-9.]+' "$MOD_PROJECT_DIR/src/Ruinarch.Modding/ModLoader.cs")}"
grep -q "public static string Version => \"$VERSION\"" "$MOD_PROJECT_DIR/src/Ruinarch.Modding/ModLoader.cs" \
  || { echo "version $VERSION differs from ModLoader.Version; update src/Ruinarch.Modding/ModLoader.cs first" >&2; exit 1; }
KEY="${RUIN_UPDATE_KEY:-$HOME/.config/ruinarch-modloader/update-signing-key.xml}"
[ -f "$KEY" ] || { echo "update signing key not found: $KEY (set RUIN_UPDATE_KEY)" >&2; exit 1; }
name="RuinarchModLoader-$VERSION"
dist="$MOD_PROJECT_DIR/dist/$name"

"$MOD_PROJECT_DIR/tools/build.sh"

rm -rf "$dist"
mkdir -p "$dist/examples/ExampleMod"

# Patcher + runtime deps + bundled loader/Harmony (framework-dependent; needs the
# .NET 8 runtime on the target machine).
cp "$MOD_BUILD_DIR"/patcher/RuinarchModLoader.Patcher.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/RuinarchModLoader.Patcher.runtimeconfig.json "$dist/"
cp "$MOD_BUILD_DIR"/patcher/Mono.Cecil*.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/Ruinarch.Modding.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/Ruinarch.Boot.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/0Harmony.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/Ruinarch.ModContent.dll "$dist/"
cp "$MOD_BUILD_DIR"/patcher/Ruinarch.ModMenu.dll "$dist/"

# Docs + example source
cp "$MOD_PROJECT_DIR/README.md" "$dist/"
cp "$MOD_PROJECT_DIR/LICENSE" "$dist/"
cp -r "$MOD_PROJECT_DIR/docs" "$dist/"
cp "$MOD_PROJECT_DIR"/examples/ExampleMod/*.cs "$dist/examples/ExampleMod/"
cp "$MOD_PROJECT_DIR"/examples/ExampleMod/mod.json "$dist/examples/ExampleMod/"

cat > "$dist/INSTALL.txt" <<'EOF'
RuinarchModLoader - install

Requires the .NET 8 runtime (https://dotnet.microsoft.com/download).

1. Keep these files together.
2. Patch your Ruinarch install (the folder with Ruinarch.exe):

     dotnet RuinarchModLoader.Patcher.dll --game "/path/to/Ruinarch"

3. Put mods (.dll, optionally in subfolders) into the new Mods/ folder next to
   Ruinarch.exe.
4. Launch Ruinarch through Steam. Check Mods/mods.log to see what loaded.

Uninstall:
     dotnet RuinarchModLoader.Patcher.dll --game "/path/to/Ruinarch" --uninstall

After a game update, run the patcher again (Steam replaces the patched file).

This tool ships no game code or assets. It edits a copy of Ruinarch you own.
EOF

( cd "$MOD_PROJECT_DIR/dist" && zip -qr "$name.zip" "$name" )
echo "OK -> dist/$name.zip"
du -h "$MOD_PROJECT_DIR/dist/$name.zip" | cut -f1

# --- In-game update assets: the files the menu downloads, a manifest, and its signature.
# Upload every file in dist/update-$VERSION/ to the GitHub release as an asset.
update="$MOD_PROJECT_DIR/dist/update-$VERSION"
rm -rf "$update"; mkdir -p "$update"
files=("$MOD_BUILD_DIR/Ruinarch.Modding.dll" "$MOD_LIB_DIR/0Harmony.dll" "$MOD_LIB_DIR/Mono.Cecil.dll" "$MOD_BUILD_DIR/Ruinarch.ModContent.dll" "$MOD_BUILD_DIR/Ruinarch.ModMenu.dll")
cp "${files[@]}" "$update/"
{
  printf '{\n  "formatVersion": 1,\n  "version": "%s",\n  "boot": 1,\n' "$VERSION"
  printf '  "page": "https://github.com/Xm0x/RuinarchModLoader/releases/tag/v%s",\n  "files": [\n' "$VERSION"
  sep=""
  for f in "${files[@]}"; do
    n="$(basename "$f")"
    printf '%s    { "name": "%s", "sha256": "%s", "size": %s }' "$sep" "$n" "$(sha256sum "$update/$n" | cut -d' ' -f1)" "$(stat -c %s "$update/$n")"
    sep=$',\n'
  done
  printf '\n  ]\n}\n'
} > "$update/update.json"
dotnet build "$MOD_PROJECT_DIR/src/UpdateSigner/UpdateSigner.csproj" -c Release -o "$MOD_BUILD_DIR/signer" -v quiet >/dev/null
dotnet "$MOD_BUILD_DIR/signer/RuinarchModLoader.UpdateSigner.dll" sign "$KEY" "$update/update.json"
echo "OK -> dist/update-$VERSION/ (upload these files to the release)"

# --- Graphical installer (self-contained: no .NET runtime needed on target) ---
echo ">>> building graphical installers"
"$MOD_PROJECT_DIR/tools/build-gui.sh" >/dev/null
for rid in linux-x64 win-x64; do
	gdir="$MOD_BUILD_DIR/gui/$rid"
	[ -d "$gdir" ] || { echo "missing $gdir" >&2; exit 1; }
	gname="RuinarchModLoader-Installer-$VERSION-$rid"
	stage="$MOD_PROJECT_DIR/dist/$gname"
	rm -rf "$stage"; mkdir -p "$stage"
	cp "$gdir"/* "$stage"/
	cp "$MOD_PROJECT_DIR/LICENSE" "$stage"/
	if [ "$rid" = "win-x64" ]; then run="Double-click RuinarchModLoader.Installer.exe"; else run="Run ./RuinarchModLoader.Installer"; fi
	cat > "$stage/INSTALL.txt" <<EOF
RuinarchModLoader - graphical installer

No .NET runtime needed; everything is bundled.

1. Keep every file in this folder together.
2. $run
3. It finds your Ruinarch install automatically (or click Browse).
4. Click Install. Drop mods into the Mods folder it creates, then launch
   Ruinarch through Steam. Uninstall reverts the game cleanly.

This tool ships no game code or assets. It edits a copy of Ruinarch you own.
EOF
	( cd "$MOD_PROJECT_DIR/dist" && zip -qr "$gname.zip" "$gname" )
	echo "OK -> dist/$gname.zip ($(du -h "$MOD_PROJECT_DIR/dist/$gname.zip" | cut -f1))"
done