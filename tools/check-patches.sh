#!/usr/bin/env bash
# Statically verify every [HarmonyPatch] in the given assemblies resolves against the
# real game DLLs (target method exists, overload is unambiguous, named patch parameters
# exist on the target). A bad target otherwise only shows up at launch, where it aborts
# the whole PatchAll. Exits non-zero on any failure.
#
# Usage: tools/check-patches.sh <assembly.dll> [<assembly.dll>...]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

[ "$#" -ge 1 ] || { echo "usage: check-patches.sh <assembly.dll>..." >&2; exit 2; }

tool="$MOD_BUILD_DIR/patchcheck/PatchCheck.dll"
proj="$MOD_PROJECT_DIR/tools/PatchCheck/PatchCheck.csproj"
# Rebuild when missing or older than its sources.
if [ ! -f "$tool" ] || [ -n "$(find "$MOD_PROJECT_DIR/tools/PatchCheck" -name '*.cs' -newer "$tool" -print -quit)" ]; then
  dotnet build "$proj" -c Release -o "$MOD_BUILD_DIR/patchcheck" -v quiet >/dev/null \
    || { echo "PatchCheck build FAILED" >&2; exit 1; }
fi

dotnet "$tool" "$RUIN_MANAGED_DIR;$MOD_BUILD_DIR;$MOD_LIB_DIR" "$@"
