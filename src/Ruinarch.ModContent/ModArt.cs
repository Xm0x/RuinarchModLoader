using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Runtime art pipeline for mods: decode loose image files (PNG/JPG shipped next to a
	/// mod's DLL, under the mod's own folder) into Unity <see cref="Sprite"/>s at load time.
	/// No AssetBundle and no Unity editor required - this is the "Rung 2" ladder from
	/// ASSETS_AND_CONTENT.md. Decoded sprites are cached by (path, ppu) so repeated loads
	/// are free. Every call is fully guarded: a missing file or non-image bytes yields
	/// <c>null</c>, never an exception.
	/// </summary>
	public static class ModArt
	{
		private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

		/// <summary>
		/// Decode a loose image file into a Sprite at <paramref name="pixelsPerUnit"/>.
		/// At 64 ppu a 64px sprite spans one inner-map tile (1 world unit). Point-filtered
		/// to keep the game's crisp pixel look. Returns <c>null</c> (never throws) when the
		/// path is missing or the bytes are not a valid image.
		/// </summary>
		public static Sprite LoadSprite(string absolutePath, float pixelsPerUnit = 64f, Vector2? pivot = null)
		{
			if (string.IsNullOrEmpty(absolutePath))
			{
				return null;
			}
			Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
			string key = absolutePath + "|" + pixelsPerUnit.ToString("R") + "|" + p.x + "," + p.y;
			if (_cache.TryGetValue(key, out Sprite cached))
			{
				return cached;
			}
			try
			{
				if (!File.Exists(absolutePath))
				{
					return null;
				}
				byte[] bytes = File.ReadAllBytes(absolutePath);
				Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
				if (!tex.LoadImage(bytes))
				{
					Object.Destroy(tex);
					return null;
				}
				tex.filterMode = FilterMode.Point;
				tex.wrapMode = TextureWrapMode.Clamp;
				Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), p, pixelsPerUnit);
				_cache[key] = sprite;
				return sprite;
			}
			catch
			{
				return null;
			}
		}
	}
}
