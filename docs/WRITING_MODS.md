# Writing mods

Everything a mod needs lives in the `Ruinarch.Modding` namespace, plus
`HarmonyLib` for runtime patching.

## The entry point: `IRuinarchMod`

Implement it on exactly one public, parameterless-constructible class. The loader
finds it, instantiates it, and calls `OnLoad` once, before the first scene loads.

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
    public ModInfo   Info         { get; } // metadata from mod.json (or defaults)
    public string    ModDirectory { get; } // absolute path this DLL loaded from
    public string    ModsRoot     { get; } // absolute path to Mods/
    public ModLogger Logger       { get; } // scoped logger
}
```

## Metadata: `mod.json`

Optional, placed next to your DLL. Missing fields fall back to defaults derived
from the DLL name. Parsed with Unity's `JsonUtility`, so no extra dependency.

```json
{
  "id": "author.mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "you",
  "description": "what it does"
}
```

`Info.id` is a good argument for `new Harmony(id)`, so each mod's patches are
grouped under a unique owner.

## Logging: `ModLogger`

`context.Logger.Info/Warning/Error` write to Unity's `Player.log` (prefixed with
your mod id) and append to `Mods/mods.log`. Use `mods.log` while iterating: the
Unity log is buffered and lags, `mods.log` is flushed on every write.

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

Drop any extra DLL your mod needs anywhere under `Mods/`. The loader's
`AssemblyResolve` hook finds dependencies by simple name across the whole `Mods/`
tree, so a shared library in `Mods/` or in your mod's subfolder both resolve.

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
