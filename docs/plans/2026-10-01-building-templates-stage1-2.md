# Building templates, stages 1-2: format, export, builder, packs as variants

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Template packs (data-only mods) add new looks for the game's village and special buildings, built at runtime from a JSON template by copying and repainting a game prefab.

**Architecture:** A new `Templates` folder in the `Ruinarch.ModContent` framework: a JSON model, an exporter (prefab to template), a builder (template plus base prefab to a hidden prefab copy), a registry (object pools, variant lists, missing-pack fallback) and pack loading. The framework's existing `StructureData.GetStructurePrefabs` patch appends variants. The loader starts the framework at launch so packs load with no code mod present, and lists packs in the mod manager. Verified by a new `TemplateSuite` in the RuinarchDebug harness.

**Tech Stack:** C# (Mono, the game's Unity 2020.3), HarmonyLib, Newtonsoft.Json (ships in the game's `Managed/`), Unity Tilemaps.

**Spec:** `docs/specs/2026-10-01-building-template-editor-design.md` (stages 1 and 2 of its build order; the editor, Test-in-world and `StructureRegistration.Template` are later plans).

## Global Constraints

- Never edit `RuinarchRE` (the decompiled game is reference only); the game DLL stays stock; everything is Harmony at runtime.
- `Ruinarch.ModContent` must not reference `Ruinarch.Modding`; the loader may reach the framework only by reflection.
- Template `formatVersion` is `1`.
- A template's pool name is `<base prefab name>@<template id>`.
- Template messages go to `Mods/mods.log` as `HH:mm:ss [LEVEL] [ModContent] message` (the loader's line format) and to `Player.log`.
- Demonic (player) buildings are refused by validation.
- Pack art is only read from `<pack>/art/`; no game files are copied anywhere.
- Code style: tabs, braces on their own lines, comments that say why (match `ContentPatches.cs`).
- Commits go to both remotes: `git push origin master && git push gitlab master` (credentials come from the session; never write tokens into files).
- Paths: loader repo `~/Desktop/Apps/Projects/RuinarchModLoader` (call it `LOADER`), mods repo `~/Desktop/Apps/Projects/RuinarchMods` (`MODS`), game `~/.local/share/Steam/steamapps/common/Ruinarch` (`GAME`).

## Build, deploy, run (used by every task)

```bash
cd ~/Desktop/Apps/Projects/RuinarchModLoader
GAME="$HOME/.local/share/Steam/steamapps/common/Ruinarch"
./tools/build-framework.sh                                   # -> build/Ruinarch.ModContent.dll
./tools/build.sh                                             # loader + patcher (Tasks 4-5 change the loader)
./tools/build-mod.sh ../RuinarchMods/RuinarchPlus "$GAME/Mods"   # also copies Ruinarch.ModContent.dll into Mods/
./tools/build-mod.sh ../RuinarchMods/RuinarchDebug "$GAME/Mods"
./tools/run-autotest.sh 1200 TemplateSuite > ~/ruinarch-runs/templates.log 2>&1
grep -E "PASS|FAIL|SKIP|AUTOTEST DONE|GAME-EXC" ~/ruinarch-runs/templates.log | grep -v "framework names\|freshly\|bookmarks\|becoming aware\|takes back"
```

When a task changes the loader (Tasks 4 and 5), re-run the patcher install after `build.sh` so the game uses the new `Ruinarch.Modding.dll` (it re-patches from the pristine backup and copies the loader and framework DLLs):
`dotnet build/patcher/RuinarchModLoader.Patcher.dll --game "$GAME"`

## File map

Framework, `LOADER/src/Ruinarch.ModContent/`:
- `Templates/BuildingTemplate.cs`: the JSON model (template, point, object, thin wall, box, exception).
- `Templates/TemplateJson.cs`: Newtonsoft settings, read/write, palette key alphabet.
- `Templates/FrameworkLog.cs`: `mods.log` + `Player.log` lines, Mods root.
- `Templates/TemplateCatalogue.cs`: every game building prefab by kind, culture, material.
- `Templates/TemplatePalette.cs`: tiles and sprites by name, pack PNGs as tiles, prototypes.
- `Templates/TemplateExporter.cs`: prefab to template.
- `Templates/TemplateBuilder.cs`: template plus base prefab to a hidden prefab copy.
- `Templates/TemplateRegistry.cs`: validation, registration, pools, variant lists, pool fallback, startup.
- `Templates/TemplatePacks.cs`: pack folders, pack ids, disabled packs, loading a pack.
- `Templates/TemplatePatches.cs`: startup hook, pool fallback patch.
- `Templates/ModTemplates.cs`: the public API (`ModTemplates`, `GameLook`, `PackLoadReport`).
- Modify `ContentPatches.cs`: `Patch_GetStructurePrefabs` gains a `Finalizer` that appends variants.

Loader, `LOADER/src/Ruinarch.Modding/ModLoader.cs`: start the framework after mods load; list data-only packs as known mods.

Harness, `MODS/RuinarchDebug/`:
- Modify `AutoTest.cs`: `public partial class AutoTest`; dispatch `TemplateSuite`.
- Create `TemplateSuite.cs`: the suite (grows task by task).

Docs: `LOADER/docs/TEMPLATES.md` (new, for pack authors), links from `LOADER/docs/CONTENT_FRAMEWORK.md` and `LOADER/README.md`.

---

### Task 1: The template model and its JSON

**Files:**
- Create: `LOADER/src/Ruinarch.ModContent/Templates/BuildingTemplate.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplateJson.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`
- Modify: `MODS/RuinarchDebug/AutoTest.cs` (class declaration line `public class AutoTest : MonoBehaviour`; dispatch near `if (Runs("FireWallTest"))`)
- Create: `MODS/RuinarchDebug/TemplateSuite.cs`

**Interfaces:**
- Produces: `BuildingTemplate`, `TemplatePoint`, `TemplateObject`, `TemplateThinWall`, `TemplateBox`, `TemplateException` (namespace `Ruinarch.ModContent.Templates`); `ModTemplates.ToJson(BuildingTemplate) : string`, `ModTemplates.FromJson(string) : BuildingTemplate` (throws `TemplateException`); `TemplateJson.Keys` (palette alphabet).

- [ ] **Step 1: Write the failing test (harness)**

In `MODS/RuinarchDebug/AutoTest.cs` change `public class AutoTest : MonoBehaviour` to `public partial class AutoTest : MonoBehaviour`, and add this line directly before `if (Runs("FireWallTest")) { yield return Safe("FireWallTest", FireWallTest()); }`:

```csharp
			if (Runs("TemplateSuite")) { yield return Safe("TemplateSuite", TemplateSuite()); }
```

Create `MODS/RuinarchDebug/TemplateSuite.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using Ruinarch.ModContent.Templates;

namespace RuinarchDebug
{
	// Building templates (Ruinarch.ModContent/Templates): the JSON format, export from game
	// prefabs, the builder, variants in the game's lists, packs on disk.
	public partial class AutoTest
	{
		private IEnumerator TemplateSuite()
		{
			// 1. The JSON format: written, read back, written again: the same text. A misspelled
			// field is an error, not silently dropped.
			BuildingTemplate sample = new BuildingTemplate
			{
				id = "autotest/sample",
				name = "Sample",
				kind = "TAVERN",
				cultures = new List<string> { "Human_Empire" },
				material = "WOOD",
				size = new[] { 2, 1 },
				center = new[] { 0, 0, 0 },
				bounds = new[] { 0, 0, 2, 1 },
				palette = new Dictionary<string, string> { ["a"] = "game:Floor" },
				floor = new List<string> { "a." },
				objects = new List<TemplateObject> { new TemplateObject { type = "TABLE", pos = new[] { 0.5f, 0.5f, 0f }, rot = 90f, sprite = "game:Table" } },
				entrances = new List<TemplatePoint> { new TemplatePoint { pos = new[] { 1.5f, 0.5f, 0f } } },
				rooms = new List<List<int[]>> { new List<int[]> { new[] { 0, 0, 0 } } },
			};
			string json = ModTemplates.ToJson(sample);
			string again = ModTemplates.ToJson(ModTemplates.FromJson(json));
			Check("a template survives a trip through JSON", () =>
				(json == again && json.Contains("\"kind\": \"TAVERN\""), json == again ? $"{json.Length} chars" : "differs: " + again));
			string error = null;
			try
			{
				ModTemplates.FromJson(json.Replace("\"material\"", "\"materal\""));
			}
			catch (TemplateException e)
			{
				error = e.Message;
			}
			Check("a misspelled template field is reported, not ignored", () => (error != null && error.Contains("materal"), error ?? "accepted"));
			yield break;
		}
	}
}
```

- [ ] **Step 2: Build the harness to see it fail**

Run: `cd LOADER && ./tools/build-mod.sh ../RuinarchMods/RuinarchDebug /tmp/rd-build 2>&1 | grep -E " error |checked"`
Expected: `error CS0246: The type or namespace name 'BuildingTemplate' could not be found` (and the same for `ModTemplates`).

- [ ] **Step 3: Write the model**

`LOADER/src/Ruinarch.ModContent/Templates/BuildingTemplate.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// The look of one building, as a template pack stores it (<c>templates/*.json</c>, see
	/// docs/TEMPLATES.md). Cells are the building's own tilemap cells; positions are in the
	/// building's local units (one unit is one tile). Layers are rows of palette keys, top
	/// row first; '.' is an empty cell.
	/// </summary>
	public sealed class BuildingTemplate
	{
		public int formatVersion = 1;
		/// <summary>Stable id, <c>pack-id/template-id</c>; part of the pool name saves record.</summary>
		public string id;
		public string name;
		/// <summary>A game STRUCTURE_TYPE name (TAVERN) or a ModContent structure id.</summary>
		public string kind;
		/// <summary>FACTION_TYPE names the look is used for.</summary>
		public List<string> cultures = new List<string>();
		/// <summary>RESOURCE name (WOOD, STONE, ...).</summary>
		public string material;
		/// <summary>Game prefab the hidden settings come from; null: the kind's own for the culture.</summary>
		public string behavesLike;
		/// <summary>The prefab's _size: w, h.</summary>
		public int[] size;
		/// <summary>The prefab's _center: x, y, z (a tilemap cell).</summary>
		public int[] center;
		/// <summary>x, y, w, h of the painted cells (tilemap cells).</summary>
		public int[] bounds;
		/// <summary>One-character key to a tile: <c>game:name</c> or <c>art:file.png</c>.</summary>
		public Dictionary<string, string> palette = new Dictionary<string, string>();
		public List<string> floor = new List<string>();
		public List<string> detail = new List<string>();
		public List<string> walls = new List<string>();
		public List<TemplateThinWall> thinWalls = new List<TemplateThinWall>();
		public List<TemplateObject> objects = new List<TemplateObject>();
		public List<TemplatePoint> entrances = new List<TemplatePoint>();
		public List<TemplatePoint> lightSpots = new List<TemplatePoint>();
		/// <summary>Each room is a list of cells (x, y, z).</summary>
		public List<List<int[]>> rooms = new List<List<int[]>>();
		/// <summary>Occupied cells when they differ from the floor cells; null: the floor cells.</summary>
		public List<int[]> footprint;
		/// <summary>The click area; null: the footprint's bounding box.</summary>
		public TemplateBox clickBox;
	}

	/// <summary>A marker (entrance, ward light spot) at a local position x, y, z.</summary>
	public sealed class TemplatePoint
	{
		public float[] pos;
	}

	/// <summary>A preplaced object: TILE_OBJECT_TYPE name, local position, rotation (degrees), sprite.</summary>
	public sealed class TemplateObject
	{
		public string type;
		public float[] pos;
		public float rot;
		public string sprite;
	}

	/// <summary>A thin wall piece: local position, rotation, its sprite renderers' sprites in order.</summary>
	public sealed class TemplateThinWall
	{
		public float[] pos;
		public float rot;
		public List<string> sprites = new List<string>();
	}

	/// <summary>The click collider's offset and size, in its own local units.</summary>
	public sealed class TemplateBox
	{
		public float[] offset;
		public float[] size;
	}

	/// <summary>A template that cannot be read, validated or built; the message says why.</summary>
	public sealed class TemplateException : Exception
	{
		public TemplateException(string message) : base(message)
		{
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/TemplateJson.cs`:

```csharp
using Newtonsoft.Json;

namespace Ruinarch.ModContent.Templates
{
	internal static class TemplateJson
	{
		/// <summary>Palette keys, one character per tile ('.' is empty), in the order export assigns them.</summary>
		internal const string Keys = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+-<=>?@^_~!";

		// An unknown field is an error: a misspelled one would otherwise be dropped silently.
		private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Ignore,
			MissingMemberHandling = MissingMemberHandling.Error,
		};

		internal static string ToJson(BuildingTemplate t)
		{
			return JsonConvert.SerializeObject(t, Settings);
		}

		internal static BuildingTemplate FromJson(string json)
		{
			try
			{
				BuildingTemplate t = JsonConvert.DeserializeObject<BuildingTemplate>(json, Settings);
				if (t == null)
				{
					throw new TemplateException("the file is empty");
				}
				return t;
			}
			catch (JsonException e)
			{
				throw new TemplateException("not a valid template: " + e.Message);
			}
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`:

```csharp
namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Building templates: export the game's buildings, build and register new looks, load
	/// template packs. See docs/TEMPLATES.md.
	/// </summary>
	public static class ModTemplates
	{
		/// <summary>The template as JSON, the way a pack stores it.</summary>
		public static string ToJson(BuildingTemplate t)
		{
			return TemplateJson.ToJson(t);
		}

		/// <summary>Reads a template; throws <see cref="TemplateException"/> naming what is wrong.</summary>
		public static BuildingTemplate FromJson(string json)
		{
			return TemplateJson.FromJson(json);
		}
	}
}
```

- [ ] **Step 4: Build and run; see it pass**

Run the "Build, deploy, run" block. Expected:
```
PASS a template survives a trip through JSON :: <n> chars
PASS a misspelled template field is reported, not ignored :: not a valid template: Could not find member 'materal' ...
AUTOTEST DONE (done) pass=... fail=0
```

- [ ] **Step 5: Commit**

```bash
cd LOADER && git add src/Ruinarch.ModContent/Templates && git commit -m "ModContent: building template model and JSON" && git push origin master && git push gitlab master
cd MODS && git add RuinarchDebug/AutoTest.cs RuinarchDebug/TemplateSuite.cs && git commit -m "Harness: TemplateSuite (JSON round trip)" && git push origin master && git push gitlab master
```

---

### Task 2: Catalogue, palette and export

**Files:**
- Create: `LOADER/src/Ruinarch.ModContent/Templates/FrameworkLog.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplateCatalogue.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplatePalette.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplateExporter.cs`
- Modify: `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`
- Modify: `MODS/RuinarchDebug/TemplateSuite.cs`

**Interfaces:**
- Consumes: Task 1 model.
- Produces: `GameLook { STRUCTURE_TYPE Kind; FACTION_TYPE Culture; RESOURCE Material; GameObject Prefab; }`; `ModTemplates.GameLooks() : IReadOnlyList<GameLook>`; `ModTemplates.Export(GameObject prefab, string id, STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material) : BuildingTemplate`; `ModTemplates.ModsRoot : string`; internal `TemplatePalette.Gather()`, `TemplatePalette.Tile(string reference, string packDirectory, bool forWalls) : TileBase`, `TemplatePalette.Sprite(string reference, string packDirectory) : Sprite`, `TemplatePalette.NameOf(TileBase) : string`, `TemplatePalette.NameOf(Sprite) : string`, `TemplatePalette.ObjectPrototype/ThinWallPrototype/ConnectorPrototype : GameObject`; `TemplateExporter.Field<T>(LocationStructureObject, string) : T`, `TemplateExporter.ClickBox(LocationStructureObject) : BoxCollider2D`; `TemplateCatalogue.Looks()`, `TemplateCatalogue.Originals(STRUCTURE_TYPE, FACTION_TYPE, RESOURCE) : List<GameObject>`, `TemplateCatalogue.FindByName(string) : GameObject`; `FrameworkLog.Info/Warning(string)`, `FrameworkLog.ModsRoot`.

- [ ] **Step 1: Read the prefab anatomy (pre-flight)**

The throwaway probe in `GAME/Mods/MenuProbe/` writes `GAME/Mods/MenuProbe/anatomy.txt` at the first world load. If it is not there, run `./tools/run-autotest.sh 300 TemplateSuite` once. Then confirm, and stop to re-plan if any of these is false:

```bash
f="$GAME/Mods/MenuProbe/anatomy.txt"
grep -m1 "CATALOGUE:" "$f"                        # a structure count, not 0
grep "root components:" "$f"                      # each shows "LSO on root"
grep -c "TAVERN | Human_Empire" "$f"              # at least 1
```

Note the special kind dissected (`ANATOMY special <KIND>`); the suite uses the first special kind found at runtime, so no name is hard-coded. Then remove the probe: `rm -r "$GAME/Mods/MenuProbe" /tmp/MenuProbe`.

- [ ] **Step 2: Write the failing test**

Replace the `yield break;` at the end of `TemplateSuite()` in `MODS/RuinarchDebug/TemplateSuite.cs` with:

```csharp
			// 2. Export: every painted cell, thin wall, object and entrance of a game building
			// ends up in its template.
			List<GameLook> picks = TemplatePicks();
			if (picks.Count == 0)
			{
				Skip("exporting a game building captures every tile, wall, object and entrance", "no game building found: " + ModTemplates.GameLooks().Count + " looks");
				yield break;
			}
			foreach (GameLook look in picks)
			{
				LocationStructureObject lso = look.Prefab.GetComponent<LocationStructureObject>();
				BuildingTemplate t = Guard($"export {look.Prefab.name}", () => ModTemplates.Export(look.Prefab, "autotest/" + look.Prefab.name.ToLowerInvariant(), look.Kind, look.Culture, look.Material));
				if (t == null)
				{
					continue;
				}
				File.WriteAllText(Path.Combine(Path.GetDirectoryName(_logPath), "template-" + look.Prefab.name + ".json"), ModTemplates.ToJson(t));
				int floor = Painted(t.floor), detail = Painted(t.detail), walls = Painted(t.walls);
				int gFloor = TilesIn(lso, "_groundTileMap"), gDetail = TilesIn(lso, "_detailTileMap"), gWalls = TilesIn(lso, "_blockWallsTilemap");
				int thin = lso.GetComponentsInChildren<ThinWallGameObject>(true).Length;
				int objects = lso.GetComponentsInChildren<StructureTemplateObjectData>(true).Length;
				int doors = lso.connectors?.Length ?? 0;
				Check($"exporting {look.Prefab.name} captures every tile, wall, object and entrance", () =>
					(floor == gFloor && detail == gDetail && walls == gWalls && t.thinWalls.Count == thin && t.objects.Count == objects && t.entrances.Count == doors,
					$"{look.Kind} {look.Culture} {look.Material}: floor {floor}/{gFloor} detail {detail}/{gDetail} walls {walls}/{gWalls} thin {t.thinWalls.Count}/{thin} objects {t.objects.Count}/{objects} entrances {t.entrances.Count}/{doors}; palette {t.palette.Count}"));
			}
		}

		// A Human Tavern, an Elven Dwelling and the first special building: the kinds the
		// suite exports and rebuilds.
		private static List<GameLook> TemplatePicks()
		{
			IReadOnlyList<GameLook> looks = ModTemplates.GameLooks();
			return new[]
			{
				looks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.TAVERN && l.Culture == FACTION_TYPE.Human_Empire),
				looks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.DWELLING && l.Culture == FACTION_TYPE.Elven_Kingdom),
				looks.FirstOrDefault(l => l.Kind.IsSpecialStructure()),
			}.Where(l => l != null).ToList();
		}

		private static int Painted(List<string> rows)
		{
			return rows.Sum(r => r.Count(ch => ch != '.'));
		}

		private static int TilesIn(LocationStructureObject lso, string field)
		{
			Tilemap tm = AccessTools.Field(typeof(LocationStructureObject), field).GetValue(lso) as Tilemap;
			if (tm == null)
			{
				return 0;
			}
			int n = 0;
			foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
			{
				if (tm.GetTile(p) != null)
				{
					n++;
				}
			}
			return n;
		}
```

and extend the `using` list at the top of the file to:

```csharp
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Ruinarch.ModContent.Templates;
using UnityEngine;
using UnityEngine.Tilemaps;
```

- [ ] **Step 3: Build the harness to see it fail**

Run: `cd LOADER && ./tools/build-mod.sh ../RuinarchMods/RuinarchDebug /tmp/rd-build 2>&1 | grep -E " error |checked"`
Expected: `'GameLook' could not be found`, `'ModTemplates' does not contain a definition for 'GameLooks'` / `'Export'`.

- [ ] **Step 4: Write the framework code**

`LOADER/src/Ruinarch.ModContent/Templates/FrameworkLog.cs`:

```csharp
using System;
using System.IO;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	// The framework has no loader reference; it writes mods.log lines in the loader's format
	// itself, so a pack author sees template problems where every mod's messages are.
	internal static class FrameworkLog
	{
		/// <summary>Mods/ next to the game executable (the loader's ModsRoot).</summary>
		internal static string ModsRoot => Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, "Mods");

		internal static void Info(string message)
		{
			Debug.Log("[ModContent] " + message);
			Append("INFO", message);
		}

		internal static void Warning(string message)
		{
			Debug.LogWarning("[ModContent] " + message);
			Append("WARN", message);
		}

		private static void Append(string level, string message)
		{
			try
			{
				File.AppendAllText(Path.Combine(ModsRoot, "mods.log"), $"{DateTime.Now:HH:mm:ss} [{level}] [ModContent] {message}{Environment.NewLine}");
			}
			catch
			{
				// Logging must never take the game down.
			}
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/TemplateCatalogue.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Every game building prefab, read straight from LandmarkManager's StructureData (not
	/// through GetStructurePrefabs, which the framework patches and which throws for a missing
	/// combination). Demonic buildings are left out: templates are not for them.
	/// </summary>
	internal static class TemplateCatalogue
	{
		private static List<GameLook> _looks;

		internal static IReadOnlyList<GameLook> Looks()
		{
			if (_looks != null)
			{
				return _looks;
			}
			List<GameLook> looks = new List<GameLook>();
			IDictionary byType = AccessTools.Field(typeof(LandmarkManager), "structureData").GetValue(LandmarkManager.Instance) as IDictionary;
			foreach (DictionaryEntry e in byType ?? new Hashtable())
			{
				if (!(e.Value is StructureData data))
				{
					continue;
				}
				IDictionary byCulture = AccessTools.Field(typeof(StructureData), "structurePrefabs").GetValue(data) as IDictionary;
				foreach (DictionaryEntry f in byCulture ?? new Hashtable())
				{
					IDictionary choices = f.Value == null ? null : AccessTools.Field(f.Value.GetType(), "structureChoices").GetValue(f.Value) as IDictionary;
					foreach (DictionaryEntry c in choices ?? new Hashtable())
					{
						StructureSetting setting = (StructureSetting)c.Key;
						if (setting.structureType.IsPlayerStructure())
						{
							continue;
						}
						foreach (GameObject prefab in (c.Value as IList)?.OfType<GameObject>() ?? Enumerable.Empty<GameObject>())
						{
							if (prefab != null && prefab.GetComponent<LocationStructureObject>() != null)
							{
								looks.Add(new GameLook { Kind = setting.structureType, Culture = (FACTION_TYPE)f.Key, Material = setting.resource, Prefab = prefab });
							}
						}
					}
				}
			}
			// Prefabs are assets: the list stays valid for the session once it has anything.
			if (looks.Count > 0)
			{
				_looks = looks;
			}
			return looks;
		}

		/// <summary>The game's own looks for a kind, culture and material, the way the game
		/// picks them: a registered kind uses its PrefabSource's; no list for the culture
		/// falls back to FACTION_TYPE.None.</summary>
		internal static List<GameObject> Originals(STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			STRUCTURE_TYPE lookup = ContentRegistry.StructuresByType.TryGetValue((int)kind, out StructureRegistration reg) ? reg.PrefabSource : kind;
			List<GameObject> own = Looks().Where(l => l.Kind == lookup && l.Culture == culture && l.Material == material).Select(l => l.Prefab).ToList();
			return own.Count > 0 ? own : Looks().Where(l => l.Kind == lookup && l.Culture == FACTION_TYPE.None && l.Material == material).Select(l => l.Prefab).ToList();
		}

		internal static GameObject FindByName(string prefabName)
		{
			return Looks().FirstOrDefault(l => l.Prefab.name == prefabName)?.Prefab;
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/TemplatePalette.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// The tiles and sprites a template can name: the game's (gathered from every building
	/// prefab, by name) and a pack's own PNGs (<c>art/</c>), made into tiles at the game's
	/// pixels per unit. Also keeps one object, thin wall and entrance from the game's
	/// buildings to copy when a base building has none of its own.
	/// </summary>
	internal static class TemplatePalette
	{
		internal static readonly Dictionary<string, TileBase> GameTiles = new Dictionary<string, TileBase>();
		internal static readonly Dictionary<string, Sprite> GameSprites = new Dictionary<string, Sprite>();
		private static readonly Dictionary<string, Tile> ArtTiles = new Dictionary<string, Tile>();
		internal static GameObject ObjectPrototype;
		internal static GameObject ThinWallPrototype;
		internal static GameObject ConnectorPrototype;
		internal static float TilePixelsPerUnit = 64f;
		private static bool _gathered;

		// The game finds block walls by tile name (LocationStructureObject.RegisterWalls:
		// name contains "Wall"), so a pack PNG painted on the walls layer is named with it.
		private const string WallSuffix = "|Wall";

		internal static void Gather()
		{
			if (_gathered)
			{
				return;
			}
			bool ppuSet = false;
			foreach (GameLook look in TemplateCatalogue.Looks())
			{
				GameObject prefab = look.Prefab;
				foreach (Tilemap tm in prefab.GetComponentsInChildren<Tilemap>(true))
				{
					foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
					{
						TileBase t = tm.GetTile(p);
						if (t == null || GameTiles.ContainsKey(t.name))
						{
							continue;
						}
						GameTiles[t.name] = t;
						if (!ppuSet && t is Tile tile && tile.sprite != null)
						{
							TilePixelsPerUnit = tile.sprite.pixelsPerUnit;
							ppuSet = true;
						}
					}
				}
				foreach (SpriteRenderer sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (sr.sprite != null && !GameSprites.ContainsKey(sr.sprite.name))
					{
						GameSprites[sr.sprite.name] = sr.sprite;
					}
				}
				if (ObjectPrototype == null)
				{
					ObjectPrototype = prefab.GetComponentInChildren<StructureTemplateObjectData>(true)?.gameObject;
				}
				if (ThinWallPrototype == null)
				{
					ThinWallPrototype = prefab.GetComponentInChildren<ThinWallGameObject>(true)?.gameObject;
				}
				if (ConnectorPrototype == null)
				{
					ConnectorPrototype = prefab.GetComponentInChildren<StructureConnector>(true)?.gameObject;
				}
			}
			_gathered = GameTiles.Count > 0;
		}

		/// <summary>The tile a reference names (<c>game:name</c>, <c>art:file.png</c>), or null.</summary>
		internal static TileBase Tile(string reference, string packDirectory, bool forWalls)
		{
			if (reference == null)
			{
				return null;
			}
			if (reference.StartsWith("game:"))
			{
				return GameTiles.TryGetValue(reference.Substring(5), out TileBase t) ? t : null;
			}
			if (!reference.StartsWith("art:") || packDirectory == null)
			{
				return null;
			}
			string path = Path.Combine(packDirectory, "art", reference.Substring(4));
			string key = path + (forWalls ? WallSuffix : "");
			if (ArtTiles.TryGetValue(key, out Tile cached))
			{
				return cached;
			}
			Sprite sprite = ModArt.LoadSprite(path, TilePixelsPerUnit);
			if (sprite == null)
			{
				return null;
			}
			Tile tile = ScriptableObject.CreateInstance<Tile>();
			tile.sprite = sprite;
			tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
			tile.name = reference + (forWalls ? WallSuffix : "");
			ArtTiles[key] = tile;
			return tile;
		}

		/// <summary>The sprite a reference names, or null.</summary>
		internal static Sprite Sprite(string reference, string packDirectory)
		{
			if (reference == null)
			{
				return null;
			}
			if (reference.StartsWith("game:"))
			{
				return GameSprites.TryGetValue(reference.Substring(5), out Sprite s) ? s : null;
			}
			if (!reference.StartsWith("art:") || packDirectory == null)
			{
				return null;
			}
			Sprite art = ModArt.LoadSprite(Path.Combine(packDirectory, "art", reference.Substring(4)), TilePixelsPerUnit);
			if (art != null)
			{
				art.name = reference;
			}
			return art;
		}

		internal static string NameOf(TileBase tile)
		{
			if (tile == null)
			{
				return null;
			}
			string n = tile.name.EndsWith(WallSuffix) ? tile.name.Substring(0, tile.name.Length - WallSuffix.Length) : tile.name;
			return n.StartsWith("art:") ? n : "game:" + n;
		}

		internal static string NameOf(Sprite sprite)
		{
			if (sprite == null)
			{
				return null;
			}
			return sprite.name.StartsWith("art:") ? sprite.name : "game:" + sprite.name;
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/TemplateExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>Reads a building prefab (LocationStructureObject on its root) into a template.</summary>
	internal static class TemplateExporter
	{
		internal static T Field<T>(LocationStructureObject lso, string name)
		{
			return (T)AccessTools.Field(typeof(LocationStructureObject), name).GetValue(lso);
		}

		internal static BoxCollider2D ClickBox(LocationStructureObject lso)
		{
			LocationStructureObjectClickCollider click = Field<LocationStructureObjectClickCollider>(lso, "_clickCollider");
			return click == null ? null : AccessTools.Field(typeof(LocationStructureObjectClickCollider), "clickCollider").GetValue(click) as BoxCollider2D;
		}

		internal static BuildingTemplate Export(GameObject prefab, string id, STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			LocationStructureObject lso = prefab.GetComponent<LocationStructureObject>();
			if (lso == null)
			{
				throw new TemplateException(prefab.name + " has no LocationStructureObject on its root");
			}
			BuildingTemplate t = new BuildingTemplate
			{
				id = id,
				name = prefab.name,
				kind = ContentRegistry.StructuresByType.TryGetValue((int)kind, out StructureRegistration reg) ? reg.Id : kind.ToString(),
				cultures = new List<string> { culture.ToString() },
				material = material.ToString(),
				behavesLike = prefab.name,
			};
			Tilemap ground = Field<Tilemap>(lso, "_groundTileMap");
			Tilemap detail = Field<Tilemap>(lso, "_detailTileMap");
			Tilemap walls = Field<Tilemap>(lso, "_blockWallsTilemap");
			List<Vector3Int> painted = Cells(ground).Concat(Cells(detail)).Concat(Cells(walls)).ToList();
			if (painted.Count == 0)
			{
				throw new TemplateException(prefab.name + " has no painted cells");
			}
			int minX = painted.Min(c => c.x), minY = painted.Min(c => c.y), maxX = painted.Max(c => c.x), maxY = painted.Max(c => c.y);
			t.bounds = new[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
			Vector2Int size = Field<Vector2Int>(lso, "_size");
			Vector3Int center = Field<Vector3Int>(lso, "_center");
			t.size = new[] { size.x, size.y };
			t.center = new[] { center.x, center.y, center.z };
			// One palette for all layers; keys in the order cells are met, so the same building
			// always gets the same keys.
			Dictionary<string, char> keys = new Dictionary<string, char>();
			t.floor = Encode(ground, t, keys);
			t.detail = Encode(detail, t, keys);
			t.walls = Encode(walls, t, keys);
			foreach (ThinWallGameObject w in prefab.GetComponentsInChildren<ThinWallGameObject>(true))
			{
				t.thinWalls.Add(new TemplateThinWall
				{
					pos = Pos(lso, w.transform),
					rot = R(w.transform.localEulerAngles.z),
					sprites = w.GetComponentsInChildren<SpriteRenderer>(true).Select(s => TemplatePalette.NameOf(s.sprite)).ToList(),
				});
			}
			foreach (StructureTemplateObjectData o in prefab.GetComponentsInChildren<StructureTemplateObjectData>(true))
			{
				t.objects.Add(new TemplateObject
				{
					type = o.tileObjectType.ToString(),
					pos = Pos(lso, o.transform),
					rot = R(o.transform.localEulerAngles.z),
					sprite = TemplatePalette.NameOf(o.spriteRenderer != null ? o.spriteRenderer.sprite : null),
				});
			}
			foreach (StructureConnector c in Field<StructureConnector[]>(lso, "_connectors") ?? new StructureConnector[0])
			{
				if (c != null)
				{
					t.entrances.Add(new TemplatePoint { pos = Pos(lso, c.transform) });
				}
			}
			foreach (WardLightSpot s in Field<WardLightSpot[]>(lso, "_wardLightSpots") ?? new WardLightSpot[0])
			{
				if (s != null)
				{
					t.lightSpots.Add(new TemplatePoint { pos = Pos(lso, s.transform) });
				}
			}
			foreach (RoomTemplate r in lso.roomTemplates ?? new RoomTemplate[0])
			{
				t.rooms.Add((r.coordinatesInRoom ?? new Vector3Int[0]).Select(c => new[] { c.x, c.y, c.z }).ToList());
			}
			// A hand-made footprint (different from the floor cells) is kept as is.
			List<Vector3Int> occupied = Field<List<Vector3Int>>(lso, "_predeterminedOccupiedCoordinates");
			if (occupied != null && occupied.Count > 0 && !SameCells(occupied, Cells(ground)))
			{
				t.footprint = occupied.Select(c => new[] { c.x, c.y, c.z }).ToList();
			}
			BoxCollider2D box = ClickBox(lso);
			if (box != null)
			{
				t.clickBox = new TemplateBox { offset = new[] { R(box.offset.x), R(box.offset.y) }, size = new[] { R(box.size.x), R(box.size.y) } };
			}
			return t;
		}

		internal static List<Vector3Int> Cells(Tilemap tm)
		{
			List<Vector3Int> cells = new List<Vector3Int>();
			if (tm == null)
			{
				return cells;
			}
			foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
			{
				if (tm.GetTile(p) != null)
				{
					cells.Add(new Vector3Int(p.x, p.y, 0));
				}
			}
			return cells;
		}

		internal static bool SameCells(IEnumerable<Vector3Int> a, IEnumerable<Vector3Int> b)
		{
			return new HashSet<Vector2Int>(a.Select(c => new Vector2Int(c.x, c.y))).SetEquals(b.Select(c => new Vector2Int(c.x, c.y)));
		}

		private static List<string> Encode(Tilemap tm, BuildingTemplate t, Dictionary<string, char> keys)
		{
			List<string> rows = new List<string>();
			if (tm == null)
			{
				return rows;
			}
			int[] b = t.bounds;
			bool any = false;
			for (int y = b[1] + b[3] - 1; y >= b[1]; y--)
			{
				StringBuilder row = new StringBuilder(b[2]);
				for (int x = b[0]; x < b[0] + b[2]; x++)
				{
					TileBase tile = tm.GetTile(new Vector3Int(x, y, 0));
					if (tile == null)
					{
						row.Append('.');
						continue;
					}
					any = true;
					string reference = TemplatePalette.NameOf(tile);
					if (!keys.TryGetValue(reference, out char key))
					{
						if (keys.Count >= TemplateJson.Keys.Length)
						{
							throw new TemplateException($"more than {TemplateJson.Keys.Length} different tiles");
						}
						key = TemplateJson.Keys[keys.Count];
						keys[reference] = key;
						t.palette[key.ToString()] = reference;
					}
					row.Append(key);
				}
				rows.Add(row.ToString());
			}
			return any ? rows : new List<string>();
		}

		private static float[] Pos(LocationStructureObject lso, Transform child)
		{
			Vector3 p = lso.transform.InverseTransformPoint(child.position);
			return new[] { R(p.x), R(p.y), R(p.z) };
		}

		// Four decimals: positions are tile fractions; float noise must not make two exports differ.
		private static float R(float v)
		{
			return (float)Math.Round(v, 4);
		}
	}
}
```

Replace `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs` with:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>One of the game's own building looks: a prefab used for a kind, culture and material.</summary>
	public sealed class GameLook
	{
		public STRUCTURE_TYPE Kind;
		public FACTION_TYPE Culture;
		public RESOURCE Material;
		public GameObject Prefab;
	}

	/// <summary>
	/// Building templates: export the game's buildings, build and register new looks, load
	/// template packs. See docs/TEMPLATES.md. Everything but JSON needs a game scene (call it
	/// from in-game, not from a mod's OnLoad).
	/// </summary>
	public static class ModTemplates
	{
		/// <summary>Mods/ next to the game executable.</summary>
		public static string ModsRoot => FrameworkLog.ModsRoot;

		/// <summary>The template as JSON, the way a pack stores it.</summary>
		public static string ToJson(BuildingTemplate t)
		{
			return TemplateJson.ToJson(t);
		}

		/// <summary>Reads a template; throws <see cref="TemplateException"/> naming what is wrong.</summary>
		public static BuildingTemplate FromJson(string json)
		{
			return TemplateJson.FromJson(json);
		}

		/// <summary>Every game building look (village and special; demonic buildings excluded).</summary>
		public static IReadOnlyList<GameLook> GameLooks()
		{
			Ready();
			return TemplateCatalogue.Looks();
		}

		/// <summary>A game building as a template, under <paramref name="id"/>.</summary>
		public static BuildingTemplate Export(GameObject prefab, string id, STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			Ready();
			return TemplateExporter.Export(prefab, id, kind, culture, material);
		}

		private static void Ready()
		{
			if (LandmarkManager.Instance == null)
			{
				throw new TemplateException("building templates need a game scene: call them in-game, not from OnLoad");
			}
			TemplatePalette.Gather();
		}
	}
}
```

- [ ] **Step 5: Build, deploy, run; see it pass**

Run the "Build, deploy, run" block. Expected, for each of the three picks:
```
PASS exporting <prefab> captures every tile, wall, object and entrance :: <KIND> <Culture> <MATERIAL>: floor n/n detail n/n walls n/n thin n/n objects n/n entrances n/n; palette n
```
and `GAME/Mods/RuinarchDebug/template-<prefab>.json` files exist. Open one and check that the floor rows outline a building.

- [ ] **Step 6: Commit**

```bash
cd LOADER && git add src/Ruinarch.ModContent/Templates && git commit -m "ModContent: template catalogue, palette and export" && git push origin master && git push gitlab master
cd MODS && git add RuinarchDebug/TemplateSuite.cs && git commit -m "Harness: TemplateSuite exports game buildings" && git push origin master && git push gitlab master
```

---

### Task 3: The builder, proven by the round trip

**Files:**
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplateBuilder.cs`
- Modify: `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`
- Modify: `MODS/RuinarchDebug/TemplateSuite.cs`

**Interfaces:**
- Consumes: `TemplateExporter.Field/ClickBox/Cells`, `TemplatePalette.Tile/Sprite/prototypes`.
- Produces: `TemplateBuilder.Build(BuildingTemplate t, GameObject basePrefab, string poolName, string packDirectory) : GameObject` (inactive-in-hierarchy copy named `poolName`; throws `TemplateException`, leaving nothing behind); `TemplateBuilder.FloorCells(BuildingTemplate) : List<Vector3Int>`; public `ModTemplates.BuildDetached(BuildingTemplate, GameObject basePrefab, string packDirectory) : GameObject`, `ModTemplates.DestroyDetached(GameObject)`.

- [ ] **Step 1: Write the failing test**

In `TemplateSuite()`, after the export loop's closing brace and before the method's closing brace, add:

```csharp
			// 3. Round trip: a building rebuilt from its template exports to the same template,
			// and has the same footprint and border cells as the original.
			foreach (GameLook look in picks)
			{
				BuildingTemplate t = Guard($"export {look.Prefab.name}", () => ModTemplates.Export(look.Prefab, "autotest/rt-" + look.Prefab.name.ToLowerInvariant(), look.Kind, look.Culture, look.Material));
				GameObject built = t == null ? null : Guard($"build {look.Prefab.name} from its template", () => ModTemplates.BuildDetached(t, look.Prefab, null));
				if (built == null)
				{
					continue;
				}
				BuildingTemplate back = ModTemplates.Export(built, t.id, look.Kind, look.Culture, look.Material);
				// Identity fields name the object exported, not its look.
				back.name = t.name;
				back.behavesLike = t.behavesLike;
				string a = ModTemplates.ToJson(t), b = ModTemplates.ToJson(back);
				LocationStructureObject original = look.Prefab.GetComponent<LocationStructureObject>(), copy = built.GetComponent<LocationStructureObject>();
				bool footprint = SameCellSet(Occupied(original), Occupied(copy));
				bool border = SameCellSet(Border(original), Border(copy));
				Check($"{look.Prefab.name} rebuilt from its template matches the original", () =>
					(a == b && footprint && border, a == b ? $"{a.Length} chars; footprint same={footprint} border same={border} ({Border(original).Count}/{Border(copy).Count})" : "first difference: " + FirstDifference(a, b)));
				ModTemplates.DestroyDetached(built);
			}
```

and add these helpers to the class (after `TilesIn`):

```csharp
		// The game's occupied cells: the stored list, or (stored empty) the floor cells, as
		// LocationStructureObject.DetermineOccupiedTileCoordinates computes them.
		private static List<Vector2Int> Occupied(LocationStructureObject lso)
		{
			List<Vector3Int> stored = AccessTools.Field(typeof(LocationStructureObject), "_predeterminedOccupiedCoordinates").GetValue(lso) as List<Vector3Int>;
			if (stored != null && stored.Count > 0)
			{
				return stored.Select(c => new Vector2Int(c.x, c.y)).ToList();
			}
			Tilemap ground = AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(lso) as Tilemap;
			List<Vector2Int> cells = new List<Vector2Int>();
			foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
			{
				if (ground.GetTile(p) != null)
				{
					cells.Add(new Vector2Int(p.x, p.y));
				}
			}
			return cells;
		}

		private static List<Vector2Int> Border(LocationStructureObject lso)
		{
			return (AccessTools.Field(typeof(LocationStructureObject), "_borderCoordinates").GetValue(lso) as List<Vector2Int>) ?? new List<Vector2Int>();
		}

		private static bool SameCellSet(List<Vector2Int> a, List<Vector2Int> b)
		{
			return new HashSet<Vector2Int>(a).SetEquals(b);
		}

		private static string FirstDifference(string a, string b)
		{
			int i = 0;
			while (i < a.Length && i < b.Length && a[i] == b[i])
			{
				i++;
			}
			int from = System.Math.Max(0, i - 60);
			return $"at {i}: original '{a.Substring(from, System.Math.Min(120, a.Length - from))}' rebuilt '{b.Substring(from, System.Math.Min(120, b.Length - from))}'";
		}
```

- [ ] **Step 2: Build the harness to see it fail**

Run: `cd LOADER && ./tools/build-mod.sh ../RuinarchMods/RuinarchDebug /tmp/rd-build 2>&1 | grep -E " error |checked"`
Expected: `'ModTemplates' does not contain a definition for 'BuildDetached'` and `'DestroyDetached'`.

- [ ] **Step 3: Write the builder**

`LOADER/src/Ruinarch.ModContent/Templates/TemplateBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Builds a template into a prefab: a copy of a game building (which keeps every hidden
	/// setting the game expects: rooms behaviour, wall type and material, colliders) with its
	/// layers repainted and its furniture, thin walls, entrances, ward light spots, rooms,
	/// footprint and click area replaced by the template's. The copy lives under an inactive
	/// root, so it never wakes up itself; the copies the game's object pool makes from it do.
	/// </summary>
	internal static class TemplateBuilder
	{
		private static GameObject _hidden;

		internal static Transform Hidden
		{
			get
			{
				if (_hidden == null)
				{
					_hidden = new GameObject("ModContent.Templates");
					_hidden.SetActive(false);
					Object.DontDestroyOnLoad(_hidden);
				}
				return _hidden.transform;
			}
		}

		internal static GameObject Build(BuildingTemplate t, GameObject basePrefab, string poolName, string packDirectory)
		{
			GameObject root = Object.Instantiate(basePrefab, Hidden);
			try
			{
				// Saves record a building's root name and rebuild it from the pool of that name.
				root.name = poolName;
				LocationStructureObject lso = root.GetComponent<LocationStructureObject>();
				Paint(TemplateExporter.Field<Tilemap>(lso, "_groundTileMap"), t, t.floor, packDirectory, "floor", false);
				Paint(TemplateExporter.Field<Tilemap>(lso, "_detailTileMap"), t, t.detail, packDirectory, "detail", false);
				Paint(TemplateExporter.Field<Tilemap>(lso, "_blockWallsTilemap"), t, t.walls, packDirectory, "walls", true);
				ReplaceObjects(lso, t, packDirectory);
				ReplaceThinWalls(lso, t, packDirectory);
				AccessTools.Field(typeof(LocationStructureObject), "_connectors").SetValue(lso, ReplaceMarkers<StructureConnector>(lso, t.entrances, TemplatePalette.ConnectorPrototype).ToArray());
				AccessTools.Field(typeof(LocationStructureObject), "_wardLightSpots").SetValue(lso, ReplaceMarkers<WardLightSpot>(lso, t.lightSpots, null).ToArray());
				lso.roomTemplates = t.rooms.Select(r => new RoomTemplate { coordinatesInRoom = r.Select(Cell).ToArray() }).ToArray();
				AccessTools.Field(typeof(LocationStructureObject), "_size").SetValue(lso, new Vector2Int(t.size[0], t.size[1]));
				AccessTools.Field(typeof(LocationStructureObject), "_center").SetValue(lso, Cell(t.center));
				List<Vector3Int> occupied = t.footprint != null ? t.footprint.Select(Cell).ToList() : FloorCells(t);
				AccessTools.Field(typeof(LocationStructureObject), "_predeterminedOccupiedCoordinates").SetValue(lso, occupied);
				// The game's own rule: the 8 neighbours of occupied cells that are not occupied.
				lso.DetermineBorderCoordinates();
				SetClickBox(lso, t, occupied);
				return root;
			}
			catch (TemplateException)
			{
				Object.DestroyImmediate(root);
				throw;
			}
			catch (Exception e)
			{
				Object.DestroyImmediate(root);
				throw new TemplateException($"building it failed: {e.GetType().Name}: {e.Message}");
			}
		}

		/// <summary>The floor's painted cells (tilemap cells), top row first.</summary>
		internal static List<Vector3Int> FloorCells(BuildingTemplate t)
		{
			List<Vector3Int> cells = new List<Vector3Int>();
			for (int r = 0; r < t.floor.Count; r++)
			{
				for (int i = 0; i < t.floor[r].Length; i++)
				{
					if (t.floor[r][i] != '.')
					{
						cells.Add(new Vector3Int(t.bounds[0] + i, t.bounds[1] + t.bounds[3] - 1 - r, 0));
					}
				}
			}
			return cells;
		}

		private static Vector3Int Cell(int[] c)
		{
			return new Vector3Int(c[0], c[1], c.Length > 2 ? c[2] : 0);
		}

		private static void Paint(Tilemap tm, BuildingTemplate t, List<string> rows, string packDirectory, string layer, bool walls)
		{
			if (tm == null)
			{
				if (rows.Count > 0)
				{
					throw new TemplateException($"it paints {layer}, but {t.behavesLike ?? "the building it behaves like"} has no {layer} layer");
				}
				return;
			}
			tm.ClearAllTiles();
			for (int r = 0; r < rows.Count; r++)
			{
				for (int i = 0; i < rows[r].Length; i++)
				{
					char key = rows[r][i];
					if (key == '.')
					{
						continue;
					}
					string reference = t.palette[key.ToString()];
					TileBase tile = TemplatePalette.Tile(reference, packDirectory, walls) ?? throw new TemplateException("unknown tile " + reference);
					tm.SetTile(new Vector3Int(t.bounds[0] + i, t.bounds[1] + t.bounds[3] - 1 - r, 0), tile);
				}
			}
			tm.CompressBounds();
		}

		private static void Place(LocationStructureObject lso, Transform child, float[] pos, float rot)
		{
			child.position = lso.transform.TransformPoint(new Vector3(pos[0], pos[1], pos.Length > 2 ? pos[2] : 0f));
			child.localEulerAngles = new Vector3(0f, 0f, rot);
		}

		// A detached copy of a prototype, so the base building's own children can be removed
		// before the template's are made from it.
		private static GameObject Detach(GameObject prototype)
		{
			return prototype == null ? null : Object.Instantiate(prototype, Hidden);
		}

		private static void ReplaceObjects(LocationStructureObject lso, BuildingTemplate t, string packDirectory)
		{
			StructureTemplateObjectData[] old = lso.GetComponentsInChildren<StructureTemplateObjectData>(true);
			Transform parent = lso.objectsParent != null ? lso.objectsParent : lso.transform;
			GameObject prototype = Detach(old.Length > 0 ? old[0].gameObject : TemplatePalette.ObjectPrototype);
			foreach (StructureTemplateObjectData o in old)
			{
				Object.DestroyImmediate(o.gameObject);
			}
			try
			{
				if (t.objects.Count > 0 && prototype == null)
				{
					throw new TemplateException("no game object to copy furniture from");
				}
				foreach (TemplateObject o in t.objects)
				{
					GameObject go = Object.Instantiate(prototype, parent);
					go.name = o.type;
					StructureTemplateObjectData data = go.GetComponent<StructureTemplateObjectData>();
					data.tileObjectType = (TILE_OBJECT_TYPE)Enum.Parse(typeof(TILE_OBJECT_TYPE), o.type);
					data.spriteRenderer.sprite = o.sprite == null ? null : TemplatePalette.Sprite(o.sprite, packDirectory) ?? throw new TemplateException("unknown sprite " + o.sprite);
					Place(lso, go.transform, o.pos, o.rot);
				}
			}
			finally
			{
				if (prototype != null)
				{
					Object.DestroyImmediate(prototype);
				}
			}
		}

		private static void ReplaceThinWalls(LocationStructureObject lso, BuildingTemplate t, string packDirectory)
		{
			ThinWallGameObject[] old = lso.GetComponentsInChildren<ThinWallGameObject>(true);
			Transform parent = old.Length > 0 ? old[0].transform.parent : lso.transform;
			GameObject prototype = Detach(old.Length > 0 ? old[0].gameObject : TemplatePalette.ThinWallPrototype);
			foreach (ThinWallGameObject w in old)
			{
				Object.DestroyImmediate(w.gameObject);
			}
			try
			{
				if (t.thinWalls.Count > 0 && prototype == null)
				{
					throw new TemplateException("no game thin wall to copy from");
				}
				foreach (TemplateThinWall w in t.thinWalls)
				{
					GameObject go = Object.Instantiate(prototype, parent);
					SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
					for (int i = 0; i < renderers.Length && i < w.sprites.Count; i++)
					{
						renderers[i].sprite = w.sprites[i] == null ? null : TemplatePalette.Sprite(w.sprites[i], packDirectory) ?? throw new TemplateException("unknown thin-wall sprite " + w.sprites[i]);
					}
					Place(lso, go.transform, w.pos, w.rot);
				}
			}
			finally
			{
				if (prototype != null)
				{
					Object.DestroyImmediate(prototype);
				}
			}
		}

		private static List<T> ReplaceMarkers<T>(LocationStructureObject lso, List<TemplatePoint> points, GameObject fallback) where T : Component
		{
			T[] old = lso.GetComponentsInChildren<T>(true);
			Transform parent = old.Length > 0 ? old[0].transform.parent : lso.transform;
			GameObject prototype = Detach(old.Length > 0 ? old[0].gameObject : fallback);
			foreach (T o in old)
			{
				Object.DestroyImmediate(o.gameObject);
			}
			List<T> made = new List<T>();
			foreach (TemplatePoint p in points)
			{
				GameObject go = prototype != null ? Object.Instantiate(prototype, parent) : new GameObject(typeof(T).Name);
				if (prototype == null)
				{
					go.transform.SetParent(parent, false);
				}
				T marker = go.GetComponent<T>() ?? go.AddComponent<T>();
				Place(lso, go.transform, p.pos, 0f);
				made.Add(marker);
			}
			if (prototype != null)
			{
				Object.DestroyImmediate(prototype);
			}
			return made;
		}

		// The template's click area, else a box over the footprint (a repainted building's
		// shape can differ from the base's).
		private static void SetClickBox(LocationStructureObject lso, BuildingTemplate t, List<Vector3Int> occupied)
		{
			BoxCollider2D box = TemplateExporter.ClickBox(lso);
			if (box == null)
			{
				return;
			}
			if (t.clickBox != null)
			{
				box.offset = new Vector2(t.clickBox.offset[0], t.clickBox.offset[1]);
				box.size = new Vector2(t.clickBox.size[0], t.clickBox.size[1]);
				return;
			}
			Tilemap ground = TemplateExporter.Field<Tilemap>(lso, "_groundTileMap");
			Vector3 low = box.transform.InverseTransformPoint(ground.CellToWorld(new Vector3Int(occupied.Min(c => c.x), occupied.Min(c => c.y), 0)));
			Vector3 high = box.transform.InverseTransformPoint(ground.CellToWorld(new Vector3Int(occupied.Max(c => c.x) + 1, occupied.Max(c => c.y) + 1, 0)));
			box.offset = (low + high) / 2f;
			box.size = new Vector2(Mathf.Abs(high.x - low.x), Mathf.Abs(high.y - low.y));
		}
	}
}
```

Add to `ModTemplates` (after `Export`):

```csharp
		/// <summary>Builds a template on <paramref name="basePrefab"/> without registering it
		/// (a hidden copy, for previews and tests). Destroy it with <see cref="DestroyDetached"/>.</summary>
		public static GameObject BuildDetached(BuildingTemplate t, GameObject basePrefab, string packDirectory)
		{
			Ready();
			return TemplateBuilder.Build(t, basePrefab, basePrefab.name + "@" + t.id, packDirectory);
		}

		public static void DestroyDetached(GameObject built)
		{
			if (built != null)
			{
				Object.DestroyImmediate(built);
			}
		}
```

and add `using Object = UnityEngine.Object;` to its `using` list.

- [ ] **Step 4: Build, deploy, run; see it pass**

Expected for each pick:
```
PASS <prefab> rebuilt from its template matches the original :: <n> chars; footprint same=True border same=True (<n>/<n>)
```
If the JSON differs, the detail shows the first difference; fix the builder or exporter for that field, not the test. If only `border same=False` fails, compare the two border counts: the game's stored border may have been made before the last floor edit in the Unity editor; then the original's stored border is stale, and the check should compare the rebuilt border with the game's own rule applied to the original (call `DetermineBorderCoordinates()` on a detached copy of the original via `Object.Instantiate(look.Prefab, hidden)` before comparing). Record which in the commit message.

- [ ] **Step 5: Commit**

```bash
cd LOADER && git add src/Ruinarch.ModContent/Templates && git commit -m "ModContent: template builder; export/build round trip exact" && git push origin master && git push gitlab master
cd MODS && git add RuinarchDebug/TemplateSuite.cs && git commit -m "Harness: TemplateSuite round trip" && git push origin master && git push gitlab master
```

---

### Task 4: Registration: pools, variant lists, the missing-pack fallback, placing and saving

**Files:**
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplateRegistry.cs`
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplatePatches.cs`
- Modify: `LOADER/src/Ruinarch.ModContent/ContentPatches.cs` (`Patch_GetStructurePrefabs`, lines 77-92)
- Modify: `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`
- Modify: `LOADER/src/Ruinarch.Modding/ModLoader.cs` (after the mod loop, line 112)
- Modify: `MODS/RuinarchDebug/TemplateSuite.cs`

**Interfaces:**
- Consumes: `TemplateBuilder.Build`, `TemplateCatalogue.Originals/FindByName`, `TemplatePalette`.
- Produces: `TemplateRegistry.Validate(BuildingTemplate, string pack) : List<string>`, `TemplateRegistry.Register(BuildingTemplate, string pack) : List<GameObject>`, `TemplateRegistry.WithVariants(FACTION_TYPE, StructureSetting requested, List<GameObject> original) : List<GameObject>` (null when there is no original and no variant), `TemplateRegistry.PoolOrBase(string) : string`, `TemplateRegistry.PrefabsFor(string id)`, `TemplateRegistry.Startup()` (Task 5 fills in pack loading); public `ModTemplates.Validate`, `ModTemplates.Register`, `ModTemplates.PrefabsFor`.

- [ ] **Step 1: Write the failing test**

In `TemplateSuite()`, after the round-trip loop, add:

```csharp
			// 4. Registered: a variant joins the game's list for its kind, culture and material,
			// stands in a village, is reachable, and saves under its own name; a save naming a
			// look whose pack is gone falls back to the building it was based on.
			GameLook tavern = picks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.TAVERN);
			if (tavern == null)
			{
				Skip("a template joins the game's list of looks for its kind and culture", "no Human Tavern");
				yield break;
			}
			BuildingTemplate variant = ModTemplates.Export(tavern.Prefab, "autotest/tavern-variant", tavern.Kind, tavern.Culture, tavern.Material);
			variant.name = "Autotest Tavern";
			IReadOnlyList<GameObject> made = Guard("register a Tavern variant", () => ModTemplates.Register(variant, null));
			List<GameObject> originals = ModTemplates.GameLooks().Where(l => l.Kind == tavern.Kind && l.Culture == tavern.Culture && l.Material == tavern.Material).Select(l => l.Prefab).ToList();
			List<GameObject> listed = Inner_Maps.InnerMapManager.Instance.GetStructurePrefabsForStructure(tavern.Culture, tavern.Kind, tavern.Material);
			Check("a template joins the game's list of looks for its kind and culture", () =>
				(made != null && made.Count == 1 && listed.Contains(made[0]) && originals.All(listed.Contains),
				$"listed {listed.Count}: {string.Join(", ", listed.Select(g => g.name))}"));
			if (made == null || made.Count != 1)
			{
				yield break;
			}
			string poolName = made[0].name;
			NPCSettlement village = Villages().FirstOrDefault(v => v.owner != null && v.owner.factionType.type == tavern.Culture);
			LocationGridTile spot = null;
			if (village != null)
			{
				LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village, new StructureSetting(tavern.Kind, tavern.Material), out spot, out string _, out int _, out LocationGridTile _);
			}
			LocationStructure placed = spot == null ? null : Guard("place the variant", () => spot.tileObjectComponent.genericTileObject.InstantPlaceStructure(poolName, village));
			if (placed == null)
			{
				Skip("a building from a template stands in a village and its entrances can be reached", village == null ? "no village of the culture" : "no room for a Tavern");
			}
			else
			{
				LocationStructureObject obj = (placed as ManMadeStructure)?.structureObj;
				List<LocationGridTile> doors = obj?.connectors.Select(c => c.tileLocation).Where(x => x != null).ToList() ?? new List<LocationGridTile>();
				Character walker = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.limiterComponent.canMove);
				Check("a building from a template stands in a village and its entrances can be reached", () =>
					(obj != null && obj.name.Replace("(Clone)", "") == poolName && placed.tiles.Count == variant.floor.Sum(r => r.Count(ch => ch != '.')) && doors.Count == variant.entrances.Count
						&& walker != null && doors.All(d => walker.movementComponent.HasPathToEvenIfDiffRegion(d)),
					$"{obj?.name}: tiles {placed.tiles.Count}, entrances {doors.Count}/{variant.entrances.Count}, walker {walker?.name ?? "none"} reaches all={walker != null && doors.All(d => walker.movementComponent.HasPathToEvenIfDiffRegion(d))}"));
				SaveDataManMadeStructure data = new SaveDataManMadeStructure();
				data.Save(placed);
				Check("saving a template building records its look", () => (data.structureTemplateName == poolName, data.structureTemplateName));
				InnerMapCameraMove.Instance.CenterCameraOn(obj.gameObject);
				yield return Screenshot("template-variant.png");
			}
			// The way villagers get buildings: a villager places the variant as a blueprint with
			// the game's own job (CharacterJobTriggerComponent.TriggerPlaceBlueprint), and the
			// village builds it (BUILD_BLUEPRINT) from materials.
			Character placer = village?.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.limiterComponent.canMove && r.limiterComponent.canPerform && !r.partyComponent.hasParty);
			LocationGridTile bpSpot = null, bpConnector = null;
			if (placer != null)
			{
				LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village, new StructureSetting(tavern.Kind, tavern.Material), out bpSpot, out string _, out int _, out bpConnector);
			}
			bool bpQueued = bpSpot != null && Guard("give a villager the blueprint job", () =>
			{
				placer.jobQueue.CancelAllJobs();
				placer.jobComponent.TriggerPlaceBlueprint(poolName, new StructureSetting(tavern.Kind, tavern.Material), bpSpot, bpConnector, out JobQueueItem job);
				return job != null && placer.jobQueue.AddJobInQueue(job) ? job : null;
			}) != null;
			if (!bpQueued)
			{
				Skip("a villager places a template building as a blueprint", placer == null ? "no free villager" : "no room for a blueprint");
			}
			else
			{
				Func<LocationStructureObject> blueprint = () => UnityEngine.Object.FindObjectsOfType<LocationStructureObject>().FirstOrDefault(o => o.name.StartsWith(poolName) && o.currentVisualMode == LocationStructureObject.Structure_Visual_Mode.Blueprint);
				yield return WaitGameHours(12f, () => blueprint() != null);
				LocationStructureObject bp = blueprint();
				Check("a villager places a template building as a blueprint", () => (bp != null, bp != null ? bp.name : $"{placer.name} job={placer.currentJob?.jobType.ToString() ?? "none"}"));
				if (bp != null)
				{
					Func<bool> built = () => village.structures.TryGetValue(tavern.Kind, out List<LocationStructure> all)
						&& all.Any(s => s is ManMadeStructure m && m.structureObj != null && m.structureObj.name.StartsWith(poolName) && m.structureObj.currentVisualMode == LocationStructureObject.Structure_Visual_Mode.Built && m != placed);
					yield return WaitGameHours(48f, built);
					List<JobQueueItem> builds = new List<JobQueueItem>();
					village.PopulateJobsOfType(builds, JOB_TYPE.BUILD_BLUEPRINT);
					if (!built() && builds.All(j => j.assignedCharacter == null))
					{
						Skip("villagers build a template building", $"nobody took the build job in two days ({builds.Count} job(s)): the game's own materials and builders");
					}
					else
					{
						Check("villagers build a template building", () => (built(), $"build jobs {builds.Count}, takers {string.Join(", ", builds.Select(j => j.assignedCharacter?.name ?? "-"))}"));
					}
				}
			}
			GameObject fallback = Guard("load a building whose pack is gone", () =>
				ObjectPoolManager.Instance.InstantiateObjectFromPool(tavern.Prefab.name + "@autotest/no-such-pack", Vector3.zero, Quaternion.identity));
			Check("a save naming a missing pack falls back to the building it was based on", () =>
				(fallback != null && fallback.name.StartsWith(tavern.Prefab.name) && fallback.GetComponent<LocationStructureObject>() != null, fallback?.name ?? "nothing"));
			if (fallback != null)
			{
				ObjectPoolManager.Instance.DestroyObject(fallback);
			}
```

Add `using System;`, `using Inner_Maps;` and `using Locations.Settlements;` to the file's `using` list.

- [ ] **Step 2: Build the harness to see it fail**

Expected: `'ModTemplates' does not contain a definition for 'Register'`.

- [ ] **Step 3: Write the registry, patches and loader start**

`LOADER/src/Ruinarch.ModContent/Templates/TemplateRegistry.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Registered templates: each is built once per base building and becomes an object
	/// pool named <c>base@id</c> (what saves record) and a variant in the game's list for
	/// its kind, culture and material.
	/// </summary>
	internal static class TemplateRegistry
	{
		private sealed class Entry
		{
			internal BuildingTemplate Template;
			internal string Pack;
			internal readonly List<GameObject> Prefabs = new List<GameObject>();
		}

		private static readonly Dictionary<string, Entry> ById = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<(STRUCTURE_TYPE, FACTION_TYPE, RESOURCE), List<GameObject>> Variants = new Dictionary<(STRUCTURE_TYPE, FACTION_TYPE, RESOURCE), List<GameObject>>();
		// The game's list plus ours, rebuilt only when the game hands over a different list.
		private static readonly Dictionary<(STRUCTURE_TYPE, FACTION_TYPE, RESOURCE), (List<GameObject> original, List<GameObject> combined)> Combined = new Dictionary<(STRUCTURE_TYPE, FACTION_TYPE, RESOURCE), (List<GameObject>, List<GameObject>)>();
		private static readonly HashSet<string> MissingWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static bool _started;

		internal static List<GameObject> PrefabsFor(string id)
		{
			return ById.TryGetValue(id, out Entry e) ? e.Prefabs.ToList() : new List<GameObject>();
		}

		internal static STRUCTURE_TYPE? ResolveKind(string kind)
		{
			if (string.IsNullOrEmpty(kind))
			{
				return null;
			}
			if (Enum.TryParse(kind, out STRUCTURE_TYPE st) && Enum.IsDefined(typeof(STRUCTURE_TYPE), st))
			{
				return st;
			}
			StructureRegistration reg = ContentRegistry.Structures.FirstOrDefault(r => r.Id == kind);
			return reg != null ? reg.StructureType : (STRUCTURE_TYPE?)null;
		}

		private static bool TryParse<T>(string s, out T value) where T : struct
		{
			return Enum.TryParse(s, out value) && Enum.IsDefined(typeof(T), value);
		}

		private static GameObject BaseFor(BuildingTemplate t, STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			return t.behavesLike != null ? TemplateCatalogue.FindByName(t.behavesLike) : TemplateCatalogue.Originals(kind, culture, material).FirstOrDefault();
		}

		internal static List<string> Validate(BuildingTemplate t, string pack)
		{
			List<string> p = new List<string>();
			if (t.formatVersion != 1)
			{
				p.Add($"formatVersion {t.formatVersion} is not supported (1 is)");
			}
			if (string.IsNullOrEmpty(t.id) || t.id.IndexOf('/') <= 0)
			{
				p.Add("id must look like pack-id/template-id");
			}
			else if (ById.ContainsKey(t.id))
			{
				p.Add($"id {t.id} is already registered");
			}
			STRUCTURE_TYPE? kind = ResolveKind(t.kind);
			if (kind == null)
			{
				p.Add($"unknown kind {t.kind}");
			}
			else if (kind.Value.IsPlayerStructure())
			{
				p.Add($"{t.kind} is a demonic building; templates are for village and special buildings");
			}
			bool materialOk = TryParse(t.material, out RESOURCE material);
			if (!materialOk)
			{
				p.Add($"unknown material {t.material}");
			}
			List<FACTION_TYPE> cultures = new List<FACTION_TYPE>();
			foreach (string c in t.cultures ?? new List<string>())
			{
				if (TryParse(c, out FACTION_TYPE f))
				{
					cultures.Add(f);
				}
				else
				{
					p.Add($"unknown culture {c}");
				}
			}
			if (cultures.Count == 0)
			{
				p.Add("no cultures");
			}
			if (t.size == null || t.size.Length != 2)
			{
				p.Add("size must be [w, h]");
			}
			if (t.center == null || t.center.Length < 2)
			{
				p.Add("center must be [x, y] or [x, y, z]");
			}
			if (t.bounds == null || t.bounds.Length != 4 || t.bounds[2] <= 0 || t.bounds[3] <= 0)
			{
				p.Add("bounds must be [x, y, w, h] with w and h above 0");
			}
			else
			{
				Rows("floor", t.floor, t, p, required: true);
				Rows("detail", t.detail, t, p, required: false);
				Rows("walls", t.walls, t, p, required: false);
			}
			foreach (KeyValuePair<string, string> kv in t.palette ?? new Dictionary<string, string>())
			{
				if (kv.Key.Length != 1 || kv.Key == ".")
				{
					p.Add($"palette key '{kv.Key}' must be one character other than '.'");
				}
				if (TemplatePalette.Tile(kv.Value, pack, false) == null)
				{
					p.Add($"unknown tile {kv.Value}");
				}
			}
			foreach (TemplateObject o in t.objects ?? new List<TemplateObject>())
			{
				if (!TryParse(o.type, out TILE_OBJECT_TYPE _))
				{
					p.Add($"unknown object type {o.type}");
				}
				if (o.sprite != null && TemplatePalette.Sprite(o.sprite, pack) == null)
				{
					p.Add($"unknown sprite {o.sprite}");
				}
				Pos($"object {o.type}", o.pos, p);
			}
			foreach (TemplateThinWall w in t.thinWalls ?? new List<TemplateThinWall>())
			{
				Pos("thin wall", w.pos, p);
				foreach (string s in w.sprites.Where(s => s != null && TemplatePalette.Sprite(s, pack) == null))
				{
					p.Add($"unknown thin-wall sprite {s}");
				}
			}
			foreach (TemplatePoint e in t.entrances ?? new List<TemplatePoint>())
			{
				Pos("entrance", e.pos, p);
			}
			foreach (TemplatePoint l in t.lightSpots ?? new List<TemplatePoint>())
			{
				Pos("light spot", l.pos, p);
			}
			if (kind != null && materialOk)
			{
				foreach (FACTION_TYPE c in cultures.Where(c => BaseFor(t, kind.Value, c, material) == null))
				{
					p.Add($"no {t.behavesLike ?? "game building"} to behave like for {t.kind} {c} {t.material}");
				}
			}
			return p;
		}

		private static void Rows(string layer, List<string> rows, BuildingTemplate t, List<string> p, bool required)
		{
			if (rows == null || rows.Count == 0)
			{
				if (required)
				{
					p.Add($"{layer} is empty");
				}
				return;
			}
			if (rows.Count != t.bounds[3])
			{
				p.Add($"{layer} has {rows.Count} rows; bounds say {t.bounds[3]}");
			}
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i].Length != t.bounds[2])
				{
					p.Add($"{layer} row {i + 1} has {rows[i].Length} cells; bounds say {t.bounds[2]}");
				}
				foreach (char ch in rows[i].Where(ch => ch != '.').Distinct())
				{
					if (t.palette == null || !t.palette.ContainsKey(ch.ToString()))
					{
						p.Add($"{layer} row {i + 1}: '{ch}' is not in the palette");
					}
				}
			}
		}

		private static void Pos(string what, float[] pos, List<string> p)
		{
			if (pos == null || pos.Length != 3)
			{
				p.Add($"{what}: pos must be [x, y, z]");
			}
		}

		internal static List<GameObject> Register(BuildingTemplate t, string pack)
		{
			List<string> problems = Validate(t, pack);
			if (problems.Count > 0)
			{
				throw new TemplateException(string.Join("; ", problems));
			}
			STRUCTURE_TYPE kind = ResolveKind(t.kind).Value;
			TryParse(t.material, out RESOURCE material);
			Entry entry = new Entry { Template = t, Pack = pack };
			Dictionary<string, GameObject> byBase = new Dictionary<string, GameObject>();
			List<(FACTION_TYPE, GameObject)> variants = new List<(FACTION_TYPE, GameObject)>();
			foreach (string c in t.cultures)
			{
				TryParse(c, out FACTION_TYPE culture);
				GameObject basePrefab = BaseFor(t, kind, culture, material);
				if (!byBase.TryGetValue(basePrefab.name, out GameObject built))
				{
					string poolName = basePrefab.name + "@" + t.id;
					built = TemplateBuilder.Build(t, basePrefab, poolName, pack);
					byBase[basePrefab.name] = built;
					entry.Prefabs.Add(built);
				}
				variants.Add((culture, built));
			}
			// Pools and lists only once every culture built: a failure leaves nothing half-registered.
			foreach (GameObject built in entry.Prefabs)
			{
				AccessTools.Method(typeof(ObjectPoolManager), "CreateNewPool").Invoke(ObjectPoolManager.Instance, new object[] { built, built.name, 0, true, true, false });
			}
			foreach ((FACTION_TYPE culture, GameObject built) in variants)
			{
				var key = (kind, culture, material);
				if (!Variants.TryGetValue(key, out List<GameObject> list))
				{
					Variants[key] = list = new List<GameObject>();
				}
				list.Add(built);
				Combined.Remove(key);
			}
			ById[t.id] = entry;
			return entry.Prefabs.ToList();
		}

		/// <summary>The game's list for a kind (as requested, before the framework redirects a
		/// registered kind to its PrefabSource), culture and material, with this session's
		/// variants appended; the variants alone where the game has no list; null for neither.</summary>
		internal static List<GameObject> WithVariants(FACTION_TYPE culture, StructureSetting requested, List<GameObject> original)
		{
			var key = (requested.structureType, culture, requested.resource);
			if (!Variants.TryGetValue(key, out List<GameObject> mine))
			{
				return original;
			}
			if (original == null)
			{
				return mine;
			}
			if (Combined.TryGetValue(key, out var cached) && ReferenceEquals(cached.original, original))
			{
				return cached.combined;
			}
			List<GameObject> combined = new List<GameObject>(original);
			combined.AddRange(mine);
			Combined[key] = (original, combined);
			return combined;
		}

		/// <summary>A pool name as saves record it; a template look whose pack is gone maps
		/// to the building it was based on (the part before '@').</summary>
		internal static string PoolOrBase(string poolName)
		{
			int at = poolName == null ? -1 : poolName.IndexOf('@');
			if (at <= 0 || PoolExists(poolName))
			{
				return poolName;
			}
			string fallback = poolName.Substring(0, at);
			if (MissingWarned.Add(poolName))
			{
				FrameworkLog.Warning($"Building look {poolName.Substring(at + 1)} is missing (its pack is gone or failed to load); using {fallback}.");
			}
			return fallback;
		}

		private static bool PoolExists(string poolName)
		{
			IDictionary pools = ObjectPoolManager.Instance == null ? null : AccessTools.Field(typeof(ObjectPoolManager), "allObjectPools").GetValue(ObjectPoolManager.Instance) as IDictionary;
			return pools != null && pools.Contains(poolName.ToUpperInvariant());
		}

		/// <summary>Once per session, in the first game scene, before any world is made or loaded.</summary>
		internal static void Startup()
		{
			if (_started)
			{
				return;
			}
			_started = true;
			TemplatePalette.Gather();
			FrameworkLog.Info($"Templates ready: {TemplatePalette.GameTiles.Count} game tiles, {TemplatePalette.GameSprites.Count} sprites, {TemplateCatalogue.Looks().Count} building looks.");
		}
	}
}
```

`LOADER/src/Ruinarch.ModContent/Templates/TemplatePatches.cs`:

```csharp
using System;
using HarmonyLib;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>Templates are built in the first game scene, on the main thread, before a
	/// world is generated or a save rebuilds its buildings by pool name.</summary>
	[HarmonyPatch(typeof(Initializer), "InitializeDataBeforeWorldCreationMainThread")]
	internal static class Patch_TemplatesStartup
	{
		private static void Postfix()
		{
			try
			{
				TemplateRegistry.Startup();
			}
			catch (Exception e)
			{
				FrameworkLog.Warning("Building templates failed to start: " + e);
			}
		}
	}

	/// <summary>A save made with a template pack loads without it: the look falls back to
	/// the building it was based on.</summary>
	[HarmonyPatch(typeof(ObjectPoolManager), nameof(ObjectPoolManager.InstantiateObjectFromPool), new Type[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(bool) })]
	internal static class Patch_TemplatePoolFallback
	{
		private static void Prefix(ref string poolName)
		{
			poolName = TemplateRegistry.PoolOrBase(poolName);
		}
	}
}
```

In `LOADER/src/Ruinarch.ModContent/ContentPatches.cs`, replace the whole `Patch_GetStructurePrefabs` class (the `[HarmonyPatch(typeof(StructureData), nameof(StructureData.GetStructurePrefabs), ...)]` attribute through its closing brace) with:

```csharp
	[HarmonyPatch(typeof(StructureData), nameof(StructureData.GetStructurePrefabs), new Type[] { typeof(FACTION_TYPE), typeof(StructureSetting) })]
	internal static class Patch_GetStructurePrefabs
	{
		private static void Prefix(ref StructureSetting p_structureSetting, out StructureSetting __state)
		{
			__state = p_structureSetting;
			if (ContentRegistry.StructuresByType.TryGetValue((int)p_structureSetting.structureType, out StructureRegistration reg))
			{
				p_structureSetting = new StructureSetting(reg.PrefabSource, p_structureSetting.resource);
			}
		}

		// Template variants join the list for the kind as asked for (a registered kind's own,
		// not its PrefabSource's). Where the game has no list it throws; variants alone then
		// answer.
		private static Exception Finalizer(Exception __exception, FACTION_TYPE p_type, StructureSetting __state, ref List<GameObject> __result)
		{
			List<GameObject> withVariants = Templates.TemplateRegistry.WithVariants(p_type, __state, __exception == null ? __result : null);
			if (withVariants != null)
			{
				__result = withVariants;
				return null;
			}
			return __exception;
		}
	}
```

and add `using System.Collections.Generic;` to its `using` list. Keep the existing doc comment above the class and add one sentence to it: `Template variants are appended (Templates/TemplateRegistry.WithVariants).`

Add to `ModTemplates` (after `DestroyDetached`):

```csharp
		/// <summary>What is wrong with a template (empty when nothing). <paramref name="packDirectory"/>
		/// is where its <c>art/</c> is (null: game art only).</summary>
		public static IReadOnlyList<string> Validate(BuildingTemplate t, string packDirectory)
		{
			Ready();
			return TemplateRegistry.Validate(t, packDirectory);
		}

		/// <summary>Builds and registers a template: one prefab per base building, each an object
		/// pool and a variant in the game's lists. Throws <see cref="TemplateException"/>.</summary>
		public static IReadOnlyList<GameObject> Register(BuildingTemplate t, string packDirectory)
		{
			Ready();
			return TemplateRegistry.Register(t, packDirectory);
		}

		/// <summary>The prefabs a registered template was built into.</summary>
		public static IReadOnlyList<GameObject> PrefabsFor(string templateId)
		{
			return TemplateRegistry.PrefabsFor(templateId);
		}
```

In `LOADER/src/Ruinarch.Modding/ModLoader.cs`, after the `foreach (string dll in dlls) { TryLoad(dll); }` loop (line 112) and before the `Done.` log line, add:

```csharp
				StartContentFramework();
```

and add this method to `ModLoader` (after `Initialize`):

```csharp
		// The content framework (Mods/Ruinarch.ModContent.dll) installs itself when a mod calls
		// it; start it here too, so template packs (data only, no DLL) load with no code mod
		// present. By reflection: the loader does not depend on the framework.
		private static void StartContentFramework()
		{
			try
			{
				string path = Path.Combine(ModsRoot, "Ruinarch.ModContent.dll");
				Assembly framework = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => SafeName(a) == "Ruinarch.ModContent")
					?? (File.Exists(path) ? Assembly.LoadFrom(path) : null);
				framework?.GetType("Ruinarch.ModContent.ModContent")?.GetMethod("Install")?.Invoke(null, null);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ModLoader] Could not start the content framework: {e.Message}");
			}
		}
```

- [ ] **Step 4: Build (framework, loader, mods), install the loader, run; see it pass**

Run the "Build, deploy, run" block, plus the patcher install line. Expected:
```
PASS a template joins the game's list of looks for its kind and culture :: listed <n+1>: ..., <Tavern>@autotest/tavern-variant
PASS a building from a template stands in a village and its entrances can be reached :: <Tavern>@autotest/tavern-variant(Clone): tiles n, entrances k/k, walker <name> reaches all=True
PASS saving a template building records its look :: <Tavern>@autotest/tavern-variant
PASS a save naming a missing pack falls back to the building it was based on :: <Tavern>...
PASS a villager places a template building as a blueprint :: <Tavern>@autotest/tavern-variant(Clone)
PASS villagers build a template building :: build jobs ..., takers ...
```
and `mods.log` has `[ModContent] Templates ready: ...` and `Building look autotest/no-such-pack is missing`. Look at `GAME/Mods/RuinarchDebug/template-variant.png`: a Tavern standing in the village.
If "stands in a village" is skipped for room, it is not a pass: re-run on a fresh world until it runs once.

- [ ] **Step 5: Commit**

```bash
cd LOADER && git add src && git commit -m "ModContent: register templates as variants; missing-pack fallback; loader starts the framework" && git push origin master && git push gitlab master
cd MODS && git add RuinarchDebug/TemplateSuite.cs && git commit -m "Harness: TemplateSuite variants, placement, saves, fallback" && git push origin master && git push gitlab master
```

---

### Task 5: Packs on disk: loading, problems, PNG tiles, switching a pack off

**Files:**
- Create: `LOADER/src/Ruinarch.ModContent/Templates/TemplatePacks.cs`
- Modify: `LOADER/src/Ruinarch.ModContent/Templates/TemplateRegistry.cs` (`Startup`)
- Modify: `LOADER/src/Ruinarch.ModContent/Templates/ModTemplates.cs`
- Modify: `LOADER/src/Ruinarch.Modding/ModLoader.cs` (list packs as known mods)
- Modify: `MODS/RuinarchDebug/TemplateSuite.cs`

**Interfaces:**
- Consumes: `TemplateRegistry.Register`, `TemplateJson.FromJson`, `FrameworkLog`.
- Produces: public `PackLoadReport { string PackId; bool Disabled; int Loaded; List<string> Problems; }`, `ModTemplates.LoadPack(string packDirectory) : PackLoadReport`; internal `TemplatePacks.PackDirectories() : IEnumerable<string>`, `TemplatePacks.Load(string dir) : PackLoadReport`, `TemplatePacks.PackId(string dir) : string`, `TemplatePacks.IsDisabled(string id) : bool`.

- [ ] **Step 1: Write the failing test**

In `TemplateSuite()`, after the fallback block, add:

```csharp
			// 5. A pack on disk: its good template loads (with its own PNG as the floor), its
			// broken ones are skipped with a reason in mods.log, and a pack switched off in the
			// mod manager does not load.
			string pack = Path.Combine(Path.GetDirectoryName(_logPath), "autotest-pack");
			if (Directory.Exists(pack))
			{
				Directory.Delete(pack, true);
			}
			Directory.CreateDirectory(Path.Combine(pack, "templates"));
			Directory.CreateDirectory(Path.Combine(pack, "art"));
			File.WriteAllText(Path.Combine(pack, "mod.json"), "{\"id\":\"autotest-pack\",\"name\":\"Autotest pack\",\"version\":\"1.0.0\",\"author\":\"autotest\",\"description\":\"\"}");
			Texture2D red = new Texture2D(64, 64);
			red.SetPixels(Enumerable.Repeat(Color.red, 64 * 64).ToArray());
			red.Apply();
			File.WriteAllBytes(Path.Combine(pack, "art", "red.png"), red.EncodeToPNG());
			BuildingTemplate redFloor = ModTemplates.Export(tavern.Prefab, "autotest-pack/red-floor", tavern.Kind, tavern.Culture, tavern.Material);
			char redKey = TemplateKeyFree(redFloor);
			redFloor.palette[redKey.ToString()] = "art:red.png";
			redFloor.floor = redFloor.floor.Select(r => new string(r.Select(ch => ch == '.' ? '.' : redKey).ToArray())).ToList();
			File.WriteAllText(Path.Combine(pack, "templates", "red-floor.json"), ModTemplates.ToJson(redFloor));
			BuildingTemplate broken = ModTemplates.FromJson(ModTemplates.ToJson(redFloor));
			broken.id = "autotest-pack/broken";
			broken.palette[broken.palette.Keys.First()] = "game:No_Such_Tile";
			File.WriteAllText(Path.Combine(pack, "templates", "broken.json"), ModTemplates.ToJson(broken));
			File.WriteAllText(Path.Combine(pack, "templates", "notjson.json"), "{ this is not json");
			int logMark = ModsLogLength();
			PackLoadReport report = Guard("load the autotest pack", () => ModTemplates.LoadPack(pack));
			Check("a template pack on disk loads; broken templates are skipped with a reason", () =>
				(report != null && report.Loaded == 1 && report.Problems.Count == 2
					&& report.Problems.Any(x => x.Contains("broken.json") && x.Contains("No_Such_Tile")) && report.Problems.Any(x => x.Contains("notjson.json")),
				report == null ? "no report" : $"loaded {report.Loaded}; " + string.Join(" | ", report.Problems)));
			Check("a pack's problems are written to mods.log", () => (ModsLogHasSince(logMark, "broken.json") && ModsLogHasSince(logMark, "notjson.json"), "mods.log"));
			GameObject redBuilt = ModTemplates.PrefabsFor("autotest-pack/red-floor").FirstOrDefault();
			Tilemap redGround = redBuilt == null ? null : AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(redBuilt.GetComponent<LocationStructureObject>()) as Tilemap;
			Tile redTile = null;
			if (redGround != null)
			{
				foreach (Vector3Int p in redGround.cellBounds.allPositionsWithin)
				{
					if (redGround.GetTile(p) is Tile t0)
					{
						redTile = t0;
						break;
					}
				}
			}
			Check("a pack's PNG becomes a floor tile", () =>
				(redTile != null && redTile.sprite != null && redTile.sprite.texture.width == 64 && redTile.sprite.texture.GetPixel(10, 10) == Color.red,
				redTile == null ? "no tile" : $"{redTile.name} {redTile.sprite?.texture.width}px {redTile.sprite?.texture.GetPixel(10, 10)}"));
			if (village != null && redBuilt != null)
			{
				LocationGridTile redSpot = null;
				LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village, new StructureSetting(tavern.Kind, tavern.Material), out redSpot, out string _, out int _, out LocationGridTile _);
				LocationStructure redPlaced = redSpot == null ? null : Guard("place the red-floor Tavern", () => redSpot.tileObjectComponent.genericTileObject.InstantPlaceStructure(redBuilt.name, village));
				if (redPlaced is ManMadeStructure redMade && redMade.structureObj != null)
				{
					InnerMapCameraMove.Instance.CenterCameraOn(redMade.structureObj.gameObject);
					yield return Screenshot("template-png.png");
				}
			}
			// Switched off: the pack's id in modloader.config.json's disabled list. The config is
			// the player's; it is restored exactly as found.
			string config = Path.Combine(ModTemplates.ModsRoot, "modloader.config.json");
			string kept = File.Exists(config) ? File.ReadAllText(config) : null;
			PackLoadReport off = null;
			try
			{
				File.WriteAllText(config, "{\"disabled\":[\"autotest-pack\"]}");
				off = ModTemplates.LoadPack(pack);
			}
			finally
			{
				if (kept != null)
				{
					File.WriteAllText(config, kept);
				}
				else
				{
					File.Delete(config);
				}
			}
			Check("a pack switched off in the mod manager does not load", () => (off != null && off.Disabled && off.Loaded == 0, off == null ? "no report" : $"disabled={off.Disabled} loaded={off.Loaded}"));
			Check("the framework reports its templates at startup", () => (ModsLogHas("[ModContent] Templates ready:") && ModsLogHas("[ModContent] Template packs:"), "mods.log"));
			Directory.Delete(pack, true);
```

and add the helper (after `FirstDifference`):

```csharp
		private static char TemplateKeyFree(BuildingTemplate t)
		{
			return "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".First(ch => !t.palette.ContainsKey(ch.ToString()));
		}
```

(Loading the same pack twice registers `autotest-pack/red-floor` once: the second load reports it as already registered, which the "switched off" check does not reach because a disabled pack loads nothing.)

- [ ] **Step 2: Build the harness to see it fail**

Expected: `'PackLoadReport' could not be found`, `'ModTemplates' does not contain a definition for 'LoadPack'`.

- [ ] **Step 3: Write pack loading, startup loading, and the mod-manager listing**

`LOADER/src/Ruinarch.ModContent/Templates/TemplatePacks.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>What loading a template pack did.</summary>
	public sealed class PackLoadReport
	{
		public string PackId;
		/// <summary>Switched off in the mod manager (modloader.config.json): nothing loaded.</summary>
		public bool Disabled;
		public int Loaded;
		/// <summary>One line per skipped file: <c>pack/file.json: reason</c>.</summary>
		public List<string> Problems = new List<string>();
	}

	/// <summary>
	/// Template packs: a folder in Mods/ with a <c>templates/</c> folder (a data-only pack, or
	/// a code mod shipping templates). Its id is mod.json's id, else the folder name; every
	/// template id in it starts with that id and a '/'.
	/// </summary>
	internal static class TemplatePacks
	{
		internal static IEnumerable<string> PackDirectories()
		{
			string root = FrameworkLog.ModsRoot;
			return Directory.Exists(root)
				? Directory.GetDirectories(root).Where(d => Directory.Exists(Path.Combine(d, "templates"))).OrderBy(d => d, StringComparer.Ordinal)
				: Enumerable.Empty<string>();
		}

		internal static string PackId(string dir)
		{
			string json = Path.Combine(dir, "mod.json");
			try
			{
				string id = File.Exists(json) ? (string)JObject.Parse(File.ReadAllText(json))["id"] : null;
				return string.IsNullOrEmpty(id) ? Path.GetFileName(dir) : id;
			}
			catch
			{
				return Path.GetFileName(dir);
			}
		}

		// The mod manager's switch (Mods/modloader.config.json, {"disabled":[ids]}); read
		// directly, the framework does not reference the loader.
		internal static bool IsDisabled(string id)
		{
			string config = Path.Combine(FrameworkLog.ModsRoot, "modloader.config.json");
			try
			{
				return File.Exists(config) && JObject.Parse(File.ReadAllText(config))["disabled"] is JArray list
					&& list.Any(x => string.Equals((string)x, id, StringComparison.OrdinalIgnoreCase));
			}
			catch
			{
				return false;
			}
		}

		internal static PackLoadReport Load(string dir)
		{
			PackLoadReport report = new PackLoadReport { PackId = PackId(dir) };
			if (IsDisabled(report.PackId))
			{
				report.Disabled = true;
				return report;
			}
			string templates = Path.Combine(dir, "templates");
			foreach (string file in Directory.Exists(templates) ? Directory.GetFiles(templates, "*.json").OrderBy(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>())
			{
				string name = $"{report.PackId}/{Path.GetFileName(file)}";
				try
				{
					BuildingTemplate t = TemplateJson.FromJson(File.ReadAllText(file));
					if (t.id == null || !t.id.StartsWith(report.PackId + "/", StringComparison.Ordinal))
					{
						throw new TemplateException($"id {t.id} must start with {report.PackId}/");
					}
					TemplateRegistry.Register(t, dir);
					report.Loaded++;
				}
				catch (TemplateException e)
				{
					Skipped(report, $"{name}: {e.Message}");
				}
				catch (Exception e)
				{
					Skipped(report, $"{name}: {e.GetType().Name}: {e.Message}");
				}
			}
			return report;
		}

		private static void Skipped(PackLoadReport report, string line)
		{
			report.Problems.Add(line);
			FrameworkLog.Warning("Template " + line + "; skipped.");
		}
	}
}
```

In `TemplateRegistry.Startup()`, replace the final `FrameworkLog.Info(...)` line with:

```csharp
			FrameworkLog.Info($"Templates ready: {TemplatePalette.GameTiles.Count} game tiles, {TemplatePalette.GameSprites.Count} sprites, {TemplateCatalogue.Looks().Count} building looks.");
			int packs = 0, loaded = 0, skipped = 0, off = 0;
			foreach (string dir in TemplatePacks.PackDirectories())
			{
				PackLoadReport r = TemplatePacks.Load(dir);
				packs++;
				loaded += r.Loaded;
				skipped += r.Problems.Count;
				off += r.Disabled ? 1 : 0;
			}
			FrameworkLog.Info($"Template packs: {packs} found ({off} switched off), {loaded} template(s) loaded, {skipped} skipped.");
```

Add to `ModTemplates` (after `PrefabsFor`):

```csharp
		/// <summary>Loads a template pack folder now (the framework loads every pack in Mods/
		/// at startup the same way). Problems are also written to mods.log.</summary>
		public static PackLoadReport LoadPack(string packDirectory)
		{
			Ready();
			return TemplatePacks.Load(packDirectory);
		}
```

In `LOADER/src/Ruinarch.Modding/ModLoader.cs`, after `StartContentFramework();` add:

```csharp
				RecordTemplatePacks();
```

and add the method (after `StartContentFramework`):

```csharp
		// A template pack has a mod.json and templates/ but no DLL: list it, so the mod
		// manager can switch it off (the framework reads the same disabled list).
		private static void RecordTemplatePacks()
		{
			foreach (string dir in System.IO.Directory.GetDirectories(ModsRoot))
			{
				string json = Path.Combine(dir, "mod.json");
				if (!File.Exists(json) || !System.IO.Directory.Exists(Path.Combine(dir, "templates"))
					|| System.IO.Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Length > 0)
				{
					continue;
				}
				ModInfo info = ModInfo.LoadOrDefault(json, Path.GetFileName(dir));
				bool enabled = !_disabled.Contains(info.id);
				RecordKnown(info, dir, null, enabled, loaded: enabled);
			}
		}
```

- [ ] **Step 4: Build all, install the loader, run; see it pass**

Expected:
```
PASS a template pack on disk loads; broken templates are skipped with a reason :: loaded 1; autotest-pack/broken.json: unknown tile game:No_Such_Tile | autotest-pack/notjson.json: not a valid template: ...
PASS a pack's problems are written to mods.log :: mods.log
PASS a pack's PNG becomes a floor tile :: art:red.png 64px RGBA(1.000, 0.000, 0.000, 1.000)
PASS a pack switched off in the mod manager does not load :: disabled=True loaded=0
PASS the framework reports its templates at startup :: mods.log
```
Look at `GAME/Mods/RuinarchDebug/template-png.png`: the Tavern's floor is red.
Then check the mod manager by hand: copy the exported `template-<Tavern>.json` into a pack `GAME/Mods/TestPack/{mod.json,templates/}` with id `testpack/...`, start the game, open Mods: "TestPack" is listed and can be switched off; after a restart with it off, `mods.log` says `1 found (1 switched off)`. Remove `GAME/Mods/TestPack` afterwards.

- [ ] **Step 5: Commit**

```bash
cd LOADER && git add src && git commit -m "ModContent: template packs on disk (problems in mods.log, PNG tiles, switched-off packs); loader lists packs" && git push origin master && git push gitlab master
cd MODS && git add RuinarchDebug/TemplateSuite.cs && git commit -m "Harness: TemplateSuite packs on disk" && git push origin master && git push gitlab master
```

---

### Task 6: Docs, and the full regression

**Files:**
- Create: `LOADER/docs/TEMPLATES.md`
- Modify: `LOADER/docs/CONTENT_FRAMEWORK.md` (add a "Building templates" section at the end)
- Modify: `LOADER/README.md` (the line listing the docs, near "Adding **new content**")

**Interfaces:** none (docs).

- [ ] **Step 1: Write `LOADER/docs/TEMPLATES.md`**

```markdown
# Building templates

A building template is the look of one building: its floor, details, walls, furniture,
entrances and footprint. A **template pack** is a mod folder holding templates; the game
then uses each template as one more look for its kind of building, next to its own. Your
pack needs no code and no Unity editor.

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

    BuildingTemplate t = ModTemplates.Export(prefab, "mybuildings/stone-tavern", STRUCTURE_TYPE.TAVERN, FACTION_TYPE.Human_Empire, RESOURCE.STONE);
    File.WriteAllText(path, ModTemplates.ToJson(t));

`ModTemplates.GameLooks()` lists every game building (kind, culture, material, prefab).

## The template file

| Field | Meaning |
|---|---|
| `formatVersion` | `1`. |
| `id` | `pack-id/template-id`, stable. |
| `name` | Shown to players. |
| `kind` | The building it is a look for: a game kind (`TAVERN`, `DWELLING`, ...) or a mod's registered id (`ruinarch.plus.library`). Demonic buildings cannot have templates. |
| `cultures` | Cultures that use it: `Human_Empire`, `Elven_Kingdom`, ... (`None` for buildings without a culture). |
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
| `clickBox` | The click area (`offset`, `size`); leave out for a box over the footprint. |

Positions are in the building's own units: one unit is one tile.

## Your own art

PNG files in `art/` become tiles and sprites at the game's 64 pixels per tile. Name them in
the palette as `art:file.png`, and in objects as `"sprite": "art:file.png"`.

## Problems

A template that cannot be used is skipped, and `Mods/mods.log` says which file and why:

    [WARN] [ModContent] Template mybuildings/tavern.json: unknown tile game:Floor_Wodo; skipped.

At startup, `mods.log` lists what loaded:

    [INFO] [ModContent] Template packs: 2 found (0 switched off), 5 template(s) loaded, 1 skipped.
```

- [ ] **Step 2: Link it**

In `LOADER/docs/CONTENT_FRAMEWORK.md`, append:

```markdown
## Building templates

New looks for village and special buildings, from data: see [TEMPLATES.md](TEMPLATES.md).
The framework builds each template on a copy of a game building at the start of the first
game scene, registers it as an object pool (`<base prefab>@<template id>`, the name saves
record) and appends it to the game's list for its kind, culture and material
(`StructureData.GetStructurePrefabs`).
```

In `LOADER/README.md`, after the sentence pointing to `docs/ASSETS_AND_CONTENT.md`, add:
`New looks for the game's buildings, as data-only template packs, are covered in [docs/TEMPLATES.md](docs/TEMPLATES.md).`

- [ ] **Step 3: Full regression**

Deploy both mods and run two full harness regressions (`./tools/run-autotest.sh 3000`, twice, fresh worlds). Expected: `TemplateSuite` passes in both, no `GAME-EXCEPTION`, and no other suite fails because of templates. The `autotest/` templates the suite registers exist only in memory for that session.

- [ ] **Step 4: Commit**

```bash
cd LOADER && git add docs README.md && git commit -m "docs: building templates (TEMPLATES.md)" && git push origin master && git push gitlab master
```
