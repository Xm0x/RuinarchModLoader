using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Ruinarch.Modding;
using Steamworks;
using UnityEngine;

namespace Ruinarch.ModMenu
{
	// The game initializes Steam in SteamManager.Awake and announces it from Start. After
	// that, subscribed and fully installed Workshop items are handed to the loader, which
	// validates them exactly like local packages. Runs once per launch, before any world.
	[HarmonyPatch(typeof(SteamManager), "Start")]
	internal static class Patch_SteamStarted
	{
		private static void Postfix()
		{
			try
			{
				Workshop.ScanInstalled();
			}
			catch (Exception e)
			{
				ModMenuMod.Log?.Error("Workshop scan failed: " + e);
			}
		}
	}

	internal static class Workshop
	{
		internal const uint AppId = 909320;
		internal const string Tag = "RuinarchModLoader";
		internal const string BrowseUrl = "https://steamcommunity.com/app/909320/workshop/";

		/// <summary>Installed items Steam is still downloading or updating; they need a restart.</summary>
		internal static int Pending { get; private set; }

		internal static bool Ready
		{
			get
			{
				try { return SteamManager.Initialized; }
				catch { return false; }
			}
		}

		internal static void ScanInstalled()
		{
			if (!Ready)
			{
				ModMenuMod.Log?.Info("Steam is not available; Workshop packages were not scanned.");
				return;
			}
			uint count = SteamUGC.GetNumSubscribedItems();
			var ids = new PublishedFileId_t[count];
			if (count > 0) SteamUGC.GetSubscribedItems(ids, count);
			var folders = new Dictionary<ulong, string>();
			foreach (PublishedFileId_t id in ids)
			{
				var state = (EItemState)SteamUGC.GetItemState(id);
				bool busy = (state & (EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) != 0;
				if ((state & EItemState.k_EItemStateInstalled) == 0 || busy)
				{
					Pending++;
					continue;
				}
				if (SteamUGC.GetItemInstallInfo(id, out ulong _, out string folder, 1024, out uint _) && Directory.Exists(folder))
				{
					folders[id.m_PublishedFileId] = folder;
				}
			}
			ModMenuMod.Log?.Info($"Steam Workshop: {count} subscribed item(s), {folders.Count} installed, {Pending} still downloading or updating (restart once Steam finishes).");
			ModLoader.LoadWorkshopPackages(folders);
		}

		internal static void Browse()
		{
			if (Ready && SteamUtils.IsOverlayEnabled())
			{
				SteamFriends.ActivateGameOverlayToWebPage(BrowseUrl);
			}
			else
			{
				Application.OpenURL("steam://url/SteamWorkshopPage/" + AppId);
			}
		}

		internal static void OpenItem(ulong id)
		{
			string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
			if (Ready && SteamUtils.IsOverlayEnabled()) SteamFriends.ActivateGameOverlayToWebPage(url);
			else Application.OpenURL("steam://url/CommunityFilePage/" + id);
		}
	}

	/// <summary>One upload of a local package: create (when no item id) then submit.</summary>
	internal sealed class WorkshopUpload
	{
		private static WorkshopUpload _current;
		// Created once and reused: Steam calls back into these after native work completes,
		// so they must never be collected or freed while the game runs.
		private static CallResult<CreateItemResult_t> _create;
		private static CallResult<SubmitItemUpdateResult_t> _submit;
		private UGCUpdateHandle_t _handle = UGCUpdateHandle_t.Invalid;
		private readonly KnownMod _package;
		private readonly ERemoteStoragePublishedFileVisibility? _visibility;
		private readonly string _note;
		private readonly Action<string> _status;

		internal static bool Busy => _current != null;
		internal ulong ItemId { get; private set; }

		private WorkshopUpload(KnownMod package, ulong itemId, ERemoteStoragePublishedFileVisibility? visibility, string note, Action<string> status)
		{
			_package = package; ItemId = itemId; _visibility = visibility; _note = note; _status = status;
		}

		/// <summary>Starts an upload. <paramref name="itemId"/> 0 creates a new item, which then
		/// needs an explicit <paramref name="visibility"/> (default Private). Null keeps an existing item's visibility.</summary>
		internal static void Start(KnownMod package, ulong itemId, ERemoteStoragePublishedFileVisibility? visibility, string note, Action<string> status)
		{
			if (_current != null) throw new InvalidOperationException("An upload is already running.");
			if (!Workshop.Ready) throw new InvalidOperationException("Steam is not available. Start the game through Steam to upload.");
			if (package == null || package.Origin != ModOrigin.Local) throw new InvalidOperationException("Choose a local package to upload.");
			// Validate again: the folder may have changed since the game started.
			KnownMod again = PackageInspector.Inspect(package.Directory);
			if (!again.Compatible) throw new InvalidOperationException("Not compatible with RuinarchModLoader: " + again.RejectionReason);
			if (again.Id != package.Id) throw new InvalidOperationException("The package id changed on disk; restart the game first.");
			if (itemId == 0 && visibility == null) visibility = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate;
			_current = new WorkshopUpload(again, itemId, visibility, note, status);
			_current.Begin();
		}

		/// <summary>Bytes processed and total while Steam uploads; false when not uploading.</summary>
		internal static bool Progress(out ulong done, out ulong total, out EItemUpdateStatus stage)
		{
			done = total = 0; stage = EItemUpdateStatus.k_EItemUpdateStatusInvalid;
			if (_current == null || _current._handle == UGCUpdateHandle_t.Invalid) return false;
			stage = SteamUGC.GetItemUpdateProgress(_current._handle, out done, out total);
			return true;
		}

		private void Begin()
		{
			if (ItemId != 0)
			{
				Submit();
				return;
			}
			_status("Creating a new Workshop item...");
			_create = _create ?? CallResult<CreateItemResult_t>.Create();
			_create.Set(SteamUGC.CreateItem(new AppId_t(Workshop.AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity), OnCreated);
		}

		private void OnCreated(CreateItemResult_t result, bool ioFailure)
		{
			if (ioFailure || result.m_eResult != EResult.k_EResultOK)
			{
				Finish($"Steam could not create the item: {(ioFailure ? "I/O failure" : result.m_eResult.ToString())}.", false);
				return;
			}
			ItemId = result.m_nPublishedFileId.m_PublishedFileId;
			ModMenuMod.Log?.Info($"Created Workshop item {ItemId} for '{_package.Id}'.");
			if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) Workshop.OpenItem(ItemId);
			Submit();
		}

		private void Submit()
		{
			var id = new PublishedFileId_t(ItemId);
			_handle = SteamUGC.StartItemUpdate(new AppId_t(Workshop.AppId), id);
			bool ok = SteamUGC.SetItemTitle(_handle, _package.Info.name)
				&& SteamUGC.SetItemDescription(_handle, _package.Info.description ?? "")
				&& SteamUGC.SetItemContent(_handle, _package.Directory)
				&& SteamUGC.SetItemTags(_handle, new List<string> { Workshop.Tag });
			string preview = Path.Combine(_package.Directory, "preview.png");
			if (ok && File.Exists(preview)) ok = SteamUGC.SetItemPreview(_handle, preview);
			if (ok && _visibility.HasValue) ok = SteamUGC.SetItemVisibility(_handle, _visibility.Value);
			if (!ok)
			{
				Finish("Steam rejected the item details. Check the package name, description and folder.", false);
				return;
			}
			_status($"Uploading '{_package.Info.name}' to item {ItemId}...");
			_submit = _submit ?? CallResult<SubmitItemUpdateResult_t>.Create();
			_submit.Set(SteamUGC.SubmitItemUpdate(_handle, string.IsNullOrWhiteSpace(_note) ? "Version " + _package.Info.version : _note), OnSubmitted);
		}

		private void OnSubmitted(SubmitItemUpdateResult_t result, bool ioFailure)
		{
			if (ioFailure || result.m_eResult != EResult.k_EResultOK)
			{
				Finish($"Upload to item {ItemId} failed: {(ioFailure ? "I/O failure" : result.m_eResult.ToString())}.", false);
				return;
			}
			string legal = result.m_bUserNeedsToAcceptWorkshopLegalAgreement
				? " Accept the Steam Workshop legal agreement on the item page before others can see it." : "";
			ModMenuMod.Log?.Info($"Uploaded '{_package.Id}' v{_package.Info.version} to Workshop item {ItemId}.");
			Finish($"Uploaded to Workshop item {ItemId}.{legal}", true);
			if (legal.Length > 0) Workshop.OpenItem(ItemId);
		}

		private void Finish(string message, bool ok)
		{
			if (!ok) ModMenuMod.Log?.Warning(message);
			_handle = UGCUpdateHandle_t.Invalid;
			_current = null;
			_status(message);
		}
	}
}
