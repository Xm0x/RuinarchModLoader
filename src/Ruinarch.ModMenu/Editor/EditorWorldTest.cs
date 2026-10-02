using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Player_Input;
using Ruinarch.ModContent.Templates;
using TMPro;
using UnityEngine;

namespace Ruinarch.ModMenu.Editor
{
	internal sealed class EditorWorldTest : MonoBehaviour
	{
		internal static bool Returning;
		private static EditorWorldTest _running;
		private EditorPack _pack;
		private BuildingTemplate _template;
		private WorldSettingsData _previousSettings;
		private GameObject _overlay, _prefab;
		private Component _pool;
		private TextMeshProUGUI _result;
		private NPCSettlement _village;
		private LocationStructureObject _placed;
		private LocationGridTile _origin;
		private int _placements;

		internal static void Begin(EditorPack pack, EditorDocument doc)
		{
			if (_running != null) throw new TemplateException("A building test is already running.");
			_running = EditorHost.Instance.gameObject.AddComponent<EditorWorldTest>();
			_running._pack = pack; _running._template = ModTemplates.FromJson(ModTemplates.ToJson(doc.Template));
			_running._previousSettings = WorldSettings.Instance.worldSettingsData;
			EditorView.Close(); _running.StartCoroutine(_running.Safe(_running.StartWorld()));
		}
		private IEnumerator Safe(IEnumerator root)
		{
			var stack = new Stack<IEnumerator>(); stack.Push(root);
			while (stack.Count > 0)
			{
				IEnumerator current = stack.Peek(); bool next = false; object value = null; Exception failure = null;
				try { next = current.MoveNext(); if (next) value = current.Current; } catch (Exception e) { failure = e; }
				if (failure != null) { ShowOverlay("Test failed: " + failure.Message); ModMenuMod.Log?.Error("Editor test: " + failure); yield break; }
				if (!next) { stack.Pop(); continue; }
				if (value is IEnumerator nested) stack.Push(nested); else yield return value;
			}
		}
		private IEnumerator Wait(Func<bool> ready, float seconds, string what)
		{
			float deadline = Time.realtimeSinceStartup + seconds;
			while (!ready()) { if (Time.realtimeSinceStartup >= deadline) throw new TemplateException("Timed out waiting for " + what + "."); yield return null; }
		}
		private IEnumerator StartWorld()
		{
			var data = new WorldSettingsData(); data.SetDefaultSettings(); data.ApplyCustomWorldSettings(); data.mapSettings.SetMapSize(MAP_SIZE.Small);
			FactionTemplate faction = data.factionSettings.AddFactionSetting(1);
			FACTION_TYPE culture = _template.cultures.Select(c => (FACTION_TYPE)Enum.Parse(typeof(FACTION_TYPE), c)).FirstOrDefault(c => c != FACTION_TYPE.None);
			faction.SetFactionType(culture == FACTION_TYPE.None ? FACTION_TYPE.Human_Empire : culture);
			WorldSettings.Instance.SetWorldSettingsData(data); WorldSettings.Instance.Close();
			SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(string.Empty);
			MainMenuManager.Instance.StartGame();
			yield return Wait(() => UIManager.Instance?.initialWorldSetupMenu?.pickPortalMessage != null
				&& UIManager.Instance.initialWorldSetupMenu.pickPortalMessage.gameObject.activeInHierarchy && GridMap.Instance?.mainRegion?.settlementsInRegion != null && PlayerManager.pickPortalInputModule != null, 900, "the small test world");
			PlacePortal();
			yield return Wait(() => PlayerManager.Instance?.player?.playerSettlement != null, 30, "the portal");
			if (!GameManager.Instance.gameHasStarted)
			{
				UIManager.Instance.initialWorldSetupMenu.OnClickConfigureLoadOut();
				yield return null;
				if (!GameManager.Instance.gameHasStarted) UIManager.Instance.initialWorldSetupMenu.loadOutMenu.OnClickContinue();
			}
			yield return Wait(() => GameManager.Instance.gameHasStarted, 30, "loadout selection");
			var introduction = AccessTools.Field(typeof(UIManager), "popUpScreensUI").GetValue(UIManager.Instance) as PopUpScreensUI;
			if (introduction != null && introduction.IsShowingStartScreen())
			{
				introduction.OnClickStartGameButton();
				yield return Wait(() => !introduction.IsShowingStartScreen(), 10, "the introduction to close");
			}
			GameManager.Instance.SetPausedState(true);
			BuildTestPrefab();
			yield return Place();
		}
		private void PlacePortal()
		{
			LocationStructureObject portal = InnerMapManager.Instance.GetStructurePrefabsForStructure(FACTION_TYPE.Demons, STRUCTURE_TYPE.THE_PORTAL, RESOURCE.NONE).First().GetComponent<LocationStructureObject>();
			List<LocationGridTile> centers = GridMap.Instance.mainRegion.settlementsInRegion.SelectMany(s => s.areas).Select(a => a.gridTileComponent.centerGridTile).Where(t => t != null).ToList();
			foreach (Area area in GridMap.Instance.mainRegion.areas.Where(a => a.gridTileComponent.centerGridTile != null).OrderByDescending(a => centers.Count == 0 ? 0f : centers.Min(t => t.GetDistanceTo(a.gridTileComponent.centerGridTile))))
			{
				var tile = area.gridTileComponent.centerGridTile;
				if (!portal.HasEnoughSpaceIfPlacedOn(tile, out string _) || !area.structureComponent.CanBuildDemonicStructureHere(STRUCTURE_TYPE.THE_PORTAL, out string _)) continue;
				AccessTools.Method(typeof(PickPortalInputModule), "PlacePortal").Invoke(PlayerManager.pickPortalInputModule, new object[] { tile }); return;
			}
			throw new TemplateException("The test world has no free portal location.");
		}
		private void BuildTestPrefab()
		{
			STRUCTURE_TYPE kind = TemplateAuthoring.Kind(_template.kind) ?? throw new TemplateException("Unknown building kind " + _template.kind);
			GameLook look = TemplateAuthoring.Looks().FirstOrDefault(l => l.Prefab.name == _template.behavesLike)
				?? TemplateAuthoring.Looks().FirstOrDefault(l => l.Kind == kind && l.Material.ToString() == _template.material);
			if (look == null) throw new TemplateException("No base prefab for this building.");
			_prefab = ModTemplates.BuildDetached(_template, look.Prefab, _pack.Directory);
			_prefab.name += "~editor-" + Guid.NewGuid().ToString("N");
			_pool = AccessTools.Method(typeof(ObjectPoolManager), "CreateNewPool").Invoke(ObjectPoolManager.Instance, new object[] { _prefab, _prefab.name, 0, true, true, false }) as Component;
		}
		private IEnumerator Place()
		{
			Region region = GridMap.Instance.mainRegion; var templateObject = _prefab.GetComponent<LocationStructureObject>();
			STRUCTURE_TYPE kind = TemplateAuthoring.Kind(_template.kind).Value;
			bool villageBuilding = kind.IsVillageStructure();
			List<NPCSettlement> villages = region.settlementsInRegion.OfType<NPCSettlement>().Where(v => v.owner != null && v.cityCenter != null).ToList();
			_village = villages.Where(v => !villageBuilding || _template.cultures.Contains("None") || _template.cultures.Contains(v.owner.factionType.type.ToString())).OrderBy(v => Vector3.Distance(v.cityCenter.GetCenterTile().centeredWorldLocation, InnerMapCameraMove.Instance.transform.position)).FirstOrDefault();
			if (_village == null) throw new TemplateException("No village matches the template cultures in this test world.");
			_origin = _village.cityCenter.passableTiles.FirstOrDefault(t => t.IsPassable()) ?? _village.cityCenter.GetCenterTile();
			IEnumerable<Area> areas = villageBuilding ? _village.areas.Concat(_village.areas.SelectMany(a => a.neighbourComponent.neighbours)).Distinct() : region.areas;
			var spots = areas.Where(a => a != null && (villageBuilding || a.GetFirstNPCSettlementOnArea() == null))
				.SelectMany(a => a.gridTileComponent.gridTiles).Where(t => t != null && t.structure is Wilderness && t.IsPassable())
				.OrderBy(t => t.GetDistanceTo(_origin));
			LocationGridTile spot = spots.FirstOrDefault(t => templateObject.HasEnoughSpaceIfPlacedOn(t, out string _)
				&& templateObject.connectors.All(c => t.parentMap.GetTileFromWorldPosition(t.centeredWorldLocation + _prefab.transform.InverseTransformPoint(c.transform.position)) != null));
			if (spot == null) throw new TemplateException("No free footprint for this template. Reduce its size or test another look.");
			BaseSettlement settlement = villageBuilding ? (BaseSettlement)_village : LandmarkManager.Instance.CreateNewSettlement(region, LOCATION_TYPE.DUNGEON, spot.area);
			LocationStructure structure = LandmarkManager.Instance.PlaceIndividualBuiltStructureForSettlement(settlement, region.innerMap, spot, _prefab.name);
			_placed = (structure as ManMadeStructure)?.structureObj ?? (structure as NaturalStructureWithStructureObject)?.structureObj;
			if (_placed == null) throw new TemplateException("The native placement did not create a building object.");
			_placements++; InnerMapCameraMove.Instance.CenterCameraOn(_placed.gameObject, true);
			// Native placement queues graph updates. Allow them to settle before measuring paths.
			yield return new WaitForSecondsRealtime(1);
			ShowOverlay(CheckPaths());
		}
		private bool Reach(LocationGridTile tile) { return tile != null && tile.IsPassable() && PathfindingManager.Instance.HasPathEvenDiffRegion(_origin, tile); }
		private string CheckPaths()
		{
			var failures = new List<string>(); int doors = 0, floor = 0, furniture = 0;
			foreach (var entrance in _placed.connectors)
			{
				if (Reach(entrance.tileLocation)) doors++; else failures.Add("Entrance is unreachable or outside the map: " + entrance.transform.position);
			}
			int[] b = _template.bounds;
			var ground = AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(_placed) as UnityEngine.Tilemaps.Tilemap;
			for (int row = 0; row < _template.floor.Count; row++) for (int x = 0; x < _template.floor[row].Length; x++)
			{
				if (_template.floor[row][x] == '.') continue;
				Vector3 position = ground.GetCellCenterWorld(new Vector3Int(b[0] + x, b[1] + b[3] - 1 - row, 0));
				var tile = GridMap.Instance.mainRegion.innerMap.GetTileFromWorldPosition(position);
				if (Reach(tile)) floor++; else failures.Add("Floor is blocked or unreachable: " + (b[0] + x) + "," + (b[1] + b[3] - 1 - row));
			}
			foreach (TemplateObject o in _template.objects)
			{
				var tile = GridMap.Instance.mainRegion.innerMap.GetTileFromWorldPosition(_placed.transform.TransformPoint(new Vector3(o.pos[0], o.pos[1], o.pos[2])));
				if (Reach(tile) || (tile != null && tile.neighbourList.Any(t => t.structure == tile.structure && Reach(t)))) furniture++;
				else failures.Add(o.type + " has no reachable interaction cell.");
			}
			return _template.name + " / placement " + _placements + "\nFrom " + _village.name + " village center (native walking graph)\n"
				+ "Entrances: " + doors + "/" + _template.entrances.Count + "\nFloor cells: " + floor + "/" + _template.floor.Sum(r => r.Count(c => c != '.')) + "\nFurniture access: " + furniture + "/" + _template.objects.Count
				+ (failures.Count == 0 ? "\nAll checked paths are reachable." : "\n" + string.Join("\n", failures.Take(8)) + (failures.Count > 8 ? "\n" + (failures.Count - 8) + " more blocked cells." : ""));
		}
		private void ShowOverlay(string result)
		{
			if (_overlay == null)
			{
				_overlay = EditorUI.Root("Building test overlay"); _overlay.GetComponent<UnityEngine.UI.Image>().color = Color.clear; _overlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
				var panel = EditorUI.Column("Test results", _overlay.transform, 18); EditorUI.Rect(panel, new Vector2(1, 1), Vector2.one, new Vector2(-470, -470), new Vector2(-18, -18));
				Transform report = EditorUI.Scroll(panel.transform, "Path report");
				_result = EditorUI.Label(report, "", 17); _result.alignment = TextAlignmentOptions.TopLeft;
				EditorUI.Button(panel.transform, "Check paths again", () => SetReport(_placed == null ? "No building was placed." : CheckPaths()));
				EditorUI.Button(panel.transform, "Place again", () => { if (_prefab != null) StartCoroutine(Safe(Place())); });
				EditorUI.Button(panel.transform, "Back to editor", Return);
			}
			SetReport(result);
		}
		private void SetReport(string result)
		{
			_result.text = result;
			_result.gameObject.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = Mathf.Max(100, _result.GetPreferredValues(result, 412, 10000).y + 20);
		}
		private void Return()
		{
			StopAllCoroutines(); Returning = true;
			if (WorldSettings.Instance != null && _previousSettings != null) WorldSettings.Instance.SetWorldSettingsData(_previousSettings);
			if (_overlay != null) Destroy(_overlay);
			DOTween.Clear(true); LevelLoaderManager.Instance.UpdateLoadingInfo(string.Empty); LevelLoaderManager.Instance.LoadLevel("MainMenu", true);
			StartCoroutine(CleanupAfterReturn());
		}
		private IEnumerator CleanupAfterReturn()
		{
			yield return Wait(() => MainMenuUI.Instance != null && GameManager.Instance == null, 60, "the main menu");
			if (_prefab != null)
			{
				var pools = AccessTools.Field(typeof(ObjectPoolManager), "allObjectPools").GetValue(ObjectPoolManager.Instance) as IDictionary;
				pools?.Remove(_prefab.name.ToUpperInvariant()); if (_pool != null) Destroy(_pool.gameObject); ModTemplates.DestroyDetached(_prefab);
			}
			_running = null; Destroy(this);
		}
	}
}
