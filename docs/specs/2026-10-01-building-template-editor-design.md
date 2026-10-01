# Building templates and the in-game template editor

Status: design approved 2026-10-01 (brainstormed with the owner), not built.

## Goal

Players and mods can see every building, change how buildings look and add new looks,
without the Unity editor and without touching the game's files:

- an **Editor** button in the main menu opens a full tile editor;
- what it makes are **templates**, saved in **template pack** mods;
- the game uses templates as extra **variants** of the game's own buildings and of
  buildings registered by code mods (Ruinarch+'s Town Hall, Library, Mass Grave), and code
  mods can give a building they register a template as its look;
- a **Test** button places the template in a small generated world.

Art: the game's own tiles and sprites, plus PNGs shipped in the pack.

## Scope

In: village buildings (dwellings, taverns, farms, workshops, ..., and mod-registered
village buildings) and special buildings placed at world generation (ruins, caves, lairs).

Out: demonic (player) buildings: their objects carry the building's HP and they are not
drawn with the game's tiles. Out: new building *kinds* from data alone (see "Packs add
looks" below). Out: weights between variants, or turning off the game's own variants: a
variant is simply added alongside the game's.

## Facts this rests on (cited against RuinarchRE)

- A building is a prefab with a `LocationStructureObject` (`LocationStructureObject.cs`):
  floor, detail and block-wall tilemaps (`_groundTileMap`, `_detailTileMap`,
  `_blockWallsTilemap`), preplaced furniture (`StructureTemplateObjectData` children,
  `RegisterPreplacedObjects`), thin walls (`ThinWallGameObject` children gathered at load,
  line 174, drawn in the building's wall material), entrances (`StructureConnector[]
  _connectors`, `ProcessConnectors`), footprint (`_size`, `_center`,
  `_predeterminedOccupiedCoordinates`, `_borderCoordinates`), plus about 30 other
  serialized settings (rooms, click collider, ward light spots, ...).
- Block-wall tiles become `BLOCK_WALL` objects when placed (lines 700-717).
- The game picks a prefab for a kind, culture and material from `StructureData`
  ScriptableObjects (`StructureData.GetStructurePrefabs`), reached through
  `InnerMapManager.GetStructurePrefabsForStructure`; ModContent already postfixes
  `GetStructurePrefabs` for its virtual structure types (`ContentPatches.cs`).
- Prefabs are instantiated from `ObjectPoolManager` pools by name;
  `ObjectPoolManager.CreateNewPool(prefab, name)` adds a pool at runtime.
- A save records each built village, special and natural building's prefab name
  (`SaveDataManMadeStructure.structureTemplateName`, likewise for natural structures with
  a structure object) and rebuilds it with
  `ObjectPoolManager.InstantiateObjectFromPool(name, ...)` (`RegionInnerMapGeneration.cs:61-111`).
- The main menu is `MainMenuUI` (`Start`, `ShowMenuButtons`); the loader's mod manager
  already adds UI there.
- Open: whether building prefabs, tile assets and `StructureData` are in memory at the
  main menu. A throwaway probe answers it (plan step 1); if they are not, the editor opens
  after a short background load of those assets.

## Packs add looks; code adds kinds

A template pack is data only:

```
Mods/MyBuildings/
  mod.json            id, name, version (no DLL)
  templates/*.json    one file per template
  art/*.png           the pack's own tiles and sprites
```

Data alone can add looks (variants) to building kinds that already exist, the game's or a
code mod's. A new kind of building (its own name, its own rules for when villagers build
it) needs code: a code mod registers it with `ModContent.RegisterStructure` as today and
names a template as its look (`StructureRegistration.Template = "pack/id"`). A code mod
can ship templates in its own folder the same way.

## Template file

- `id`: stable, scoped to the pack (`pack-id/template-id`); `name`.
- `kind`: a game `STRUCTURE_TYPE` name or a ModContent structure id.
- `cultures`: the faction types it applies to; `material`: the resource (wood, stone, ...).
- `behavesLike`: the game prefab whose hidden settings are copied; defaults to that kind's
  prefab for the same culture and material. Chosen when saving; the editor offers a
  default per kind.
- `size`, `center`.
- `layers`: `floor`, `detail`, `blockWalls`: per cell a tile reference, `game:<tile name>`
  or `art:<file>`.
- `thinWalls`: cell plus edge (north, east, south, west).
- `objects`: `TILE_OBJECT_TYPE`, cell, optional `art:<file>` sprite, rotation.
- `entrances`: cell plus direction.
- The footprint is computed from the painted cells, never entered.

## Runtime builder (Ruinarch.ModContent)

1. **Load** at startup: read `templates/*.json` from every mod folder; validate (known
   kind, culture, material, tiles, cells inside the size). A bad template is skipped with a
   `mods.log` line naming file and problem; the game keeps its own buildings.
2. **Build** each template once the game's prefabs are available and **before any save
   loads** (saves rebuild buildings by prefab name): instantiate the `behavesLike` prefab
   hidden; clear and repaint its tilemaps from a palette of `TileBase` by name (gathered
   from every loaded building) and runtime tiles made from pack PNGs at the game's
   pixels-per-unit; rebuild furniture, entrances and footprint fields; make thin walls by
   copying a `ThinWallGameObject` from the base prefab (else from any prefab of the same
   material).
3. **Register**: `ObjectPoolManager.CreateNewPool(copy, poolName)` with
   `poolName = "<base prefab name>@<template id>"`, and add the copy to the list
   `GetStructurePrefabs` returns for its kind, culture and material.
4. **Missing pack**: a prefix on `ObjectPoolManager.InstantiateObjectFromPool` maps an
   unknown `...@...` name to the base prefab name before the pool lookup, so a save made
   with a pack still loads without it (the building keeps its place, with the base look).
5. **Code mods**: `StructureRegistration.Template` makes the registered type use that
   template's prefab; without it, `PrefabSource` works as today.

## Editor (in the loader, next to the mod manager)

- **Entry**: an Editor button cloned from a main-menu button.
- **Start screen**: pick a pack (any mod folder with templates) or create one (name, the
  editor writes the folder and `mod.json`); open a template, or start a new one **from an
  existing building** (browse the game's buildings by kind, culture, material, and other
  packs' templates) or **blank** (pick a size).
- **Canvas**: the template drawn with real tilemaps and sprites, grid overlay, pan and zoom.
- **Tools**: paint, erase, fill, rectangle, picker; undo and redo; layer switch (floor,
  detail, walls, thin walls, furniture, entrances) and per-layer visibility.
- **Palette**: game tiles grouped by source building and culture, with search; the pack's
  PNGs; furniture types with icons.
- **Properties**: name, kind, cultures, material, behaves like, size.
- **Live checks**: no entrance; floor cells unreachable from an entrance; furniture off
  the floor; a wall cell without a wall tile; unknown tile. Saving works with warnings.
- **Save**, **Test**, **Back**.
- UI built in code with Unity UI and the game's font (as the mod manager is).

## Test in a world

Test saves, then starts a new game with the smallest world settings through the game's own
new-game path. When the world is up the template is placed: a village building built at once
in the nearest village of a matching culture
(`LandmarkManager.PlaceIndividualBuiltStructureForSettlement`), a special building in the
wilderness; the camera centres on it. An overlay shows checks (a villager can walk from the
village centre to each entrance and every floor cell; each piece of furniture is reachable)
and buttons: **Place again** (elsewhere) and **Back to editor** (to the main menu, reopening
the editor on this template).

## Export

"From an existing building" reads a prefab into the template format: every tilemap cell by
tile name, thin walls, furniture, entrances, footprint. The same pass builds the palette.

## Verification

A new RuinarchDebug harness suite, plus screenshots:

- **Round trip**: export a game building, build it from the template, compare every cell,
  thin wall, object and entrance with the original: identical.
- **Variant**: a pack adds a Tavern variant; the game's list for that culture includes it;
  villagers place and build it as a blueprint, walk in, use the furniture.
- **Pack PNG**: a PNG tile shows in the built building (screenshot).
- **Saves**: a template building survives save and load; with the pack removed, the save
  loads and the building has the base look.
- **Code mods**: a registered structure with `Template` uses it (Ruinarch+ Town Hall).
- **Editor**: opens from the menu, paints, saves, reloads (screenshots).

## Build order

Each stage is usable on its own:

1. Probe the main menu; format, export and builder, proven by the round trip.
2. Packs and variants in game (load, register, saves, missing-pack fallback).
3. The editor.
4. Test in a world.
5. Ruinarch+ buildings get their own templates (Town Hall, Library, Mass Grave).
