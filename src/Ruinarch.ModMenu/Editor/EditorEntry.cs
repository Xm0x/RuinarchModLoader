using System;
using System.Collections;
using HarmonyLib;
using Ruinarch.ModContent.Templates;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ruinarch.ModMenu.Editor
{
	[HarmonyPatch(typeof(MainMenuUI), "ShowMenuButtons")]
	internal static class EditorEntry
	{
		private static void Postfix(MainMenuUI __instance)
		{
			try
			{
				var newGame = AccessTools.Field(typeof(MainMenuUI), "newGameButton").GetValue(__instance) as Button;
				Transform buttons = newGame?.transform.parent, mods = buttons?.Find("ModsBtn"), exit = buttons?.Find("ExitBtn");
				if (mods == null || exit == null) { ModMenuMod.Log?.Warning("Cannot add building editor: the main menu has no Mods or Exit button."); return; }
				if (buttons.Find("EditorBtn") == null)
				{
					// The menu's buttons sit at hand-placed positions, not in a layout group:
					// Editor takes Exit's row and Exit moves down by the same step.
					var modsRect = (RectTransform)mods; var exitRect = (RectTransform)exit;
					Vector2 step = new Vector2(0, exitRect.anchoredPosition.y - modsRect.anchoredPosition.y);
					var go = UnityEngine.Object.Instantiate(mods.gameObject, buttons, false); go.name = "EditorBtn";
					go.transform.SetSiblingIndex(mods.GetSiblingIndex() + 1);
					((RectTransform)go.transform).anchoredPosition = new Vector2((modsRect.anchoredPosition.x + exitRect.anchoredPosition.x) / 2, exitRect.anchoredPosition.y);
					exitRect.anchoredPosition += step;
					var label = go.GetComponentInChildren<TMP_Text>(true);
					if (label != null)
					{
						// The localization component would set the copied label back to "Mods".
						foreach (Component c in label.GetComponents<Component>()) if (c.GetType().Name == "CustomLocalizeStringEvent") UnityEngine.Object.DestroyImmediate(c);
						label.text = "Editor";
					}
					var button = go.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.interactable = true;
					button.onClick.AddListener(EditorHost.Open); go.SetActive(true);
				}
				if (EditorWorldTest.Returning) { EditorWorldTest.Returning = false; EditorHost.Open(); }
			}
			catch (Exception e) { ModMenuMod.Log?.Error("Cannot add building editor: " + e); }
		}
	}

	internal sealed class EditorHost : MonoBehaviour
	{
		internal static EditorHost Instance;
		internal static EditorPack RetainedPack;
		internal static EditorDocument RetainedDocument;
		internal static void Open()
		{
			if (Instance == null)
			{
				var host = new GameObject("Building template editor host"); DontDestroyOnLoad(host); Instance = host.AddComponent<EditorHost>();
			}
			if (EditorView.IsOpen) return;
			TMP_Text font = MainMenuUI.Instance?.GetComponentInChildren<TMP_Text>(true);
			if (font != null) { EditorUI.Font = font.font; EditorUI.FontMaterial = font.fontSharedMaterial; }
			Instance.StartCoroutine(Instance.Prepare());
		}
		private IEnumerator Prepare()
		{
			EditorView.Loading("Loading building assets... No world will be generated.");
			var prepare = TemplateAuthoring.Prepare();
			Exception failure = null;
			while (true)
			{
				bool next = false; object current = null;
				try { next = prepare.MoveNext(); if (next) current = prepare.Current; }
				catch (Exception e) { failure = e; }
				if (!next || failure != null) break;
				yield return current;
			}
			if (failure != null) { EditorView.LoadFailed(failure.Message); yield break; }
			if (RetainedDocument != null && RetainedPack != null) EditorView.Edit(RetainedPack, RetainedDocument);
			else EditorView.StartScreen();
		}
	}
}
