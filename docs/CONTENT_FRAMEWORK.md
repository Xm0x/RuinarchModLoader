# The content framework (`Ruinarch.ModContent`)

`Ruinarch.ModContent` is a small library that lets a mod add genuinely **new**
content to the stock game: a new buildable structure with its own
`STRUCTURE_TYPE`, its own structure class, and its own build skill. It does this
**without editing the game's assembly**. The game file stays exactly as
installed, so every mod coexists and no game code is redistributed.

This document is the full specification. If you only want to add a structure and
follow a worked example, read
[`ASSETS_AND_CONTENT.md`](ASSETS_AND_CONTENT.md) first; it introduces the same
framework in plain terms and pairs it with the art/asset ladder. This document
covers exactly how the framework works, which game methods it patches, and the
public API.

The framework name is `Ruinarch.ModContent`. In prose it is called "the content
framework" or "ModContent".

## Why the framework exists

Harmony is enough for most mods: it rewrites methods that already exist, so you
can make an action cheaper, change a rule, or react to an event. What Harmony
cannot do is **invent a new named thing** in the game's compiled assembly. In
particular it cannot:

- add a new value to the `STRUCTURE_TYPE` enum,
- add a new structure class for that value, or
- add a new build skill the player can unlock.

The obvious alternative, editing `Assembly-CSharp.dll` to add those, is
rejected. Editing the shipped assembly permanently changes the game file, breaks
the "stock DLL" model the loader depends on, and pollutes the decompiled
reference that modders read to find what to patch. The framework exists so that
new enum-backed content can be added at runtime, against an unmodified
`Assembly-CSharp`.

## How the game creates content, and how the framework hooks it

Many kinds of content in the game are identified by an enum value, for example
`STRUCTURE_TYPE.CRYPT`. When the game needs to create one, it does not call your
code. It turns the enum value into a class name and builds that class by
reflection, in the shape:

```csharp
Type.GetType("<namespace>." + enumValue.ToStringEnumNoSpace() + ", Assembly-CSharp");
Activator.CreateInstance(type, args);
```

For a brand new structure, that lookup would need a new enum value and a matching
class inside `Assembly-CSharp`, neither of which a mod can add. The framework
solves this in two steps.

### 1. Allocate a deterministic virtual enum value

When you register content, the framework allocates a **virtual** enum value: a
cast integer in a reserved high range that no real game value uses. The real game
`STRUCTURE_TYPE` / `PLAYER_SKILL_TYPE` values top out in the low hundreds; the
reserved range is `[100000, 1000000)` (`VirtualBase = 100000`,
`VirtualRange = 900000`).

The value is derived from your string id with an FNV-1a hash into that range,
linear-probing on collision. The same id therefore always maps to the same
value, on every machine and in every session, which is what lets a saved game
reload your content later. Choose the id once and never change it after players
have saves.

Crucially, these values are cast integers, not real enum members, so
`Enum.GetValues(typeof(STRUCTURE_TYPE))` never returns them. Every
"iterate all enum values" loop in the game (world generation and the like)
simply ignores virtual content. Virtual values only surface through the specific
reflection factories the framework patches, which is exactly the control that is
wanted.

### 2. Prefix the reflection factories

For a virtual value the reflection lookup above returns `null` and the stock game
throws. The framework puts a Harmony **prefix** on each factory. When the game
tries to create content for a registered virtual value, the prefix returns the
mod's instance and skips the reflection entirely (`return false`). For any value
the framework does not own, the prefix returns `true` and the stock code runs
unchanged. The game's assembly is never edited.

## Patch table

Every patch below lives in `src/Ruinarch.ModContent/ContentPatches.cs`. Method
names are the exact game methods patched.

| Patch | Game method | Kind | Purpose |
|---|---|---|---|
| `Patch_CreateNewStructureAt` | `LandmarkManager.CreateNewStructureAt` | Prefix | When the player places a registered structure, build it with the registration's `Factory`, add it to the region and settlement, initialize it, register it in the structure database, and skip the stock reflection factory. |
| `Patch_LoadNewStructureAt` | `LandmarkManager.LoadNewStructureAt` | Prefix | On save reload, rebuild a registered structure with the registration's `LoadFactory`, run `InitializeFromSave` unless it was destroyed, and register it in the structure database. |
| `Patch_GetStructureData` | `LandmarkManager.GetStructureData` | Prefix | For a registered type, return the `StructureData` (prefab, visual, footprint) of the registration's `PrefabSource`, so no new Unity asset is required. |
| `Patch_ConstructDemonicSkills` | `PlayerSkillManager.ConstructAllDemonicStructureSkillsData` | Postfix | After the game builds its skill dictionary from its fixed array, add each registered skill straight into `allDemonicStructureSkillsData`. The fixed-size demonic-skills array is **never** grown (see the gotcha below); only the dictionary that the build menu and `GetDemonicStructureSkillData` actually read is extended. |
| `Patch_IsPlayerStructure` | `Extensions.IsPlayerStructure(STRUCTURE_TYPE)` | Postfix | Return `true` for a registered type when its `IsPlayerStructure` flag is set. The game has no separate "demonic" switch: a demonic (player-built) structure is a player structure. |
| `Patch_IsSpecialStructure` | `Extensions.IsSpecialStructure(STRUCTURE_TYPE)` | Postfix | Return `true` for a registered type when its `IsSpecialStructure` flag is set. |
| `Patch_IsVillageStructure` | `Extensions.IsVillageStructure(STRUCTURE_TYPE)` | Postfix | Return `true` for a registered type when its `IsVillageStructure` flag is set, so villagers treat it as a normal village building (placement rules, settlement ownership). |
| `Patch_StructureToStringEnum` | `StringEnumLookUp.ToStringEnum(STRUCTURE_TYPE)` | Prefix | Name a registered type (`"Mass Grave"` becomes `"MASS_GRAVE"`). The game's lookup is a raw dictionary index that throws for any virtual value. |
| `Patch_StructureToStringEnumWithSpace` | `StringEnumLookUp.ToStringEnumWithSpace(STRUCTURE_TYPE)` | Prefix | Display form of a registered type (its `DisplayName`). |
| `Patch_LocalizedStructureName` | `Extensions.LocalizedStructureName(STRUCTURE_TYPE)` | Prefix | Localized name of a registered type (its `DisplayName`); the game's localization table has no entry for it. |
| `Patch_UnlockRegisteredSkill` | `PlayerSkillComponent.AddAndCategorizePlayerSkill` | Postfix | When the player gains a registration's `UnlockWith` source skill, also grant the registered virtual skill (and broadcast the gained-skill signal) so it appears in the dynamic build menu. |

The `Extensions` classification patches make registered types answer the game's own
type-classification switches, so the rest of the game treats the content as
first-class. The name patches matter more than they look: every log line, job
description and UI panel that mentions a structure names it through those lookups, and
without them any mention of a registered structure throws.

`Install()` applies the patches **one class at a time**. With `PatchAll`, a single patch
whose target method does not exist aborts every patch after it and leaves the framework
half-installed; applied individually, a bad patch only disables itself and is named in
the log (`[ModContent] Patch X failed: ...`). The build also checks every patch
statically (see `tools/check-patches.sh` below).

## The public API

The entry point is the static class `ModContent` in
`src/Ruinarch.ModContent/ModContent.cs`.

```csharp
// Register a new buildable structure. Allocates the virtual STRUCTURE_TYPE and
// PLAYER_SKILL_TYPE, stores the registration, and returns it with those values
// filled in. Calls Install() for you.
public static StructureRegistration RegisterStructure(StructureRegistration reg);

// The virtual STRUCTURE_TYPE allocated for a registered id (or default(0) if the
// id was never registered).
public static STRUCTURE_TYPE StructureTypeFor(string id);

// The virtual PLAYER_SKILL_TYPE allocated for a registered id's skill (or
// PLAYER_SKILL_TYPE.NONE). A mod's skill class resolves its own `type` getter
// through this.
public static PLAYER_SKILL_TYPE SkillTypeFor(string id);

// Idempotent. Applies the framework's Harmony patches. Auto-called by
// RegisterStructure; a mod may also call it explicitly (order-independent).
public static void Install();
```

`ModArt` (`src/Ruinarch.ModContent/ModArt.cs`) turns loose image files shipped with a
mod into sprites, with no AssetBundle and no Unity editor. `tools/build-mod.sh` deploys a
mod's `art/`, `audio/` and `bundles/` folders next to its DLL, so a mod finds its files
under `context.ModDirectory`.

```csharp
// Decode a PNG/JPG into a point-filtered Sprite (cached per path + ppu + pivot).
// Returns null, never throws, for a missing file or bad image data.
public static Sprite LoadSprite(string absolutePath, float pixelsPerUnit = 64f, Vector2? pivot = null);
```

Call it from gameplay code, **never from `OnLoad`**. Mods load inside the game
assembly's module initializer, before Unity's graphics device exists; creating a
texture there crashes the game outright (not a catchable exception). `LoadSprite`
refuses and logs a warning if it is called before the first rendered frame.

`StructureRegistration` (see `src/Ruinarch.ModContent/StructureRegistration.cs`)
describes one new structure. You fill in the input fields; the framework fills in
`StructureType` and `SkillType` during registration.

```csharp
public sealed class StructureRegistration
{
    // Stable string id, e.g. "yourmod.thing". Drives the deterministic
    // virtual-enum allocation, so never change it once players have saves.
    public string Id;

    // Human display name for menus/tooltips. Optional.
    public string DisplayName;

    // Builds a fresh instance. Receives the allocated virtual type, which your
    // structure class must pass to its base(STRUCTURE_TYPE, Region) constructor.
    public Func<STRUCTURE_TYPE, Region, LocationStructure> Factory;

    // Rebuilds an instance from a save. Receives the allocated virtual type.
    public Func<STRUCTURE_TYPE, Region, SaveDataLocationStructure, LocationStructure> LoadFactory;

    // An existing structure whose StructureData (prefab/visual/footprint) this
    // reuses, so no new Unity asset is required. e.g. STRUCTURE_TYPE.CRYPT.
    public STRUCTURE_TYPE PrefabSource;

    // The build-skill instance (a DemonicStructurePlayerSkill subclass) that puts
    // the structure in the demonic build menu.
    public DemonicStructurePlayerSkill Skill;

    // If set, the structure's skill is granted whenever the player gains this
    // source skill, so it appears alongside a related structure. NONE means the
    // mod grants the skill itself.
    public PLAYER_SKILL_TYPE UnlockWith = PLAYER_SKILL_TYPE.NONE;

    // Classification flags mirrored into the game's Extensions switches. A demonic
    // (player-built) structure is IsPlayerStructure; a normal village building that
    // villagers own and build is IsVillageStructure.
    public bool IsPlayerStructure = true;
    public bool IsSpecialStructure = false;
    public bool IsVillageStructure = false;

    // Filled by the framework during RegisterStructure:
    public STRUCTURE_TYPE StructureType { get; internal set; }
    public PLAYER_SKILL_TYPE SkillType { get; internal set; }
}
```

### Example

Call the API from your mod's `OnLoad`. `YourStructure` and `YourStructureData`
are classes you write in your own mod DLL; you never edit the game to create
them.

```csharp
using Ruinarch.ModContent;

var registration = ModContent.RegisterStructure(new StructureRegistration {
    // Stable, unique id. Never change it once players have saves.
    Id = "yourmod.thing",

    // How to build a fresh instance when the player constructs it.
    Factory = (type, region) => new YourStructure(type, region),

    // How to rebuild it when a saved game is loaded.
    LoadFactory = (type, region, save) => new YourStructure(region, save),

    // Borrow an existing structure's visual so no new art is needed to start.
    PrefabSource = STRUCTURE_TYPE.CRYPT,

    // The build skill that adds your structure to the build menu.
    Skill = new YourStructureData(),

    // Show it in the menu wherever this existing skill is unlocked.
    UnlockWith = PLAYER_SKILL_TYPE.CRYPT,
});

// The allocated virtual values are now available:
STRUCTURE_TYPE type = ModContent.StructureTypeFor("yourmod.thing");
PLAYER_SKILL_TYPE skill = ModContent.SkillTypeFor("yourmod.thing");
```

Your build-skill class should resolve its own `type` getter lazily through
`ModContent.SkillTypeFor(id)`, so it reads the correct virtual value after
registration.

## Saving your mod's data: `ModSave`

The game only saves what its own classes know about, so a mod that remembers
anything (who knows what, how old someone is, a settlement's tier) needs its own
storage. `ModSave` (`src/Ruinarch.ModContent/ModSave.cs`) keeps it **inside the
player's save file**.

How it works: a Ruinarch save is a zip of the game's temp folder
(`Utilities.tempZipPath`), and loading a save extracts that zip back into
`Utilities.tempPath`. Just before the game saves, the framework writes each
registered mod's data as `ModData/<id>.json` into that folder, so it ends up in
the zip. Once a save has finished loading, it reads the file back. The data
travels with the save: copying, renaming or deleting a save does the same to your
mod's data. The game ignores the extra file, so a save made with your mod still
loads without it.

```csharp
ModSave.Register("yourmod.feature",
    save: () => JsonUtility.ToJson(myState),        // null = nothing to store
    load: json => myState = json == null ? new MyState() : JsonUtility.FromJson<MyState>(json));
```

For every registered id:

| Moment | Call | Game hook |
|---|---|---|
| Any game starts, new or loaded | `load(null)`, so no state leaks from the previous game | postfix `Initializer.InitializeDataBeforeWorldCreationMainThread` |
| The player saves, or an autosave runs | `save()`; the result is written into the save | prefix `SaveCurrentProgressManager.DoManualSave` |
| A save has finished loading, whole world in place | `load(json)`, or `load(null)` if the save has no data for this id | postfix `SaveManager.DeleteSaveFilesInTempDirectory` |

Store references as persistent ids (`faction.persistentID`,
`structure.persistentID`, `character.persistentID`) and resolve them in `load`
(`FactionManager.Instance.GetFactionByPersistentID`,
`DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe`):
the objects exist by then. Keep the id stable across versions, or older saves
lose their data. A handler that throws is logged and skipped; it never stops the
game saving or loading, or the other mods.

## Gotchas

- **`Messenger` is internal to `Assembly-CSharp`.** An external mod assembly
  cannot call it directly. Reach it through reflection or Harmony's
  `AccessTools` (the unlock patch broadcasts the gained-skill signal this way).
- **Virtual enum values are invisible to `Enum.GetValues()`.** That is deliberate
  and keeps world generation from touching your content, but it means you must
  drive content through the specific factories the framework patches. Never
  expect a registered type to show up in a "for every enum value" loop.
- **Do not grow the fixed demonic-skills array.** The game builds its skill
  dictionary from a fixed-size array whose length drives a fixed UI slot array;
  adding to that array throws once the UI reads it. The framework adds registered
  skills to the runtime **dictionary** instead, which the build menu and
  `GetDemonicStructureSkillData` actually read.
- **A reused prefab keeps its own type.** When villagers build from a blueprint, the
  finished structure's type is read from the placed prefab's
  `LocationStructureObject.structureType` (`GenericTileObject.BuildBlueprint`), not from
  the job's `StructureSetting`. Borrowing another structure's prefab through
  `PrefabSource` therefore builds *that* structure unless your mod redirects the type at
  construction time. Never mutate the prefab's field: prefabs are pooled and shared with
  the real structure. See Ruinarch+'s `MassGraveConstruction` for a working pattern.
- **Release builds turn Unity logging off** after startup (`WorldConfigManager.Awake`
  sets `Debug.unityLogger.logEnabled = false`). `Debug.Log` from gameplay code never
  reaches `Player.log`; log through your mod's `ModLogger` (`mods.log`) instead.

## Checking patches before you ship: `tools/check-patches.sh`

A Harmony patch whose target method does not exist fails only at launch, and with
`PatchAll` it silently takes every later patch down with it. `tools/check-patches.sh
<assembly.dll>...` resolves every class-level `[HarmonyPatch(typeof(T), "Method"
[, Type[] args])]` against the real game DLLs without running the game. It reports:

- a target method that does not exist on the type or its base types,
- an overloaded target given without argument types (Harmony would reject it as
  ambiguous),
- a patch parameter whose name is not a parameter of the target (or `__instance` on a
  static method, `__result` on a void one, a `___field` that does not exist).

`tools/build.sh` runs it on the framework and the mod menu, and `tools/build-mod.sh`
runs it on every mod it builds and refuses to deploy a mod that fails.

## Shipping and installation

The framework ships as `Ruinarch.ModContent.dll` placed in the game's `Mods/`
folder, alongside `0Harmony.dll`, the same way any shared mod dependency is
shipped (the loader resolves dependencies by simple name across the whole
`Mods/` tree). It has no coupling to the loader: it self-installs its Harmony
patches the first time any mod touches the API, which happens during mod load,
before the game builds its skill and structure tables. `Install()` is
idempotent, so calling the register methods or `Install()` more than once is
safe and order-independent.
