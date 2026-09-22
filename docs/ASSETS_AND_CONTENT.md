# Assets & new content

How to give a Ruinarch mod **new content** — new structures, new skills, new
sprites, new sounds — without forking the game DLL and without shipping (or
replacing) the game itself.

Read [`WRITING_MODS.md`](WRITING_MODS.md) first for the loader + Harmony basics.
This doc is about the two things plain Harmony can't do on its own: **adding new
enum-backed content**, and **adding new art/audio**.

> **The golden rule.** Everything a mod adds is an *additive file* under `Mods/`.
> The stock `Assembly-CSharp.dll` stays stock, every game file is untouched, and
> other mods keep working. You never rebuild, repackage, or redistribute the
> game. This is the same footing every BepInEx-style mod stands on.

---

## Two separate problems

Modding "new content" is really two problems, and they have different answers:

1. **New *logic/identity*** — a new `STRUCTURE_TYPE`, a new build skill, a new
   class the game instantiates by enum name. Harmony patches *existing* methods;
   it can't mint a new enum value or a new type. → solved by the **content
   framework** (`Ruinarch.ModContent`), below.
2. **New *art/audio*** — a sprite, an icon, a portrait, a structure's look, a
   sound. → solved by the **asset ladder**, below.

A feature like Mass Grave uses both: the framework registers it as a real
buildable structure, and (for now) it borrows an existing prefab's visual.

---

## Part 1 — New logic: the content framework (`Ruinarch.ModContent`)

### Why it's needed

The game instantiates content by **reflecting on an enum name**:

```csharp
Type.GetType("<ns>." + enumValue.ToStringEnumNoSpace() + ", Assembly-CSharp");
Activator.CreateInstance(...);
```

Harmony can't add a new value to `STRUCTURE_TYPE` or a new class to
`Assembly-CSharp`. Baking those into the game DLL was tried and **rejected** — it
forks the DLL and pollutes the decompiled reference. The framework solves it
without touching the DLL.

### How it works

1. It allocates a **virtual enum value** — a cast-`int` in `[100000, 1000000)`,
   derived deterministically from a string id via FNV-1a (so saves stay stable
   across sessions and machines). `Enum.GetValues()` never returns these, so
   world-gen and every "iterate all enum values" path ignores virtual content.
   *This is why virtual content can't cause a world-gen crash.*
2. It puts a Harmony **prefix** on each reflection factory: for a registered
   virtual value it returns the mod's instance and skips the reflection. Stock
   DLL untouched.

### Registering a structure

```csharp
var reg = ModContent.RegisterStructure(new StructureRegistration {
    Id           = "yourmod.thing",                 // stable → deterministic virtual enum
    Factory      = (type, region)       => new Thing(type, region),
    LoadFactory  = (type, region, save) => new Thing(region, (SaveDataDemonicStructure)save),
    PrefabSource = STRUCTURE_TYPE.CRYPT,             // reuse an existing visual (see Part 2)
    Skill        = new ThingData(),                 // its `type` getter → ModContent.SkillTypeFor(Id)
    UnlockWith   = PLAYER_SKILL_TYPE.CRYPT,          // appears in the build menu with the Crypt
});
```

`ModContent` self-installs its Harmony patches on first use. The
[Mass Grave feature](https://github.com/Xm0x/RuinarchMods) is the reference
consumer.

### What the framework is / isn't for

- **For:** content that needs *new enum values + new classes* the game reflects
  into (new structures, new build skills).
- **Not needed for:** pure mechanics/behaviour changes — those are plain Harmony
  patches (see `WRITING_MODS.md`), already easy.
- **Limit:** because virtual values are invisible to `Enum.GetValues()`, drive
  your content through the *specific* factories, never through code that
  enumerates every enum value.

---

## Part 2 — New art/audio: the asset ladder

Ruinarch is a **2D sprite game on Unity 2020.3.20f1**. That's the single most
important fact for assets: most "new art" is just swapping `Sprite`s, which needs
**no Unity editor at all**. Only a genuinely new *shape/footprint* needs the
editor. Pick the lowest rung that does the job.

### Rung 1 — Reuse an existing asset (zero files, no editor)

Point `PrefabSource` at an existing `STRUCTURE_TYPE` and inherit its visual, as
Mass Grave does with `CRYPT`. Free. Use when an existing look is close enough.

### Rung 2 — Loose PNG → `Sprite` at runtime (no editor)

Ship plain `.png` files next to your DLL and build `Sprite`s at load time. The
Unity editor never opens. This covers **icons, portraits, tile-object sprites,
reskinned structures, and UI** — probably ~80% of what you'll want.

```csharp
static Sprite LoadSprite(string path, float pixelsPerUnit = 64f) {
    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
        filterMode = FilterMode.Point           // crisp pixels; match the game's look
    };
    tex.LoadImage(System.IO.File.ReadAllBytes(path));   // decodes PNG/JPG bytes
    return Sprite.Create(
        tex, new Rect(0, 0, tex.width, tex.height),
        new Vector2(0.5f, 0.5f), pixelsPerUnit);        // pivot centre; match game PPU
}

// after a structure/tile-object spawns, swap what its SpriteRenderers show:
var png = System.IO.Path.Combine(context.ModsRoot, "MyMod/art/mound.png");
foreach (var sr in structureObj.GetComponentsInChildren<SpriteRenderer>())
    sr.sprite = LoadSprite(png);
```

**Audio the same way:** the game's own sounds go through Wwise
(`AkSoundEngine.PostEvent`), which would need a soundbank. Skip it — play a loose
`.wav`/`.ogg` through a plain Unity `AudioSource` you create at runtime.

### Rung 3 — New prefab via AssetBundle (needs the editor, tiny + additive)

Only when you need a genuinely new **shape**: a different footprint, a custom
tilemap layout, an animation, or a particle system. You build one small
`.bundle` in Unity and load it at runtime:

```csharp
var bundlePath = System.IO.Path.Combine(context.ModsRoot, "MyMod/mymod.bundle");
var bundle     = AssetBundle.LoadFromFile(bundlePath);
var prefab     = bundle.LoadAsset<GameObject>("MassGraveMound");
// then register that prefab as your structure's visual via ModContent
```

Still one additive file. It **never** replaces `Assembly-CSharp` or any game data.

---

## What a *structure* prefab actually is

A Ruinarch structure is **not** a free-floating model. It's a GameObject carrying
the game's own `LocationStructureObject` MonoBehaviour, whose serialized fields
are (from the decompiled source):

- `structureType` — the `STRUCTURE_TYPE` enum
- ground / detail / wall **`Tilemap`s** + their `TilemapRenderer`s
- `_size`, `_center`, `_predeterminedOccupiedCoordinates`, `_borderCoordinates`
  — the footprint
- wall types (`_blockWallType`, `_thinWallResource`)
- `StructureConnector[]`, `RoomTemplate[]`, a click collider

So the "asset" is a **tilemap-driven prefab with a game-code component**. That
shapes the three ways to author one:

### Path A — Full editor project referencing the game DLLs (the proper way)

Copy the game's `Assembly-CSharp.dll` + the Unity module DLLs into a **Unity
2020.3.20f1** project. Now `LocationStructureObject`, `STRUCTURE_TYPE`, the
tilemaps, etc. exist in the editor and you build the prefab exactly like the devs
did — add the component, paint tilemaps, set the footprint coordinates, mark it
into an AssetBundle, build. Most work, most freedom (genuinely new footprint).
Keep the `Assembly-CSharp` reference **out** of the shipped bundle — bundles bind
to the game's copy at load time, they don't embed it.

### Path B — Pure-visual prefab + wire the component in code (lighter editor)

Build only the visual hierarchy (sprites, a simple GameObject) in a **clean**
editor project with **no** game DLLs → bundle it. At runtime,
`AddComponent<LocationStructureObject>()` and set its serialized fields (via
reflection, cloning values from an existing template). Editor stays clean; you
pay in runtime wiring code. Good for simple single-sprite footprints.

### Path C — Clone-and-reskin (the pragmatic sweet spot)

Clone an existing structure prefab at runtime — you inherit a *valid*
`LocationStructureObject` with correct tilemaps, coordinates, connectors, and
walls — then swap **only the sprites/tiles** with Rung-2 loose PNGs or a
sprite-only bundle. New look, proven machinery, minimal editor. This is what
"Mass Grave with its own mound sprite" would be.

**Rule of thumb:** new *shape* → Path A. New *look* on existing machinery →
Path C (little/no editor).

---

## AssetBundle gotchas (the ones that actually bite)

- **Unity version must be exactly 2020.3.20f1.** A bundle built in a different
  minor version can fail to load or mis-serialize. Match the game.
- **Shaders:** use Unity's built-in **Sprites/Default** (or the game's sprite
  shader) inside the bundle — not a URP/HDRP shader — or your sprites render
  bright pink (missing shader) under the game's pipeline.
- **Sorting layers + pixels-per-unit** must match the game's, or your object
  floats above/below the map or is the wrong scale.
- **Tilemaps need `Tile` assets**, not just PNGs — that's genuine editor
  authoring, and only matters for Path A / novel footprints.
- **Bundles are additive.** Ship the `.bundle` in your mod folder and load it
  with `AssetBundle.LoadFromFile`; never put it inside the game's `*_Data`.

---

## Licensing / redistribution

You never redistribute the game's assets. Loose PNGs are art **you** make; an
AssetBundle contains **only your** content; reusing an existing prefab happens at
runtime on the player's own legally-installed copy — nothing of theirs is
shipped. That's the same clean footing every mod for a paid Unity game stands on.

---

## Quick decision guide

| You want to add… | Use | Editor? |
|---|---|---|
| A behaviour/rule change | plain Harmony patch | no |
| A new structure/skill (logic + identity) | `Ruinarch.ModContent` (Part 1) | no |
| A new icon / portrait / UI sprite | Rung 2 (loose PNG) | no |
| A reskin of an existing structure | Rung 2 or Path C | no (or tiny) |
| A new sound | loose `.wav`/`.ogg` + `AudioSource` | no |
| A genuinely new structure *shape/footprint* | Rung 3 + Path A | **yes** (2020.3.20f1) |
