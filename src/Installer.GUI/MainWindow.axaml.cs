using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ruinarch.Modding.Patcher;
using Core = Ruinarch.Modding.Patcher.Installer;

namespace Ruinarch.Modding.Installer
{
	public partial class MainWindow : Window
	{
		private readonly string[] _srcDirs =
		{
			AppContext.BaseDirectory,
			Directory.GetCurrentDirectory(),
		};

		public MainWindow()
		{
			InitializeComponent();
			PathBox.TextChanged += (_, _) => RefreshStatus();
			Opened += (_, _) => AutoDetect();
		}

		private void AutoDetect()
		{
			string? found = SteamLocator.FindRuinarch();
			if (found != null)
			{
				PathBox.Text = found;
				Log("Found Ruinarch: " + found);
			}
			else
			{
				Log("Could not auto-detect Ruinarch. Click Browse and point at your install (the folder with Ruinarch.exe).");
			}
			RefreshStatus();
		}

		private void OnDetect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AutoDetect();

		private async void OnBrowse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var top = GetTopLevel(this);
			if (top is null)
			{
				return;
			}
			var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = "Select your Ruinarch install folder",
				AllowMultiple = false,
			});
			if (picked.Count > 0)
			{
				string? path = picked[0].TryGetLocalPath();
				if (!string.IsNullOrEmpty(path))
				{
					PathBox.Text = path;
				}
			}
		}

		private async void OnInstall(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			string game = (PathBox.Text ?? "").Trim();
			if (Core.ResolveManagedDir(game) == null)
			{
				Log("No Ruinarch install at this path. Fix the folder first.");
				return;
			}
			SetBusy(true);
			Log("--- Installing ---");
			bool ok = await Task.Run(() => Core.Install(game, force: false, Log, _srcDirs));
			Log(ok ? "Done." : "Install failed.");
			SetBusy(false);
			RefreshStatus();
		}

		private async void OnUninstall(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			string game = (PathBox.Text ?? "").Trim();
			SetBusy(true);
			Log("--- Uninstalling ---");
			bool ok = await Task.Run(() => Core.Uninstall(game, Log));
			Log(ok ? "Done." : "Uninstall failed.");
			SetBusy(false);
			RefreshStatus();
		}

		private void OnOpenMods(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			string game = (PathBox.Text ?? "").Trim();
			string? managed = Core.ResolveManagedDir(game);
			if (managed == null)
			{
				Log("No Ruinarch install at this path; cannot open the Mods folder.");
				return;
			}
			string mods = Core.ModsDirFor(managed);
			Directory.CreateDirectory(mods);
			OpenInFileManager(mods);
		}

		private void RefreshStatus()
		{
			string game = (PathBox.Text ?? "").Trim();
			if (Core.ResolveManagedDir(game) == null)
			{
				StatusText.Text = "Status: no Ruinarch install found at this path.";
				InstallBtn.IsEnabled = false;
				UninstallBtn.IsEnabled = false;
				ModsBtn.IsEnabled = false;
				return;
			}
			bool installed = Core.IsInstalled(game);
			StatusText.Text = installed
				? "Status: mod loader is INSTALLED."
				: "Status: mod loader is not installed.";
			InstallBtn.Content = installed ? "Reinstall / Repair" : "Install";
			InstallBtn.IsEnabled = true;
			UninstallBtn.IsEnabled = installed;
			ModsBtn.IsEnabled = true;
		}

		private void SetBusy(bool busy)
		{
			InstallBtn.IsEnabled = !busy;
			UninstallBtn.IsEnabled = !busy;
			DetectBtn.IsEnabled = !busy;
			BrowseBtn.IsEnabled = !busy;
		}

		private void Log(string message)
		{
			void Append()
			{
				LogBox.Text += (LogBox.Text?.Length > 0 ? "\n" : "") + message;
				LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
			}
			if (Dispatcher.UIThread.CheckAccess())
			{
				Append();
			}
			else
			{
				Dispatcher.UIThread.Post(Append);
			}
		}

		private void OpenInFileManager(string path)
		{
			try
			{
				if (OperatingSystem.IsWindows())
				{
					Process.Start(new ProcessStartInfo("explorer.exe", '"' + path + '"') { UseShellExecute = true });
				}
				else if (OperatingSystem.IsMacOS())
				{
					Process.Start("open", '"' + path + '"');
				}
				else
				{
					Process.Start("xdg-open", '"' + path + '"');
				}
				Log("Opened Mods folder: " + path);
			}
			catch (Exception ex)
			{
				Log("Mods folder is at: " + path + " (" + ex.Message + ")");
			}
		}
	}
}
