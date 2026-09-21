#!/usr/bin/env bash
# Build everything and assemble a distributable release zip under dist/.
# The zip contains ONLY our code + MIT Harmony: the patcher, the loader assembly,
# 0Harmony, install notes, and the example mod source. No game files.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

VERSION="${1:-0.1.0}"
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
cp "$MOD_BUILD_DIR"/patcher/0Harmony.dll "$dist/"

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