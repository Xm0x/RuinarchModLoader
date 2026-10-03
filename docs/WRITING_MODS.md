# Writing mods

Everything a mod needs lives in the `Ruinarch.Modding` namespace, plus
`HarmonyLib` for runtime patching.

## The entry point: `IRuinarchMod`

Implement it on exactly one public, parameterless-constructible class and name that class
in `mod.json` (below). The loader instantiates it and calls `OnLoad` once, before the
first scene loads.

```csharp
public interface IRuinarchMod
{
    void OnLoad(ModContext context);
}
```

Throwing inside `OnLoad` is caught and logged; it will not crash the game or the
other mods.

## What you are handed: `ModContext`

```csharp
public sealed class ModContext
{
    public ModInfo   Info         { get; } // metadata from mod.json
    public string    ModDirectory { get; } // absolute path of your package folder
    public string    ModsRoot     { get; } // absolute path to Mods/
    public ModLogger Logger       { get; } // scoped logger
}
```

## Metadata: `mod.json`

Required, in your package folder next to your DLL. The loader reads it, and your DLL's
metadata, before loading any code; a package with a missing or wrong manifest is shown as
not compatible and never loaded.

```json
{
  "id": "author.mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "you",
  "description": "what it does",
  "loader": "RuinarchModLoader",
  "loaderApi": 1,
  "type": "code",
  "entryAssembly": "MyMod.dll",
  "entryType": "MyNamespace.MyMod"
}
```

Every field and check is described in [PACKAGES.md](PACKAGES.md), along with sharing
your mod on the Steam Workshop. `Info.id` is a good argument for `new Harmony(id)`, so
each mod's patches are grouped under a unique owner.

## Logging: `ModLogger`

`context.Logger.Info/Warning/Error` append to `Mods/mods.log` (flushed on every write)
and also go to Unity's `Player.log`, prefixed with your mod id. Rely on `mods.log`: the
shipped game switches Unity logging off once it starts initializing the world
(`WorldConfigManager.Awake`), so anything logged during play, including a plain
`Debug.Log`, never reaches `Player.log`.

`mods.log` starts fresh at every launch. The previous launch's log is kept in
`Mods/logs/mods-<date>_<time>.log` (named by when it was last written); the newest 20
are kept.

## Settings

Players change your mod's options in the game: the Settings window has a **Mods** tab that
lists every installed mod with settings. You describe the options once, as a class; the
loader draws the controls, saves the values and writes changes straight into your object.

```csharp
using Ruinarch.Modding;
using UnityEngine;   // for [Range]

[ModSettings("My Mod")]
public class MyModSettings
{
    [Section("Monsters")]
    [Setting("Angry wolves", "Wolves attack anyone they see.")]
    public bool angryWolves = true;

    [Setting("Pack size"), Range(1, 12)]
    public int packSize = 4;

    [Setting("Hunger per hour"), Range(0f, 2f)]
    public float hunger = 0.5f;

    [Setting("Starting season"), RequiresRestart]
    public Season season = Season.Spring;

    public int[] notShown = { 1, 2 };   // no [Setting]: saved, not shown
}

public class MyMod : IRuinarchMod
{
    internal static MyModSettings Settings;

    public void OnLoad(ModContext context)
    {
        Settings = context.Settings.Register<MyModSettings>();
        context.Settings.Changed += field => { /* only if you must react to a change */ };
    }
}
```

- `bool` is a checkbox, `int` and `float` are sliders (they need `[Range]`), an `enum` is a
  dropdown of its names. Any other type, or a number without `[Range]`, is not shown; the
  reason is written to `Mods/mods.log`.
- `[Section("...")]` starts a header that covers the following fields up to the next one.
- Read your fields where you use them (`MyMod.Settings.packSize`); the loader writes a change
  into the same object at once.
- Mark a field `[RequiresRestart]` when you read it only while loading. Its change is saved,
  your object keeps the old value until the next start, and the tab shows "Restart to apply".
- `Changed` is raised on the main thread after a change is saved, with the field's name.
- Values are saved in `Mods/settings/<your mod id>.json`, outside your mod folder, so they
  survive updates. Fields missing from the file get your defaults, names your class no longer
  has are dropped, numbers are kept inside their `[Range]`, and a file that cannot be read is
  renamed to `.json.bad` while your mod starts on its defaults.
- A mod that uses settings must declare `"loaderApi": 2` in `mod.json` and needs
  RuinarchModLoader 0.8.0 or newer. Older loaders refuse it with a clear message.

## Patching the game with Harmony

`0Harmony.dll` is installed into `Mods/` and resolved for every mod. Create a
Harmony instance in `OnLoad` and patch by attribute or manually.

```csharp
public void OnLoad(ModContext context)
{
    var harmony = new Harmony(context.Info.id);
    harmony.PatchAll(typeof(MyMod).Assembly);
}

[HarmonyPatch(typeof(JobManager), "CanTakeJob")]
static class Patch
{
    static void Postfix(ref bool __result) { /* ... */ }
}
```

Because the game's type, method, and field names are intact, you patch against
real names directly. Read the decompiled source (the RuinarchRE project) to find
exactly what to hook: behaviours, the job system, the GOAP planner, traits, and
so on.

### A gotcha: Mono inlines trivial methods

A Harmony patch on a very small method can look like it never fired, because the
JIT inlined the callsite and never entered the patched body. That is a false
negative, not a Harmony failure. Real game methods (Unity messages like `Start`,
virtual overrides, anything called by reflection) are not inlined and patch
normally. If you ever need a tiny method to be patchable, mark it
`[MethodImpl(MethodImplOptions.NoInlining)]`.

## Dependencies

`0Harmony.dll` and `Ruinarch.ModContent.dll` are shared and always available. Any other
DLL your mod needs goes in your package folder and is listed in `mod.json`:
`"dependencies": ["lib/MyLibrary.dll"]`. The loader resolves dependencies only from
accepted, enabled packages; a package may not ship a copy of a game, Unity, .NET or loader
assembly.

## Building

```bash
tools/build-mod.sh path/to/MyMod  "/path/to/Ruinarch/Mods"
```

It references every game assembly plus the modding API and Harmony, compiles your
`.cs` files into `MyMod.dll`, copies your `mod.json`, and ensures `0Harmony.dll`
is present in the `Mods/` root. Launch the game to load it; watch `mods.log`.

## Adding new content (structures, skills, art)

Harmony changes behaviour that already exists. To add genuinely **new** content
(a new `STRUCTURE_TYPE`, a new build skill, or new sprites, sounds, and
AssetBundle prefabs), see [`ASSETS_AND_CONTENT.md`](ASSETS_AND_CONTENT.md). It
covers the `Ruinarch.ModContent` framework and the loose-PNG to AssetBundle
asset ladder.
