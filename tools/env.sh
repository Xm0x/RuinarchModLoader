#!/usr/bin/env bash
# Central path config for RuinarchModLoader.
# Source from other scripts: source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

# Your Ruinarch install (source of the game's Managed/ DLLs we reference at
# build time; never committed, never redistributed).
export RUIN_GAME_DIR="${RUIN_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Ruinarch}"
export RUIN_MANAGED_DIR="$RUIN_GAME_DIR/Ruinarch_Data/Managed"

export PATH="$PATH:$HOME/.dotnet/tools"

export MOD_PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export MOD_LIB_DIR="$MOD_PROJECT_DIR/lib"
export MOD_BUILD_DIR="$MOD_PROJECT_DIR/build"

# Version of Harmony to fetch for bundling.
export HARMONY_VERSION="${HARMONY_VERSION:-2.2.2}"
