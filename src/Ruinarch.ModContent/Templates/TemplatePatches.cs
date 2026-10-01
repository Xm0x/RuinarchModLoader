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
