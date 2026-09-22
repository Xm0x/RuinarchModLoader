using System.IO;
using UnityEditor;
using UnityEngine;

// Drop this file into a Unity 2020.3.20f1 project under Assets/Editor/.
// It adds a "Ruinarch" menu that builds every AssetBundle defined in the project
// for the game's runtime platform (the Windows build, run directly or via Proton).
//
// Usage:
//   1. Assign each asset to a bundle (Inspector, bottom "AssetBundle" dropdown),
//      e.g. name the bundle "ruinarchplus".
//   2. Menu: Ruinarch > Build AssetBundles (Windows64).
//   3. The .bundle files land in <project>/AssetBundles/StandaloneWindows64/.
//   4. Ship a .bundle next to your mod and load it at runtime with
//      AssetBundle.LoadFromFile(...). See docs/ASSETS_AND_CONTENT.md.
//
// The build target is StandaloneWindows64 because Ruinarch ships as the Windows
// build; a bundle must match the platform of the game process that loads it.
public static class BundleBuilder
{
	private const string OutputSubdir = "AssetBundles/StandaloneWindows64";

	[MenuItem("Ruinarch/Build AssetBundles (Windows64)")]
	public static void BuildWindows64()
	{
		Build(BuildTarget.StandaloneWindows64);
	}

	private static void Build(BuildTarget target)
	{
		string projectRoot = Directory.GetParent(Application.dataPath).FullName;
		string outDir = Path.Combine(projectRoot, OutputSubdir);
		Directory.CreateDirectory(outDir);

		string[] names = AssetDatabase.GetAllAssetBundleNames();
		if (names.Length == 0)
		{
			Debug.LogError("[BundleBuilder] No AssetBundles defined. Assign at least one asset " +
				"to a bundle (Inspector > bottom AssetBundle dropdown) before building.");
			return;
		}

		// ChunkBasedCompression = LZ4: fast to load, streamable, small enough. The stock
		// game loads its own bundles the same way.
		AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
			outDir,
			BuildAssetBundleOptions.ChunkBasedCompression,
			target);

		if (manifest == null)
		{
			Debug.LogError("[BundleBuilder] Build returned no manifest. Nothing was built.");
			return;
		}

		string[] built = manifest.GetAllAssetBundles();
		Debug.Log($"[BundleBuilder] Built {built.Length} bundle(s) for {target} -> {outDir}\n" +
			string.Join("\n", built));
		EditorUtility.RevealInFinder(outDir);
	}
}
