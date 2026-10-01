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
			Entry entry = new Entry();
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
			}
			// A culture-neutral variant shows in every culture that falls back to it, so any
			// cached list may be stale now (registration happens at startup, not per frame).
			Combined.Clear();
			ById[t.id] = entry;
			return entry.Prefabs.ToList();
		}

		/// <summary>The game's list for a kind (as requested, before the framework redirects a
		/// registered kind to its PrefabSource), culture and material, with this session's
		/// variants appended; the variants alone where the game has no list; null for neither.</summary>
		internal static List<GameObject> WithVariants(FACTION_TYPE culture, StructureSetting requested, List<GameObject> original)
		{
			var key = (requested.structureType, culture, requested.resource);
			if (Combined.TryGetValue(key, out var cached) && ReferenceEquals(cached.original, original))
			{
				return cached.combined;
			}
			List<GameObject> mine = VariantsFor(requested.structureType, culture, requested.resource);
			if (mine == null)
			{
				return original;
			}
			List<GameObject> combined = original == null ? mine : original.Concat(mine).ToList();
			Combined[key] = (original, combined);
			return combined;
		}

		// The culture's own variants, plus the culture-neutral ones when the game hands this
		// culture the neutral list (it has none of its own): a variant of a Human Tavern is
		// registered for None, and Human villages get it there.
		private static List<GameObject> VariantsFor(STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			Variants.TryGetValue((kind, culture, material), out List<GameObject> own);
			if (culture == FACTION_TYPE.None || !Variants.TryGetValue((kind, FACTION_TYPE.None, material), out List<GameObject> neutral)
				|| TemplateCatalogue.HasOwnList(kind, culture, material))
			{
				return own;
			}
			return own == null ? neutral : own.Concat(neutral).ToList();
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
		}
	}
}
