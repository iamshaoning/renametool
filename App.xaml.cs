using System.IO;
using System.Windows;
using RenameTool.ViewModels;

namespace RenameTool;

public partial class App : Application
{
	public static bool IsDarkMode { get; private set; }

	private static string ThemeFilePath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenameTool", "theme.txt");

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += (_, args) =>
		{
			MessageBox.Show(args.Exception.Message, "发生错误", MessageBoxButton.OK, MessageBoxImage.Error);
			args.Handled = true;
		};

		bool dark = IsSystemDark();
		try
		{
			if (File.Exists(ThemeFilePath))
				bool.TryParse(File.ReadAllText(ThemeFilePath).Trim(), out dark);
		}
		catch { /* 忽略 */ }
		SetDarkMode(dark);

		var vm = new MainViewModel();
		var window = new MainWindow { DataContext = vm };
		MainWindow = window;
		window.Show();
	}

	public static void SetDarkMode(bool dark)
	{
		IsDarkMode = dark;
		if (Current is not App app) return;
		var source = dark ? "Themes/ColorsDark.xaml" : "Themes/ColorsLight.xaml";
		var palette = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
		var merged = app.Resources.MergedDictionaries;
		bool replaced = false;
		for (int i = 0; i < merged.Count; i++)
		{
			var src = merged[i].Source?.OriginalString ?? "";
			if (src.Contains("Colors", StringComparison.OrdinalIgnoreCase))
			{
				merged[i] = palette;
				replaced = true;
				break;
			}
		}
		if (!replaced) merged.Insert(0, palette);
		try
		{
			var dir = Path.GetDirectoryName(ThemeFilePath)!;
			Directory.CreateDirectory(dir);
			File.WriteAllText(ThemeFilePath, dark.ToString());
		}
		catch { /* 忽略写入失败 */ }
	}

	private static bool IsSystemDark()
	{
		try
		{
			using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
				@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
			if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
		}
		catch { /* 忽略 */ }
		return false;
	}
}
