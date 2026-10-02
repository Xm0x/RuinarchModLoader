using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

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
	/// template packs. See docs/TEMPLATES.md. Authoring needs TemplateAuthoring.Prepare()
	/// at the menu or a loaded game scene; registering variants needs a loaded game scene.
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
			WorldReady();
			Ready();
			return TemplateRegistry.Register(t, packDirectory);
		}

		/// <summary>The prefabs a registered template was built into.</summary>
		public static IReadOnlyList<GameObject> PrefabsFor(string templateId)
		{
			return TemplateRegistry.PrefabsFor(templateId);
		}

		/// <summary>Loads a template pack folder now. At startup the framework loads every
		/// pack the loader accepted the same way. Problems are also written to mods.log.</summary>
		public static PackLoadReport LoadPack(string packDirectory)
		{
			WorldReady();
			Ready();
			return TemplatePacks.Load(packDirectory);
		}

		/// <summary>Package directories the loader accepted (compatible and enabled) for
		/// template loading this session, local and Steam Workshop.</summary>
		public static IReadOnlyList<string> PackDirectories => TemplatePacks.PackDirectories;

		/// <summary>Called by the loader, by reflection, before the first world is made.</summary>
		public static void SetPackDirectories(string[] directories)
		{
			TemplatePacks.SetPackDirectories(directories);
		}

		private static void Ready()
		{
			if (LandmarkManager.Instance == null && !TemplateCatalogue.HasLooks)
			{
				throw new TemplateException("building assets are not ready: run TemplateAuthoring.Prepare at the menu, or call in-game");
			}
			TemplatePalette.Gather();
		}

		private static void WorldReady()
		{
			if (LandmarkManager.Instance == null || TemplateAuthoring.Loading)
				throw new TemplateException("registering templates needs a live game scene, not an authoring preload");
		}
	}
}
