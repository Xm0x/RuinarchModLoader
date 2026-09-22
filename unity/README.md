# Unity AssetBundle tooling

Ruinarch is a 2D Unity game built with **Unity 2020.3.20f1**. To add genuinely new
art (a new sprite, a reskinned structure, a custom prefab), you build an
**AssetBundle** in that exact Unity version and load it at runtime next to your mod.
The stock game is never rebuilt or redistributed; you ship one small `.bundle` file
that contains only your own content.

This folder holds the editor tooling for that. For the full picture (when you need a
bundle at all versus a loose PNG, the licensing footing, and the runtime loading
code) read [`../docs/ASSETS_AND_CONTENT.md`](../docs/ASSETS_AND_CONTENT.md).

## One-time setup

1. Install **Unity 2020.3.20f1** with the **Windows Build Support (Mono)** module.
   That module is required so the editor can compile bundles for the game's runtime
   platform (`StandaloneWindows64`), which is what a bundle must target because
   Ruinarch runs as the Windows build (directly or through Proton).
2. Create a new **2D** project in that version (Unity Hub, New project, 2D template).
   Name it anything, for example `RuinarchBundles`.
3. Copy `Editor/BundleBuilder.cs` from this folder into your project's
   `Assets/Editor/` folder. A new **Ruinarch** menu appears in the menu bar.

## Building a bundle

1. Import your art into `Assets/` (drag a `.png` in).
2. Select the texture. In the Inspector set:
   - **Texture Type**: `Sprite (2D and UI)`
   - **Filter Mode**: `Point (no filter)` for crisp pixel art (match the game's look)
   - **Pixels Per Unit**: match the game's tile art so the sprite is the right scale
     (Ruinarch tiles are authored around 64 PPU; adjust to taste against a reference).
3. At the bottom of the Inspector, in the **AssetBundle** dropdown, assign the asset
   to a bundle, for example `ruinarchplus`. Every asset with the same bundle name is
   packed into one `.bundle`.
4. Menu: **Ruinarch > Build AssetBundles (Windows64)**.
5. The output lands in `<project>/AssetBundles/StandaloneWindows64/`. The file named
   after your bundle (for example `ruinarchplus`) is the one you ship.

## Shipping and loading at runtime

Put the `.bundle` next to your mod DLL (anywhere under the game's `Mods/` tree) and
load it from your mod code:

```csharp
using UnityEngine;

// bundlePath is e.g. Path.Combine(context.ModDirectory, "ruinarchplus");
AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
Sprite mound = bundle.LoadAsset<Sprite>("MassGraveMound");   // asset name in the bundle

// Swap it onto a spawned structure's renderers:
foreach (var sr in structureObject.GetComponentsInChildren<SpriteRenderer>())
    sr.sprite = mound;
```

Load a bundle once and cache it; do not call `LoadFromFile` on the same path twice
without `bundle.Unload(...)` in between.

## Gotchas

- **Version must be exactly 2020.3.20f1.** A bundle built in a different minor version
  can fail to load or deserialize wrong.
- **Use the built-in `Sprites/Default` shader** (the default for imported sprites).
  A URP/HDRP shader renders bright pink under the game's built-in pipeline because the
  shader is missing at runtime.
- **Match sorting layers and pixels-per-unit** to the game, or your art draws behind or
  in front of the wrong things, or at the wrong size.
- A plain sprite swap needs no editor beyond this. A brand new *shape* or footprint
  (a new tilemap layout, animation, or particle system) is the only case that needs
  full prefab authoring; the runtime side is the same `LoadFromFile` call.
