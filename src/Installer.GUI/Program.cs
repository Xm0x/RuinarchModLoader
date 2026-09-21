using System;
using Avalonia;

namespace Ruinarch.Modding.Installer
{
	internal static class Program
	{
		// Avalonia needs an STA thread and to be configured before any control is
		// touched, so keep this entry point minimal.
		[STAThread]
		public static void Main(string[] args) =>
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

		public static AppBuilder BuildAvaloniaApp() =>
			AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.WithInterFont()
				.LogToTrace();
	}
}
