# Packages: installing, sharing and the Steam Workshop

A **package** is one folder that RuinarchModLoader can load: a code mod (a DLL), a
building template pack (data only), or a code mod that also ships templates. This page
explains how players install packages, how authors describe and share them, and what
the loader checks before it runs anything.

## The loader is installed separately

Packages never install or update RuinarchModLoader. Every player installs the loader once
with its installer (see the [README](../README.md#install-players)). Without it, the game
ignores packages: the stock game does not load DLL mods, and its own Workshop support only
reads villager-class XML files.

## Where packages come from

- **Local folders.** Download a package (GitHub, Nexus, a friend) and put its folder in
  `Ruinarch/Mods/`, next to `Ruinarch.exe`: `Ruinarch/Mods/<package>/mod.json`.
- **Steam Workshop.** Subscribe to an item on the Ruinarch Workshop page. Steam downloads
  it to its own Workshop folder; you do not copy anything. The loader reads subscribed,
  fully downloaded items each time the game starts.

Both kinds go through exactly the same checks and appear in one list in the main-menu
**Mods** window.

## The manifest: `mod.json`

Every package has a `mod.json` in its root folder. This is loader API 1:

```json
{
  "id": "author.mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "you",
  "description": "What it does",
  "loader": "RuinarchModLoader",
  "loaderApi": 1,
  "type": "code",
  "entryAssembly": "MyMod.dll",
  "entryType": "MyMod.MyMod",
  "dependencies": ["lib/SomeLibrary.dll"]
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Unique id: lowercase letters, digits, dots, underscores or hyphens. Saves and settings refer to it, so never change it. |
| `name` | yes | Shown in the Mods window and used as the Workshop title. |
| `version` | yes | `major.minor.patch`, for example `1.2.0`. |
| `author`, `description` | no | Shown in the Mods window; the description is also the Workshop description. |
| `loader` | yes | Exactly `RuinarchModLoader`. |
| `loaderApi` | yes | Exactly `1`, the interface version this loader supports. |
| `type` | yes | `code` (has a DLL) or `templates` (data only). |
| `entryAssembly` | code only | Path of the DLL holding your mod class, relative to the folder. |
| `entryType` | code only | Full name of your public `IRuinarchMod` class (namespace included). |
| `dependencies` | no | Other DLLs your mod ships, as relative paths. |

Unknown fields are an error, so a typo is reported instead of silently ignored.

A **template pack** (`"type": "templates"`) has a `templates/` folder with one JSON file
per building look, an optional `art/` folder of PNG files, and no DLLs at all. See
[TEMPLATES.md](TEMPLATES.md).

## What the loader checks

The loader reads `mod.json` and the DLL files' metadata (with Mono.Cecil) **without
running or loading them**. A package is accepted only when:

- `mod.json` is valid JSON with the fields above, `loader` is `RuinarchModLoader` and
  `loaderApi` is 1;
- every path stays inside the package folder, and the folder contains no symbolic links;
- a code package's entry type exists in its entry DLL, is public, not abstract, has a
  public constructor without parameters, and implements `Ruinarch.Modding.IRuinarchMod`;
- every DLL in the folder is the entry DLL or listed in `dependencies`, and none of them
  is (or pretends to be) a game, Unity, .NET or loader assembly;
- a template pack contains no DLLs, every template uses `formatVersion` 1 and an id that
  starts with the package id and a slash, and every `art:` picture exists in `art/`.

A package that fails stays **visible** in the Mods window with an exclamation mark,
**Not compatible with RuinarchModLoader**, and the exact reason. It has no enable switch,
none of its code is loaded, and none of its templates are registered. This includes
older loader mods without `loader`/`loaderApi`/`type`/`entryAssembly`/`entryType` (add
those fields to update them), vanilla Workshop items that only contain XML class files,
and stray DLLs placed directly in `Mods/`.

Compatibility is **not** a security check. An accepted code mod runs with the same
rights as the game, like any mod. Install code only from authors you trust.

## Duplicates, disabling and restarts

- Two packages with the same `id`: a local folder wins over a Workshop item; between two
  local folders (or two Workshop items) the first in name order wins. The other copy is
  listed as a duplicate and not loaded.
- The switch in the Mods window disables or enables a package. Disabled packages are
  never loaded and their templates are not used. A disabled local package still keeps
  its id, so a Workshop copy of it does not take over.
- Packages load once per game start. Switching packages on or off, subscribing,
  unsubscribing, and Workshop downloads or updates take effect the next time you start the
  game. An item you subscribe to while the game runs appears in the Mods window as soon as
  Steam has downloaded it, marked to load at the next start.

## Browsing the Workshop

**Mods -> Browse Workshop** opens the Ruinarch Workshop page (in the Steam overlay when it
is available, otherwise in the Steam client). Subscribe there; once Steam has downloaded
the item it shows up in the Mods window and loads the next time you start the game.

In the building editor, templates from Workshop packages appear as sources you can copy
from (**New from existing**); a Workshop package itself is read-only. The copy goes into
one of your local packs together with the PNG files it uses.

## Uploading your package

1. Put the package folder in `Ruinarch/Mods/` and start the game through Steam. Its row
   in the Mods window must not show the exclamation mark.
2. Optional: add `preview.png` to the package folder; it becomes the item's picture.
3. Open **Mods -> Upload to Workshop**, choose the package, and:
   - leave **Existing Workshop item id** empty to create a new item, or enter the number
     of an item you own to update it;
   - pick the visibility. New items are **Private** unless you choose otherwise; for an
     update, **Keep current visibility** leaves it unchanged;
   - optionally write a change note (the default is `Version <version>`).
4. Click **Upload**. The panel shows Steam's progress and the final result, and the new
   item's page opens in Steam. Steam may ask you to accept the Workshop legal agreement
   on that page first; until then only you can see the item.

The package is checked again just before uploading. The whole package folder is uploaded,
with its `mod.json` title and description and the tag `RuinarchModLoader`. The loader is
never uploaded: tell your players to install RuinarchModLoader first.

## Logs

`Mods/mods.log` records what was loaded. The game's `Player.log` also lists every
package that was skipped and why (`[ModLoader] Not compatible with RuinarchModLoader: ...`).
