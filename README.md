# RuinarchModLoader
DISCLAIMER: For 100% honesty, help of AI was used in this project.

A mod loader for **Ruinarch**, with [Harmony](https://github.com/pardeike/Harmony)
runtime patching built in. Install it into your own copy of the game, drop mods
in a folder, and they load at startup. No BepInEx, no external injector.

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

> After a game update Steam replaces `Assembly-CSharp.dll`; open the installer
> again and click **Reinstall**.

The installer also puts helper DLLs in `Mods/`: `Ruinarch.ModContent.dll`, the
framework that lets mods add new buildings, skills and villager actions;
`Mono.Cecil.dll`, which the loader uses to inspect packages before loading them; and
`Ruinarch.ModMenu.dll`, which replaces the game's **Mods** window (main menu) with a list
of your packages, local and subscribed on the Steam Workshop. From there you can turn
each one on or off (takes effect after a restart), see why a package is not compatible,
browse the Workshop, upload your own package, read `mods.log`, and open the `Mods/`
folder. Installing, sharing and the Workshop are explained in
[`docs/PACKAGES.md`](docs/PACKAGES.md).

### Updating

From version 0.6.0 the loader updates itself. When a newer release is out, the main menu
shows a notice with an **Update** button. The download is checked against a signature
made by the release author, and the new version installs the next time you start the
game. You still need the installer after a Steam update of Ruinarch (click
**Reinstall**), and the notice says so when a release needs it. Coming from 0.5.0 or
older, run the installer once.

Release authors: see [`docs/RELEASING.md`](docs/RELEASING.md).

For ready-made mods, see [RuinarchMods](https://github.com/Xm0x/RuinarchMods).

### Version 0.9.0

The Workshop upload panel accepts a primary-picture path, with package-root
`preview.png` as the default. Missing, empty, unsupported or oversized pictures are
reported before creating an item. See [Uploading your package](docs/PACKAGES.md#uploading-your-package).

Template footprints follow the rendered floor after resizing or changing the building's
center, including borrowed tilemaps with a different local origin.

### Version 0.8.0

Mod settings: mods describe their options with attributes and players change them in the game's Settings window, in a new Mods tab. Values are saved in `Mods/settings/`. Mods that use it declare `"loaderApi": 2`; see [Settings](docs/WRITING_MODS.md#settings).

### Version 0.7.0

Assembly mods (compiled .NET DLLs implementing `IRuinarchMod`) can add usable demonic
buildings: deferred skill construction, copied skill settings, normal grants, placement
using borrowed prefabs and charge refunds on destruction. Ruinarch+ 0.11.0 needs this for
its Blight Heart. Mod authors: see [`docs/CONTENT_FRAMEWORK.md`](docs/CONTENT_FRAMEWORK.md).

Fixed: saved worlds with a mod building (a Ruinarch+ Library, Town Hall or Mass Grave)
stopped loading partway, because the game's save wrote the building's type as empty.
New saves keep the type, and saves made with older versions load again: each such
building is recognised by its name.

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

Every mod folder needs a `mod.json` next to your DLL, naming the loader API, the DLL and
the class above (`MyMod` here, in no namespace):

```json
{ "id": "you.mymod", "name": "My Mod", "version": "1.0.0", "author": "you", "description": "...",
  "loader": "RuinarchModLoader", "loaderApi": 1, "type": "code",
  "entryAssembly": "MyMod.dll", "entryType": "MyMod" }
```

The loader checks it before loading anything; see [`docs/PACKAGES.md`](docs/PACKAGES.md).

The full API and patterns are in [`docs/WRITING_MODS.md`](docs/WRITING_MODS.md).
Adding **new content** (new structures, skills and villager actions, sprites, sounds,
and AssetBundles) is covered in [`docs/ASSETS_AND_CONTENT.md`](docs/ASSETS_AND_CONTENT.md).
The content-injection framework that backs new structures, skills and actions has its
own specification in [`docs/CONTENT_FRAMEWORK.md`](docs/CONTENT_FRAMEWORK.md).
New looks for the game's buildings, as data-only template packs, are covered in
[`docs/TEMPLATES.md`](docs/TEMPLATES.md).

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
tools/run-autotest.sh [timeout-seconds] [suites]
```

Launches Ruinarch through Steam with the test harness of the `RuinarchDebug` mod armed
(from the [RuinarchMods](https://github.com/Xm0x/RuinarchMods) repo; it must be deployed).
The harness starts a new world by itself, places the portal, runs the world at speed,
plays out test scenarios, writes `PASS`/`FAIL` lines to `Mods/RuinarchDebug/autotest.log`,
and quits the game. Earlier runs are kept in `Mods/RuinarchDebug/logs/`, named by date and
time (newest 20). The script prints that log and exits non-zero on any failure.
Steam must be running. To run only some of the harness's suites (much faster while working
on one feature), name them, comma-separated: `tools/run-autotest.sh 900 HuntSuite,TradeSuite`.
The world is still generated and checked first.

## Build from source

```bash
export RUIN_GAME_DIR="/path/to/Ruinarch"   # your install (for build-time refs)
tools/build.sh                              # loader + patcher -> build/
dotnet build/patcher/RuinarchModLoader.Patcher.dll --game "$RUIN_GAME_DIR"  # install into your copy
```

```bash
tools/build-gui.sh                          # graphical installer -> build/gui/{linux-x64,win-x64}
```

`build/patcher/` then holds the patcher plus `Ruinarch.Boot.dll`, `Ruinarch.Modding.dll`,
`0Harmony.dll`, `Mono.Cecil.dll`, `Ruinarch.ModContent.dll` and `Ruinarch.ModMenu.dll`,
ready to run or package with `tools/package-release.sh` (see
[`docs/RELEASING.md`](docs/RELEASING.md)).

Requirements: .NET SDK (8.x) and a legitimate Ruinarch install to reference the
game's Unity DLLs at build time.

## License

MIT (this project's code). See [LICENSE](LICENSE). Ruinarch and its assets belong to their
respective owners; this project is not affiliated with or endorsed by them.
