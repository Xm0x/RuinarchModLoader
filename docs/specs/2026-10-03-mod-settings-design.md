# Mod settings: a Mods tab in the game's Settings window

Status: design approved by the owner in conversation (2026-10-03); not implemented.

## Goal

Players change a mod's options inside the game instead of editing a JSON file. The game's
Settings window (Gameplay, Audio, Graphics) gets a fourth tab, **Mods**: a list of the
installed mods that have settings on the left, and the selected mod's settings on the right,
drawn with the game's own checkboxes, sliders and dropdowns.

Mod authors get this by marking the fields of their config class. The loader draws the
controls, saves the values and tells the mod when something changes.

## What exists today

- The Settings window belongs to the game's `SettingsManager` (`Settings/SettingsManager.cs`),
  one instance kept for the whole session (`DontDestroyOnLoad`). The main menu
  (`MainMenuUI.OnClickSettings`) and the in-game pause menu (`OptionsMenu.OpenSettings`) open
  the same window. It is a prefab: each tab is a panel in its hierarchy, and its controls are
  Unity `Toggle`, `Slider` and `TMP_Dropdown` objects with localized labels.
- The loader (`Ruinarch.Modding`) has no settings support. A mod gets a `ModContext` in
  `OnLoad` (its manifest, its folder, a logger).
- The loader accepts only manifests with `"loaderApi": 1` and rejects unknown manifest fields
  (`PackageInspector.cs`).
- Ruinarch+ keeps about 105 options in `RuinarchPlusConfig` (`RuinarchPlus/Config.cs`), loaded
  once at startup from `config.json` in its own folder. The Performance Mod has no options.

## For mod authors

A mod marks its config class and registers it in `OnLoad`:

```csharp
using Ruinarch.Modding;
using UnityEngine;   // for [Range]

[ModSettings("Ruinarch+")]
public class RuinarchPlusConfig
{
    [Section("Corpses")]
    [Setting("Rotting corpses", "Corpses rot and spread disease.")]
    public bool rottingCorpses = true;

    [Setting("Hours until a corpse rots"), Range(6, 72)]
    public int rotHours = 24;

    [Setting("Largest village"), Range(10, 200), RequiresRestart]
    public int maxVillage = 60;

    public int[] internalList;   // no [Setting]: saved, but not shown
}

public void OnLoad(ModContext context)
{
    RuinarchPlusConfig config = context.Settings.Register<RuinarchPlusConfig>();
    context.Settings.Changed += field => { /* optional: react to a change */ };
}
```

### Attributes (namespace `Ruinarch.Modding`)

| Attribute | On | Meaning |
|---|---|---|
| `[ModSettings("Title")]` | the class | Title shown in the tab's list. Optional; the manifest `name` is used without it. |
| `[Section("Title")]` | a field | Starts a section with this header; it covers this field and the ones after it, up to the next `[Section]`. Fields before the first section have no header. |
| `[Setting("Label", "Description")]` | a field | Shows the field in the tab. The description is optional. |
| `[Range(min, max)]` | a field | Unity's own `UnityEngine.RangeAttribute`: the limits of a number. |
| `[RequiresRestart]` | a field | The new value is saved at once but only takes effect when the game next starts. |

### Controls

| Field type | Control | Needs |
|---|---|---|
| `bool` | checkbox | |
| `int` | slider in whole steps, value shown beside it | `[Range]` |
| `float` | slider, value shown with two decimals | `[Range]` |
| any `enum` | dropdown of the enum's names | |

Only public instance fields are read. A `[Setting]` on any other type, or a number without
`[Range]`, is a declaration mistake: it is written once to `mods.log` when the class is
registered, and that field is not shown (it is still saved).

### `context.Settings`

- `T Register<T>() where T : class, new()`: creates `T`, fills it from the saved file and
  returns it. The loader keeps that object and writes changes straight into it, so code that
  reads `config.rotHours` sees a change at once. One class per mod: a second call writes an
  error to `mods.log` and returns the object already registered.
- `event Action<string> Changed`: raised on the main thread after a change is written to the
  object and saved; the argument is the field name. Needed only by mods that must act on a
  change (the Performance Mod applying a new frame cap). Raised for restart-only fields too,
  so a mod may record them; the mod decides what to do.

### Live and restart-only settings

A field without `[RequiresRestart]` is live: the object changes at once and `Changed` is
raised. A `[RequiresRestart]` field is saved at once as well, and the tab shows
**Restart to apply** beside it while its saved value differs from the value the game started
with. The author marks a field restart-only when the mod reads it only at startup.

## Saving and loading

Each mod's settings live in `Mods/settings/<mod id>.json` (`<mod id>` is the manifest `id`),
outside every mod folder, so they survive Workshop updates, reinstalling a mod and switching
between a local copy and the Workshop copy.

`Register<T>()`:

1. Creates `T`: the coded field values are the defaults.
2. If the file exists, copies each property whose name matches a public instance field of `T`
   (Newtonsoft.Json, per field). Fields missing from the file keep their defaults; properties
   that match no field are ignored. A mod update can add, rename or drop settings without
   breaking a player's file.
3. Clamps numbers into their `[Range]`; an enum name the enum no longer has falls back to the
   default. Each correction is one line in `mods.log`.
4. Writes the file back, so it always lists every current field with its value.

Every change made in the tab is written to the file immediately, as the game does with its
own options.

### Errors

- A file that cannot be read as JSON is renamed to `<mod id>.json.bad` (replacing an older
  `.bad`), the mod starts on its defaults, and `mods.log` says what happened. A broken file is
  never silently overwritten.
- A field whose saved value cannot be converted keeps its default and is logged; the rest of
  the file still loads.
- An exception in a mod's `Changed` handler is caught and logged with the mod's name; the
  value stays saved and the tab keeps working.
- Nothing in the settings system throws into the game.

## The Mods tab

```
+------------------------- Settings -------------------------+
| [Gameplay] [Audio] [Graphics] [Mods]                       |
| +-----------------+  Performance Mod                       |
| | Performance Mod |  ----- Frame rate -----                |
| | Ruinarch+       |  [x] Match screen refresh rate         |
| |                 |  Frame rate cap  ---o------- 165       |
| +-----------------+  ----- ... -----                       |
|  (description of the hovered setting)  [Reset to defaults] |
+------------------------------------------------------------+
```

- **Tab button**: a fourth button in the tab row, cloned from Graphics, switching panels the
  way the other three do. The row's buttons share its width.
- **Mod list** (left): the mods that registered settings, sorted by title; the selected one is
  highlighted. The selection is remembered while the game runs. Without any such mod the tab
  says "No installed mod has settings." Mods that are disabled or failed to load are not
  listed (they never registered); their saved files are left alone.
- **Settings** (right): the mod's title, then its sections with the game's ornamented section
  header, then one row per setting, cloned from the game's own controls with the clone's
  localization and callbacks removed. The right side scrolls when the list is long.
- **Description line**: at the bottom; shows the description of the setting under the mouse.
  It works the same in the main menu and in game.
- **Restart note**: as described under "Live and restart-only settings".
- **Reset to defaults**: puts the selected mod's settings back to their coded defaults. The
  first click changes the button to "Click again to reset"; a second click within a few
  seconds resets, saves and raises `Changed` for every field that changed.
- The tab is available wherever the game's Settings window opens: main menu and in game.

## Loader API and versions

- The loader becomes **0.8.0** and accepts `"loaderApi": 1` and `"loaderApi": 2`.
- API 2 is API 1 plus `context.Settings`. Mods that use settings declare `"loaderApi": 2`;
  mods that do not stay on 1 and keep loading on every loader.
- An older loader refuses an API 2 mod with its existing message ("Unsupported loaderApi; this
  loader supports API 1."), which the Mods window shows. No new manifest field is added.

## First users

Delivered with the feature:

- **Performance Mod 0.3.0** (`loaderApi` 2), section "Frame rate":
  - "Match screen refresh rate" (`bool`, default on, live): today's behaviour, the cap
    follows the screen when it is faster than the game's 144.
  - "Frame rate cap" (`int`, `[Range(30, 360)]`, default 144, live): used while matching is off.
    Vertical sync, when switched on in the game's Graphics tab, still takes precedence.
  - Section "Fixes": one checkbox per fix. "Minimap redraws only when it changes" is live;
    "Tile objects share one signal listener" and "Finished jobs drop their crime listener" are
    restart-only (switching them mid-game would mean moving tens of thousands of
    subscriptions).
- **Ruinarch+** (next version, `loaderApi` 2): every option in `RuinarchPlusConfig` gets
  `[Setting]` with a label and description, `[Section]` by feature (corpses, plague, knowledge,
  migration, towns, famine, unrest, hunters and traders, night watch, blight, and so on) and
  `[Range]` where the code has limits. Each field is checked against how its feature reads it:
  read once at startup means `[RequiresRestart]`; read through `RuinarchPlusConfig.Current`
  each time means live. Fields that are not player options stay unmarked.
  On the first start of that version, an existing `Mods/RuinarchPlus/config.json` is moved to
  `Mods/settings/ruinarch.plus.json` with its values, and the old file is renamed
  `config.json.migrated`, so nobody loses their settings.

## Testing

A `ModSettingsSuite` in RuinarchDebug (the harness), plus screenshots of the tab:

1. Changing the Performance Mod's frame cap through the same code the tab uses changes
   `Application.targetFrameRate` at once and updates `Mods/settings/ruinarch.performance.json`.
2. A restart-only change is saved, raises `Changed`, and the running game is unaffected.
3. With RuinarchDebug's own small test settings class (it is a development-only mod): an
   unreadable file becomes `.json.bad` and the class starts on defaults; out-of-range numbers
   are clamped; unknown properties are ignored; missing ones get defaults.
4. A Ruinarch+ `config.json` with non-default values arrives in
   `Mods/settings/ruinarch.plus.json` with those values, and the old file is renamed.
5. The Settings window has a Mods tab, listing exactly the mods that registered settings;
   selecting the Performance Mod shows its rows; clicking a checkbox flips the config field
   and the file; it works from the main menu and in game.
6. Full regressions before each release, as usual.

## Delivery order

1. Performance Mod 0.2.0 (no settings; already built and being regression-tested).
2. Loader 0.8.0: settings API, loader API 2 accepted, the Mods tab, documentation (a
   "Settings" section in `docs/WRITING_MODS.md` and the README).
3. Performance Mod 0.3.0 with its settings.
4. Ruinarch+ with its settings and the config migration.

## Not in this design

- A Settings button per mod in the main-menu Mods manager (can come later).
- Text, key-binding, list or colour settings.
- Translating mod labels.
- Settings for data-only template packs (they have no code to read values).
