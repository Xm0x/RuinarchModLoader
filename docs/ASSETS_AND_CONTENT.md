# Assets & new content

This guide explains how to give a Ruinarch mod **new content**: new structures,
new skills, new sprites, and new sounds. It assumes no prior knowledge beyond the
basics in [`WRITING_MODS.md`](WRITING_MODS.md) (how a mod is loaded and how to
patch the game with Harmony). Read that first if you haven't.

Everything here works **without forking the game** and **without shipping or
replacing the game itself**.

> **The golden rule.** Every file a mod adds is a separate, additive file inside
> the game's `Mods/` folder. The game's own `Assembly-CSharp.dll` and all of its
> data files stay exactly as installed. Because nothing in the game is
> overwritten, your mod coexists with other mods, and you never rebuild,
> repackage, or redistribute any part of the game. This is the same approach that
> mod loaders for other paid Unity games use.

---

## Two separate problems

"Adding new content" is really two different jobs, and they have different
solutions. It helps to keep them apart in your head:

1. **New logic and identity.** A new structure type, a new build skill, or any
   new "thing" the game creates by name. Harmony can change methods that already
   exist, but it cannot invent a brand new type of structure on its own. This is
   solved by the **content framework** described in Part 1.
2. **New art and audio.** A sprite, an icon, a portrait, the look of a structure,
   or a sound effect. This is solved by the **asset ladder** described in Part 2.

A single feature often needs both: you register a new structure (Part 1) and then
decide what it looks like (Part 2). Those are independent choices, which is why
this guide keeps them in separate sections.

---

## Background: how the game creates content

To understand why Part 1 exists, you need one fact about how Ruinarch works
internally.

Many kinds of content in Ruinarch are identified by an **enum value** (a named
number in the game's code, for example `STRUCTURE_TYPE.CRYPT`). When the game
needs to create one, it does not call your code directly. Instead it takes the
enum's name, looks up a C# class with that name using reflection, and creates an
instance of it. In simplified form:

```csharp
// the game turns an enum value into a class name, finds that class, and builds it
Type.GetType("<namespace>." + enumValue.Name + ", Assembly-CSharp");
Activator.CreateInstance(...);
```

Two consequences follow, and they define what modding can and cannot do:

- To add a **new** structure, you would need a **new enum value** and a **new
  class**. Harmony patches existing methods; it cannot add a new value to an enum
  or a new class to the game's compiled assembly.
- Editing the game's assembly to add them was tried and rejected: it permanently
  changes the shipped game file and defeats the whole "stock game" approach.

Part 1 is the workaround that adds new enum-backed content **without** editing the
game's assembly.

---

## Part 1: new logic, using the content framework

The loader ships a small library, `Ruinarch.ModContent`, that lets a mod register
genuinely new structures and skills. You call it from your mod's `OnLoad`; it
installs the necessary Harmony patches automatically the first time you use it.

### How it works, in plain terms

1. **It invents a fake enum value for you.** When you register a structure, the
   framework assigns it a number in a reserved range (100000 and up) that no
   real game enum uses. That number is derived from the text id you provide,
   using a hashing function, so the **same id always produces the same number**,
   on every machine and in every session. That stability is what lets a saved
   game reload your structure later.
2. **It intercepts the game's "create by name" step.** Using a Harmony patch,
   when the game tries to create content for one of these reserved numbers, the
   framework hands back the object your mod supplied instead of failing. The
   game's own assembly is never modified.

A useful side effect: because these fake enum values are outside the real enum,
any game code that lists "every value of the enum" (such as world generation)
simply never sees them. That means your custom content cannot interfere with
those systems.

### Registering a structure

You describe your structure by filling in a small object and passing it to
`RegisterStructure`. Every field is explained in the comments:

```csharp
var registration = ModContent.RegisterStructure(new StructureRegistration {
    // A stable, unique text id for your structure. This is what produces the
    // deterministic fake enum value, so never change it once players have saves.
    Id = "yourname.yourstructure",

    // How to build a fresh instance when the player constructs it.
    Factory = (type, region) => new YourStructure(type, region),

    // How to rebuild it when a saved game is loaded.
    LoadFactory = (type, region, save) =>
        new YourStructure(region, (SaveDataDemonicStructure)save),

    // Which existing structure's visual to borrow (see Part 2, Rung 1).
    // Reusing an existing look means you need no art at all to start.
    PrefabSource = STRUCTURE_TYPE.CRYPT,

    // The build skill that adds your structure to the build menu.
    Skill = new YourStructureData(),

    // The existing skill your structure appears alongside in the build menu.
    // Here, it shows up wherever the Crypt does.
    UnlockWith = PLAYER_SKILL_TYPE.CRYPT,
});
```

`YourStructure` and `YourStructureData` are classes you write in your mod. You do
not need to edit the game to create them; they live entirely in your mod's DLL.

### When you need the framework, and when you don't

- **Use the framework** when you are adding content that the game creates by
  name: a new structure type or a new build skill.
- **You do not need it** for changing behaviour that already exists (making an
  existing action cheaper, altering a rule, and so on). Those are ordinary
  Harmony patches, covered in `WRITING_MODS.md`.
- **One limitation to remember:** because your content's enum value is invisible
  to code that lists all enum values, always trigger your content through the
  specific registration above, never by expecting it to appear in a "for every
  enum value" loop.

---

## Part 2: new art and audio, using the asset ladder

The single most important fact for art is that **Ruinarch is a 2D sprite game
built in Unity version 2020.3.20f1**. Because it is 2D, most "new art" is simply
providing a new sprite (a flat image), and providing a sprite does **not** require
the Unity editor at all.

Think of the options as a ladder. Start at the lowest rung that solves your
problem, because each rung up costs more effort.

### Rung 1: reuse an existing look (no files, no editor)

When you register a structure (Part 1), the `PrefabSource` field lets you point at
an existing structure type and borrow its appearance. For example, setting it to
`STRUCTURE_TYPE.CRYPT` makes your structure look like a Crypt. This costs nothing
and is the right choice when an existing look is close enough to what you want.

### Rung 2: load a loose PNG at runtime (no editor)

You can place ordinary `.png` image files next to your mod's DLL and turn them
into game sprites while the game runs. The Unity editor is never involved. This
one technique covers icons, portraits, tile-object images, reskinned structures,
and user-interface graphics, which is the large majority of art a mod needs.

The content framework ships a helper for this, `ModArt.LoadSprite`. It decodes a PNG
(or JPG) into a point-filtered sprite, caches it, and returns `null` instead of
throwing if the file is missing or not an image:

```csharp
using Ruinarch.ModContent;

// pixelsPerUnit controls on-screen size: at 64, a 64px image spans one map tile.
Sprite sprite = ModArt.LoadSprite(absolutePathToPng, pixelsPerUnit: 64f);
```

Put your images in an `art/` folder inside your mod's source folder.
`tools/build-mod.sh` copies `art/` (and `audio/`, `bundles/`) next to your DLL, so at
runtime they live under `context.ModDirectory`, the folder your DLL was loaded from.

**Load art from gameplay, never from `OnLoad`.** Mods load before Unity's graphics
device exists, and creating a texture at that point crashes the game outright. It
is not an exception you can catch. `LoadSprite` refuses and logs a warning if it is
called that early. Load lazily the first time something is shown, or on the first
in-game tick.

To change how something looks, assign your loaded sprite to the image components
Unity uses to draw it (called `SpriteRenderer`s). For example, once a structure
object exists in the world:

```csharp
// modDirectory is context.ModDirectory, saved from OnLoad.
string imagePath = System.IO.Path.Combine(modDirectory, "art", "yourart.png");
Sprite sprite = ModArt.LoadSprite(imagePath);

foreach (var renderer in structureObject.GetComponentsInChildren<SpriteRenderer>())
    renderer.sprite = sprite;
```

**Audio works the same way.** The game's built-in sounds use a specialised audio
system (Wwise) that is awkward to extend. You can sidestep it entirely: load a
loose `.wav` or `.ogg` file at runtime and play it through a standard Unity
`AudioSource` component that your mod creates.

### Rung 3: build a new prefab as an AssetBundle (editor required, small and additive)

You only need this rung when you want a genuinely new **shape**: a structure with
a different footprint, a custom tile layout, an animation, or a particle effect,
none of which can be expressed as a single flat sprite.

An **AssetBundle** is a single file that Unity produces containing your custom
content. You build it once in the Unity editor, ship that one file inside your mod
folder, and load it at runtime:

```csharp
string bundlePath = System.IO.Path.Combine(context.ModsRoot, "YourMod/yourmod.bundle");
AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
GameObject prefab = bundle.LoadAsset<GameObject>("YourStructurePrefab");
// You then use this prefab as your structure's visual through the framework.
```

It is still a single additive file. It never replaces the game's assembly or any
game data.

---

## What a "structure" really is (important before you try Rung 3)

A Ruinarch structure is **not** just an image. It is a Unity object (a
"GameObject") that carries a game-specific component called
`LocationStructureObject`. That component stores everything the game needs to
place and run the structure, including:

- `structureType`: which structure type this is
- ground, detail, and wall **tilemaps** plus the renderers that draw them (a
  tilemap is Unity's grid-of-tiles system, which Ruinarch uses to paint floors
  and walls)
- the **footprint**: its size, its centre point, which tile coordinates it
  occupies, and its border tiles
- wall types, connector points (where doors/entrances attach), room templates,
  and a click collider (so the player can select it)

In other words, the "asset" for a structure is a tilemap-based object with a
game component, not a lone picture. That is why there are three practical ways to
create one, from most effort to least.

### Approach A: a Unity project that references the game's files (full control)

Copy the game's `Assembly-CSharp.dll` and Unity's own module DLLs into a Unity
project set to **exactly version 2020.3.20f1**. With those references in place,
the `LocationStructureObject` component and the structure-type list exist inside
your editor, so you can build a structure prefab the same way the game's
developers did: add the component, paint the tilemaps, set the footprint
coordinates, and export it as an AssetBundle. This gives you the most freedom,
including brand new footprints. Do not include the game's `Assembly-CSharp.dll`
inside the exported bundle; the bundle links to the game's own copy when it loads.

### Approach B: build only the visuals, then attach the component in code

Build just the visual part (the images and the object hierarchy) in a **clean**
Unity project that has **no** game references, and export it as a bundle. Then, at
runtime, your mod adds the `LocationStructureObject` component in code and fills in
its values (copying them from an existing structure as a template). Your editor
project stays simple, at the cost of more setup code in your mod. This suits
simple structures with a single-image footprint.

### Approach C: copy an existing structure and change only its look (least effort)

At runtime, duplicate an existing structure object. Because you copied a real one,
it already has a correct `LocationStructureObject`, with valid tilemaps,
footprint, and connectors. Then swap only its images, using the Rung 2 loose-PNG
technique (or a bundle that contains only images). You get a new appearance on
proven, working machinery with little or no editor work.

**Rule of thumb:** if you need a new **shape**, use Approach A. If you only need a
new **look** on an existing structure, use Approach C.

---

## AssetBundle pitfalls (the ones that commonly cause problems)

- **Match the Unity version exactly: 2020.3.20f1.** A bundle built in a different
  version can fail to load or read its data incorrectly.
- **Use a compatible shader.** Inside the bundle, use Unity's built-in
  `Sprites/Default` shader (or the game's sprite shader). If you use a shader from
  a different render pipeline, your art shows up as solid bright pink, which means
  "missing shader".
- **Match sorting and scale.** Your object's sorting layer and its
  pixels-per-unit must match the game's, or it will draw in front of or behind the
  map, or appear at the wrong size.
- **Tilemaps need `Tile` assets, not just PNGs.** Painting tilemaps is genuine
  editor work, and it only matters for Approach A / new footprints.
- **Keep bundles additive.** Ship the `.bundle` inside your mod's folder and load
  it with `AssetBundle.LoadFromFile`. Never place it inside the game's data
  folders.

---

## Quick decision guide

| What you want to add | What to use | Editor needed? |
|---|---|---|
| A change to existing behaviour or a rule | An ordinary Harmony patch | No |
| A new structure or skill (new logic + identity) | The content framework (Part 1) | No |
| A new icon, portrait, or interface image | Rung 2, a loose PNG | No |
| A new look for an existing structure | Rung 2, or Approach C | No, or very little |
| A new sound | A loose `.wav` / `.ogg` played via `AudioSource` | No |
| A new structure with a brand new shape | Rung 3 with Approach A | Yes (version 2020.3.20f1) |
