# Building templates

A building template is the look of one building: its floor, details, walls, furniture,
entrances and footprint. A **template pack** is a mod folder holding templates; the game
then uses each template as one more look for its kind of building, next to its own. Your
pack needs no code and no Unity editor.

Templates change layouts, not gameplay rules. A new building kind still needs a code mod.
The main-menu **Editor** creates and edits templates without external tools.

## Availability

Building templates and the editor need RuinarchModLoader 0.6.0 or newer.

## A pack

    Mods/MyBuildings/
      mod.json            see below
      templates/*.json    one template per file
      art/*.png           your own tiles and sprites (optional)

A template pack's `mod.json`:

```json
{ "id": "mybuildings", "name": "My Buildings", "version": "1.0.0",
  "loader": "RuinarchModLoader", "loaderApi": 1, "type": "templates" }
```

A template pack contains no DLLs. The editor's **Create pack** writes this for you.

Every template id starts with the pack id and a slash: `mybuildings/stone-tavern`. The id
is stored in save files, so do not change it once players use the pack. A save made with a
pack still loads without it: those buildings take the look they were based on.

Switch a pack off in the in-game Mods window like any mod. Packs can be shared on the
Steam Workshop; subscribed packs are used like local ones, and the editor lets you copy
their templates into your own pack (it never edits them in place). See
[PACKAGES.md](PACKAGES.md).

## Visual editor

Open **Editor** from the main menu (below **Mods**). On the first visit it briefly loads the game's
building assets, without generating a world. Create or select a local pack, then open
a template or choose **New from existing / New blank**. Filter the source browser by
kind, culture, material or prefab name. A blank building still borrows a real building
for its hidden runtime settings.

**Delete** next to a template removes its file; **Delete pack** removes a whole template
pack folder. Both ask first and cannot be undone. Saved games that used a deleted building
show the building it was based on. The editor never deletes code mods; remove those from
the `Mods/` folder yourself.

The workspace has paint, erase, connected fill, rectangle and picker tools; the chosen
tool, layer and tile are highlighted. The palette lists every tile, wall piece and piece
of furniture the game's buildings use for the chosen layer, with a thumbnail: first the
ones in this building, then your pack's PNGs, then all other game buildings. Type in
**Search tiles** to narrow it down. A layer the base building does not have (many
buildings have no block walls, only thin walls) cannot be painted and the palette says
so. Thin-wall brushes retain their native edge, corners and decoration. **Rotate 90
degrees** rotates wall/furniture pieces. Right or middle drag pans; scroll zooms around
the pointer; **Fit** restores the overview. Each layer and the grid can be hidden
independently.

**Material** decides which villages build the look. Villages build most kinds in wood or
stone, depending on the resource they can reach, and the game keeps a list of looks for
each. A kind with a single version (a farm, a special building) uses **Any village**.

Undo/redo operate on complete strokes, fills, resizes and property edits. Shortcuts:
Ctrl+S saves, Ctrl+Z undoes, Ctrl+Y redoes (not while typing in a field). Resize keeps
the lower-left cell origin and clips tiles, furniture and walls outside the new bounds.
Entrances are where the game attaches the building to a village's paths; stock buildings
put them just outside their walls, so the entrances layer can be painted up to two cells
around the bounds and resizing keeps them. Floor edits recompute the footprint. Live
checks report a missing entrance (for kinds whose game buildings have them; caves and
mines placed by the world generator have none), floor walled off from the outside, off-floor
furniture, wrong wall tiles and unknown tiles. **Save** permits warnings,
but structurally invalid documents never overwrite the previous file. **Packs** and
**Back** prompt for unsaved changes.

**Test** saves and generates a small disposable world through the game's normal startup
flow. Village looks are placed in a matching village; special looks go into free
wilderness. The camera centers on the result. The overlay reports native walking paths
from a village center to entrances, floor cells and furniture access cells. Failures
remain visible. **Place again** uses another free footprint. **Back to editor** returns
to the same saved draft. The test world is paused and is not a saved campaign.

## Start from a game building

The easiest template is a copy of one of the game's buildings, then edited. A code mod (or
the RuinarchDebug harness) can export one:

    BuildingTemplate t = ModTemplates.Export(prefab, "mybuildings/stone-tavern", STRUCTURE_TYPE.TAVERN, FACTION_TYPE.None, RESOURCE.STONE);
    File.WriteAllText(path, ModTemplates.ToJson(t));

`ModTemplates.GameLooks()` lists every game building (kind, culture, material, prefab).

These APIs are in `Ruinarch.ModContent.Templates`. In a live world they are immediately
available. At the menu, first run `TemplateAuthoring.Prepare()` as a Unity coroutine;
it indexes stock assets without generating a world. Registration and pack loading
still require a live game scene and must not run from `OnLoad`.

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
| `thinWalls` | Thin wall pieces: `pos` `[x, y, z]`, `rot`, `sprites`, optional `layout` (`prefab name#wall index`) preserving the native edge, collider and child geometry. Export/editor writes it automatically. |
| `objects` | Furniture: `type` (a TILE_OBJECT_TYPE, e.g. `BED`), `pos`, `rot`, `sprite` (`game:` or `art:`). |
| `entrances` | Where paths meet the building: `pos`. |
| `lightSpots` | Ward light positions: `pos`. |
| `rooms` | Lists of cells `[x, y, z]`. |
| `footprint` | Only when the occupied cells differ from the floor. |
| `clickBox` | Overrides an existing native collider (`offset`, `size`); if omitted, that collider fits the footprint. Prefabs without one keep using the game's tile-based selection. |

Positions are in the building's own units: one unit is one tile.
Tilemap cells and prefab-local object positions can have different origins in stock
buildings. The editor converts between them; do not assume furniture coordinates
equal floor cell indices.

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

At startup, `mods.log` lists what loaded. Only packs the loader accepted (compatible and
switched on, local or Workshop) are counted; the Mods window shows why any other pack
was not loaded:

    [INFO] [ModContent] Template packs: 2 accepted by the loader, 5 template(s) loaded, 1 skipped.
