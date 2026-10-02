using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ruinarch.ModMenu.Editor;
using Ruinarch.Modding;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Ruinarch.ModMenu
{
	[HarmonyPatch(typeof(MainMenuUI), "ShowMenuButtons")]
	internal static class Patch_UpdateNotice
	{
		private static void Postfix(MainMenuUI __instance)
		{
			try { Updater.Show(__instance); }
			catch (Exception e) { ModMenuMod.Log?.Error("Update notice failed: " + e); }
		}
	}

	/// <summary>
	/// In-game loader updates (docs/specs/2026-10-02-in-game-updates.md). Checks a signed
	/// release manifest once per launch, downloads and verifies the files, and stages them
	/// for Ruinarch.Boot to install at the next start.
	/// </summary>
	internal sealed class Updater : MonoBehaviour
	{
		private const string DefaultSource = "https://github.com/Xm0x/RuinarchModLoader/releases/latest/download/";
		private const string PublicKey = "<RSAKeyValue><Modulus>xrbF9Cp3HPHdm5N27Lh3QzuFH6u5vlgvdf9d4PH8RcEKpvEzZYhKasY+KvSWuAO9ltqRPOi7NKqngM1LEKGw9Sson5nU8xQya3R86/ITFKiOTsKsYHxWQYJYmN7GR+1gE9sWMicCKj42athncI+AzD68481FydO5MJ1lsTxlDyltiItyiR3vTG/7gYlQIUz65qZgmdiW+ZGl8TdgWuU4rnCMJprWPuGlPP1NOAWiW8r+zTV589vOeGVXTE2BjGh+V2RRUvIhAOuWstKIXj6Tg/gRD7AdbSAJOaboLAhvn5m8G4sNs/hYd7YnBcpQJSVboMNFE3LDa6iNh0yM2nHAYoBponG53mxC56WBkuJR9heuqImZQ4lY+Qs8/JiqlkR+NdarK+X0mlr8rDq80lj2d3kjimte63TdiUnThebqO7Hf2q5EuxVdWeq+yuUdtol2Ghv0dY9l4dLWZN+MmijPboqc13NUufDSEii+2Eb9fTESdDN8u4kAlnoFX3HbIvKf</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

		private enum State { Idle, Checking, Available, Downloading, Ready, NeedsInstaller, Installed, Failed }

		private sealed class Release
		{
			internal string Version, Page;
			internal int Boot;
			internal List<(string name, string sha256, long size)> Files = new List<(string, string, long)>();
		}

		private static Updater _host;
		private static State _state;
		private static Release _release;
		private static string _message;
		private static GameObject _notice;
		private static MainMenuUI _menu;

		private static string UpdateDir => Path.Combine(Path.GetDirectoryName(ModLoader.ModsRoot), "ModLoaderUpdate");
		private static string Staged => Path.Combine(UpdateDir, "staged");

		internal static void Show(MainMenuUI menu)
		{
			_menu = menu;
			if (_host == null)
			{
				var go = new GameObject("RuinarchModLoader updater"); DontDestroyOnLoad(go); _host = go.AddComponent<Updater>();
				ReadPreviousResult();
				if (_state == State.Idle && File.Exists(Path.Combine(Staged, "apply.txt")))
				{
					_state = State.Ready; _message = File.ReadAllLines(Path.Combine(Staged, "apply.txt"))[0].Substring(8);
				}
				if (_state == State.Idle) _host.StartCoroutine(_host.Check());
			}
			Render();
		}

		// Ruinarch.Boot reports what it did with the last staged update.
		private static void ReadPreviousResult()
		{
			string file = Path.Combine(UpdateDir, "result.txt");
			if (!File.Exists(file)) return;
			string result = File.ReadAllText(file).Trim();
			File.Delete(file);
			if (result.StartsWith("updated ")) { _state = State.Installed; _message = "RuinarchModLoader was updated to " + result.Substring(8) + "."; }
			else { _state = State.Failed; _message = "The loader update could not be installed (" + result.Substring(Math.Min(7, result.Length)).Trim() + "). Your previous version is still running."; }
			ModMenuMod.Log?.Info("Update result: " + result);
		}

		private static string Source()
		{
			string file = Path.Combine(UpdateDir, "source.txt");
			string url = File.Exists(file) ? File.ReadAllText(file).Trim() : DefaultSource;
			return url.EndsWith("/") ? url : url + "/";
		}

		private static int InstalledBoot()
		{
			object value = Type.GetType("Ruinarch.Boot.Boot, Ruinarch.Boot")?.GetField("Version")?.GetRawConstantValue();
			return value is int v ? v : 0;
		}

		private IEnumerator Check()
		{
			_state = State.Checking;
			string source = Source();
			byte[] manifest = null, signature = null; string error = null;
			yield return Get(source + "update.json", b => manifest = b, e => error = e);
			if (error == null) yield return Get(source + "update.json.sig", b => signature = b, e => error = e);
			try
			{
				if (error != null) throw new InvalidDataException(error);
				if (!Verify(manifest, signature)) throw new InvalidDataException("the update manifest's signature is not valid");
				_release = Parse(manifest);
				if (System.Version.Parse(_release.Version) <= System.Version.Parse(ModLoader.Version))
				{
					_state = State.Idle;
					ModMenuMod.Log?.Info($"RuinarchModLoader {ModLoader.Version} is up to date (latest {_release.Version}).");
				}
				else
				{
					_state = _release.Boot > InstalledBoot() ? State.NeedsInstaller : State.Available;
					ModMenuMod.Log?.Info($"RuinarchModLoader {_release.Version} is available ({(_state == State.Available ? "in-game update" : "needs the installer")}).");
				}
			}
			catch (Exception e)
			{
				// A check that cannot complete is not the player's problem: log it, show nothing.
				_state = State.Idle;
				ModMenuMod.Log?.Info("Update check skipped: " + e.Message);
			}
			Render();
		}

		private static bool Verify(byte[] data, byte[] signature)
		{
			using (var rsa = new RSACryptoServiceProvider())
			{
				rsa.FromXmlString(PublicKey);
				return rsa.VerifyData(data, "SHA256", signature);
			}
		}

		private static Release Parse(byte[] manifest)
		{
			JObject json = JObject.Parse(Encoding.UTF8.GetString(manifest));
			if ((int?)json["formatVersion"] != 1) throw new InvalidDataException("unsupported manifest format");
			var release = new Release { Version = (string)json["version"], Page = (string)json["page"], Boot = (int?)json["boot"] ?? 1 };
			System.Version.Parse(release.Version);
			foreach (JObject f in json["files"])
			{
				string name = (string)f["name"];
				if (name == null || !name.EndsWith(".dll") || name == "Ruinarch.Boot.dll" || !name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
					throw new InvalidDataException("bad file name in manifest: " + name);
				release.Files.Add((name, ((string)f["sha256"]).ToLowerInvariant(), (long)f["size"]));
			}
			if (release.Files.Count == 0) throw new InvalidDataException("the manifest lists no files");
			return release;
		}

		private IEnumerator Download()
		{
			_state = State.Downloading; Render();
			string source = Source(), temp = Staged + ".tmp";
			string error = null;
			try { if (Directory.Exists(temp)) Directory.Delete(temp, true); Directory.CreateDirectory(temp); }
			catch (Exception e) { error = e.Message; }
			for (int i = 0; i < _release.Files.Count && error == null; i++)
			{
				var file = _release.Files[i];
				_message = $"Downloading {file.name} ({i + 1} of {_release.Files.Count})..."; Render();
				byte[] bytes = null;
				yield return Get(source + file.name, b => bytes = b, e => error = e);
				if (error != null) break;
				if (bytes.Length != file.size || Hash(bytes) != file.sha256) { error = file.name + " does not match the signed release"; break; }
				File.WriteAllBytes(Path.Combine(temp, file.name), bytes);
			}
			try
			{
				if (error != null) throw new InvalidDataException(error);
				File.WriteAllLines(Path.Combine(temp, "apply.txt"), new[] { "version " + _release.Version }.Concat(_release.Files.Select(f => f.sha256 + " " + f.name)));
				if (Directory.Exists(Staged)) Directory.Delete(Staged, true);
				Directory.Move(temp, Staged);
				_state = State.Ready; _message = _release.Version;
				ModMenuMod.Log?.Info($"RuinarchModLoader {_release.Version} downloaded; it installs at the next start.");
			}
			catch (Exception e)
			{
				try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
				_state = State.Failed; _message = "The update could not be downloaded: " + e.Message;
				ModMenuMod.Log?.Warning(_message);
			}
			Render();
		}

		private static IEnumerator Get(string url, Action<byte[]> done, Action<string> failed)
		{
			using (var request = UnityWebRequest.Get(url))
			{
				request.timeout = 60;
				yield return request.SendWebRequest();
				if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.data);
				else failed($"{url}: {request.error}");
			}
		}

		private static string Hash(byte[] bytes)
		{
			using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
		}

		private static void Render()
		{
			if (_notice != null) Destroy(_notice);
			_notice = null;
			if (_menu == null || _state == State.Idle || _state == State.Checking) return;
			TMP_Text font = _menu.GetComponentInChildren<TMP_Text>(true);
			if (font != null) { EditorUI.Font = font.font; EditorUI.FontMaterial = font.fontSharedMaterial; }
			_notice = EditorUI.Box("Loader update notice", _menu.transform, EditorUI.Panel);
			var rt = _notice.GetComponent<RectTransform>();
			rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, 0); rt.sizeDelta = new Vector2(1100, 58); rt.anchoredPosition = new Vector2(0, 24);
			var row = _notice.AddComponent<HorizontalLayoutGroup>();
			row.padding = new RectOffset(16, 10, 10, 10); row.spacing = 8; row.childAlignment = TextAnchor.MiddleLeft;
			row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = row.childForceExpandHeight = false;
			string text;
			switch (_state)
			{
				case State.Available: text = $"RuinarchModLoader {_release.Version} is available (you have {ModLoader.Version})."; break;
				case State.Downloading: text = _message; break;
				case State.Ready: text = $"RuinarchModLoader {_message} is downloaded. It installs the next time you start the game."; break;
				case State.NeedsInstaller: text = $"RuinarchModLoader {_release.Version} is available. This version needs the installer."; break;
				default: text = _message; break;
			}
			EditorUI.Label(_notice.transform, text, 17).GetComponent<LayoutElement>().flexibleWidth = 1;
			Action page = () => Application.OpenURL(_release?.Page ?? "https://github.com/Xm0x/RuinarchModLoader/releases/latest");
			switch (_state)
			{
				case State.Available:
					EditorUI.Button(_notice.transform, "Update", () => _host.StartCoroutine(_host.Download()), 130);
					EditorUI.Button(_notice.transform, "What's new", () => page(), 130);
					break;
				case State.Ready:
					EditorUI.Button(_notice.transform, "Quit game", Application.Quit, 130);
					break;
				case State.NeedsInstaller:
					EditorUI.Button(_notice.transform, "Download page", () => page(), 160);
					break;
				case State.Failed when _release != null:
					EditorUI.Button(_notice.transform, "Try again", () => _host.StartCoroutine(_host.Download()), 130);
					break;
			}
			if (_state != State.Downloading) EditorUI.Button(_notice.transform, "Dismiss", () => { _state = State.Idle; Render(); }, 110);
		}
	}
}
