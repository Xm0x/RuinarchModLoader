# Building templates

A building template is the look of one building: its floor, details, walls, furniture,
entrances and footprint. A **template pack** is a mod folder holding templates; the game
then uses each template as one more look for its kind of building, next to its own. Your
pack needs no code and no Unity editor.

Templates change layouts, not gameplay rules. A new building kind still needs a code mod.
This stage uses JSON authoring; the visual layout editor is not implemented yet.

## Availability

Loader v0.5.0 does not include building templates. Until a loader release includes this
feature, [build and install from source](../README.md#build-from-source).

## A pack

    Mods/MyBuildings/
      mod.json            {"id": "mybuildings", "name": "My Buildings", "version": "1.0.0"}
      templates/*.json    one template per file
      art/*.png           your own tiles and sprites (optional)

Every template id starts with the pack id and a slash: `mybuildings/stone-tavern`. The id
is stored in save files, so do not change it once players use the pack. A save made with a
pack still loads without it: those buildings take the look they were based on.

Switch a pack off in the in-game mod manager like any mod.

## Start from a game building

The easiest template is a copy of one of the game's buildings, then edited. A code mod (or
the RuinarchDebug harness) can export one:

    BuildingTemplate t = ModTemplates.Export(prefab, "mybuildings/stone-tavern", STRUCTURE_TYPE.TAVERN, FACTION_TYPE.None, RESOURCE.STONE);
    File.WriteAllText(path, ModTemplates.ToJson(t));

`ModTemplates.GameLooks()` lists every game building (kind, culture, material, prefab).

These APIs are in `Ruinarch.ModContent.Templates`. Call them after a world has loaded,
not from `OnLoad` or the main menu, where the game's building catalogue is unavailable.

To export examples with the development harness, build and deploy `RuinarchDebug` from
the RuinarchMods repository, then run `tools/run-autotest.sh 1200 TemplateSuite` from the
loader checkout. It writes `template-<prefab name>.json` files into
`Mods/RuinarchDebug/`. Copy a file into your pack's `templates/` folder and change its
`id` to start with your pack id before editing the layout.

## The template file

| Field | Meaning |
|---|---|
| `formatVersion` | `1`. |
| `id` | `pack-id/template-id`, stable. |
| `name` | Descriptive name for the look. Pool names and saves use `id`, not this label. |
| `kind` | The building it is a look for: a game kind (`TAVERN`, `DWELLING`, ...) or a mod's registered id (`ruinarch.plus.library`). Demonic buildings cannot have templates. |
| `cultures` | Cultures that use it, see below. |
| `material` | `WOOD`, `STONE`, `METAL`, ... as the game files the kind's looks. |
| `behavesLike` | The game building whose hidden settings (wall material, behaviour) are kept. Leave it out to use the kind's own building for each culture. |
| `size`, `center` | The building's size and centre cell, as the game uses them for placement. |
| `bounds` | `[x, y, w, h]`: the area the layers cover, in the building's own cells. |
| `palette` | One character per tile: `"a": "game:<tile name>"` or `"b": "art:file.png"`. |
| `floor`, `detail`, `walls` | Rows of palette characters, top row first, `.` for empty. The floor is the footprint. Wall tiles become solid walls. |
| `thinWalls` | Thin wall pieces: `pos` `[x, y, z]`, `rot`, `sprites`. |
| `objects` | Furniture: `type` (a TILE_OBJECT_TYPE, e.g. `BED`), `pos`, `rot`, `sprite` (`game:` or `art:`). |
| `entrances` | Where paths meet the building: `pos`. |
| `lightSpots` | Ward light positions: `pos`. |
| `rooms` | Lists of cells `[x, y, z]`. |
| `footprint` | Only when the occupied cells differ from the floor. |
| `clickBox` | Overrides an existing native collider (`offset`, `size`); if omitted, that collider fits the footprint. Prefabs without one keep using the game's tile-based selection. |

Positions are in the building's own units: one unit is one tile.

## Cultures

The game keeps one list of looks per kind, culture and material. Only some cultures have
lists of their own (the cult, the church and the Wiccans); every other culture, Human and
Elven villages included, uses the culture-neutral list, `None`. A template follows the same
rule:

- `"cultures": ["None"]` adds the look for every culture without a list of its own.
- `"cultures": ["Human_Empire"]` adds it for Human villages only.
- `"cultures": ["Demon_Cult"]` adds it to the cult's own list.

## Your own art

PNG files in `art/` become tiles and sprites at the game's 64 pixels per tile. Name them in
the palette as `art:file.png`, and in objects as `"sprite": "art:file.png"`.
PNG floors keep the ground behaviour of the borrowed building's first floor tile. The
image changes the appearance, not the terrain classification used by the game.

## Problems

A template that cannot be used is skipped, and `Mods/mods.log` says which file and why:

    [WARN] [ModContent] Template mybuildings/tavern.json: unknown tile game:Floor_Wodo; skipped.

At startup, `mods.log` lists what loaded:

    [INFO] [ModContent] Template packs: 2 found (0 switched off), 5 template(s) loaded, 1 skipped.
