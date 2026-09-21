# RuinarchModLoader

A first-class mod loader for **Ruinarch**, with [Harmony](https://github.com/pardeike/Harmony)
runtime patching built in. Install it into your own copy of the game, drop mods
in a folder, and they load at startup. No BepInEx, no external injector.

> This is a fan-made modding tool. It ships **no game code or assets**. It edits
> a copy of Ruinarch that **you already own**, on your own machine. You need a
> legitimate copy of the game.

## How it works

Most Unity mod loaders sit beside the game as an external injector (a proxy DLL
or a doorstop). This one is different: a small **patcher** adds a single call to
the loader into your own `Assembly-CSharp.dll`, so the loader is part of the game
after install. That call runs the instant the game assembly loads, before any
scene, and scans `Mods/` for mod DLLs.

- The patcher backs up your original as `Assembly-CSharp.dll.orig`, so uninstall
  is a clean revert.
- Verified working on the stock Steam build under Proton/Wine and Mono.

## Install (players)

1. Download the latest release and unzip it. Keep the files together
   (`RuinarchModLoader.Patcher`, `Ruinarch.Modding.dll`, `0Harmony.dll`).
2. Run the patcher, pointing it at your Ruinarch install folder (the one with
   `Ruinarch.exe`):

   ```bash
   # Linux
   dotnet RuinarchModLoader.Patcher.dll --game "/path/to/Ruinarch"
   ```
   ```bat
   REM Windows
   RuinarchModLoader.Patcher.exe --game "C:\Program Files (x86)\Steam\steamapps\common\Ruinarch"
   ```

   Find the folder in Steam: right-click Ruinarch, Manage, Browse local files.
3. Put mods in the `Mods/` folder that now sits next to `Ruinarch.exe`
   (each mod is a `.dll`, optionally in its own subfolder).
4. Launch the game normally through Steam.

Check `Mods/mods.log` to see what loaded.

### Uninstall

```bash
dotnet RuinarchModLoader.Patcher.dll --game "/path/to/Ruinarch" --uninstall
```

Restores the original `Assembly-CSharp.dll` and removes the loader. Your `Mods/`
folder is left alone.

> After a game update, Steam replaces `Assembly-CSharp.dll`. Just run the patcher
> again to re-install.

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

### Build a mod

```bash
tools/build-mod.sh path/to/MyMod  "/path/to/Ruinarch/Mods"
```

This compiles against the game's assemblies, the modding API, and Harmony, and
installs the result into the target `Mods/` folder (with `0Harmony.dll`).

## Build from source

```bash
export RUIN_GAME_DIR="/path/to/Ruinarch"   # your install (for build-time refs)
tools/build.sh                              # loader + patcher -> build/
```

`build/patcher/` then holds the patcher plus `Ruinarch.Modding.dll` and
`0Harmony.dll`, ready to run or package with `tools/package-release.sh`.

Requirements: .NET SDK (8.x) and a legitimate Ruinarch install to reference the
game's Unity DLLs at build time. Nothing from the game is committed or shipped.

## Notes

- **Normal Steam launch works.** If you launch `Ruinarch.exe` directly (outside
  Steam) while the Steam client is running, Steamworks may bounce the process in
  a restart loop; that is a launch-environment quirk, not a loader issue. Launch
  through Steam.
- `mods.log` is flushed on every write; the Unity `Player.log` is buffered, so
  `mods.log` is the reliable place to watch mod output.
- A mod that throws while loading is logged and skipped; it never takes down the
  game or other mods.

## License

MIT (our code). See [LICENSE](LICENSE). Ruinarch and its assets belong to their
respective owners; this project is not affiliated with or endorsed by them.
