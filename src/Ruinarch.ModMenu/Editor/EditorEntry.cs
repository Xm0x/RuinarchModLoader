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
				var source = AccessTools.Field(typeof(MainMenuUI), "newGameButton").GetValue(__instance) as Button;
				if (source == null || __instance.transform.Find("Building template editor button") != null) return;
				var go = UnityEngine.Object.Instantiate(source.gameObject, __instance.transform, false); go.name = "Building template editor button";
				var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1); rt.sizeDelta = new Vector2(200, 45); rt.anchoredPosition = new Vector2(-28, -28);
				var label = go.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = "Editor";
				var button = go.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.interactable = true;
				button.onClick.AddListener(EditorHost.Open); go.SetActive(true);
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
