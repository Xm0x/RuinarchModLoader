# RuinarchModLoader

A first-class mod loader for **Ruinarch**, with [Harmony](https://github.com/pardeike/Harmony)
runtime patching built in. Install it into your own copy of the game, drop mods
in a folder, and they load at startup. No BepInEx, no external injector.

> This is a fan-made modding tool that edits a copy of Ruinarch you already own.
> You need a legitimate copy of the game.

## How it works

Most Unity mod loaders sit beside the game as an external injector (a proxy DLL
or a doorstop). This one is different: a small **patcher** adds a single call to
the loader into your own `Assembly-CSharp.dll`, so the loader is part of the game
after install. That call runs the instant the game assembly loads, before any
scene, and scans `Mods/` for mod DLLs.

- The patcher backs up your original as `Assembly-CSharp.dll.orig`, so uninstall
  is a clean revert.
- Runs on the stock Steam build under Proton/Wine and Mono.

## Install (players)

### The installer app

A small graphical installer does everything for you. It bundles the .NET
runtime, so there is nothing else to install.

1. Download the installer for your OS from the latest
   [release](https://github.com/Xm0x/RuinarchModLoader/releases):
   - Windows: `RuinarchModLoader-Installer-<version>-win-x64.zip`
   - Linux: `RuinarchModLoader-Installer-<version>-linux-x64.zip`
2. Unzip it (keep the files together) and run it:
   - Windows: double-click `RuinarchModLoader.Installer.exe`
   - Linux: `./RuinarchModLoader.Installer`
3. It finds your Ruinarch install automatically (or use **Browse**). Click
   **Install**.
4. Drop mods into the `Mods/` folder it opens, then launch Ruinarch through
   Steam. **Uninstall** is a button in the same window (clean revert).

> After a game update Steam replaces `Assembly-CSharp.dll`; just open the
> installer again and click **Reinstall**.

## Writing a mod

A mod is a class that implements `IRuinarchMod`. `examples/ExampleMod/` is a
complete, working reference; copy it as a starting point.

```csharp
using HarmonyLib;
using Ruinarch.Modding;
using UnityEngine;

public class MyMod : IRuinarchMod
{
    public void OnLoad(ModContext context)
    {
        context.Logger.Info("MyMod loaded!");
        var harmony = new Harmony(context.Info.id);
        harmony.PatchAll(typeof(MyMod).Assembly);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Death))]
static class Character_Death_Patch
{
    static void Postfix(Character __instance)
    {
        Debug.Log($"[mymod] {__instance.name} died");
    }
}
```

Ship an optional `mod.json` next to your DLL:

```json
{ "id": "you.mymod", "name": "My Mod", "version": "1.0.0", "author": "you", "description": "..." }
```

The full API and patterns are in [`docs/WRITING_MODS.md`](docs/WRITING_MODS.md).
Adding **new content** (new structures and skills, sprites, sounds, and
AssetBundles) is covered in [`docs/ASSETS_AND_CONTENT.md`](docs/ASSETS_AND_CONTENT.md).
The content-injection framework that backs new structures and skills has its own
specification in [`docs/CONTENT_FRAMEWORK.md`](docs/CONTENT_FRAMEWORK.md).

### Build a mod

```bash
tools/build-mod.sh path/to/MyMod  "/path/to/Ruinarch/Mods"
```

This compiles against the game's assemblies, the modding API, and Harmony, and
installs the result into the target `Mods/` folder (with `0Harmony.dll`). It also
deploys the mod's `art/`, `audio/` and `bundles/` folders, and first checks every
`[HarmonyPatch]` in the mod against the real game DLLs (`tools/check-patches.sh`): a
patch whose target method does not exist fails the build instead of failing at launch.

### Test a mod in the running game

```bash
tools/run-autotest.sh [timeout-seconds]
```

Launches Ruinarch through Steam with the test harness of the `RuinarchDebug` mod armed
(from the [RuinarchMods](https://github.com/Xm0x/RuinarchMods) repo; it must be deployed).
The harness starts a new world by itself, places the portal, runs the world at speed,
plays out test scenarios, writes `PASS`/`FAIL` lines to `Mods/RuinarchDebug/autotest.log`,
and quits the game. The script prints that log and exits non-zero on any failure.
Steam must be running.

## Build from source

```bash
export RUIN_GAME_DIR="/path/to/Ruinarch"   # your install (for build-time refs)
tools/build.sh                              # loader + patcher -> build/
```

```bash
tools/build-gui.sh                          # graphical installer -> build/gui/{linux-x64,win-x64}
```

`build/patcher/` then holds the patcher plus `Ruinarch.Modding.dll` and
`0Harmony.dll`, ready to run or package with `tools/package-release.sh`.

Requirements: .NET SDK (8.x) and a legitimate Ruinarch install to reference the
game's Unity DLLs at build time.

## License

MIT (this project's code). See [LICENSE](LICENSE). Ruinarch and its assets belong to their
respective owners; this project is not affiliated with or endorsed by them.
