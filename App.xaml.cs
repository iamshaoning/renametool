using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RenameTool.Services;
using RenameTool.ViewModels;

namespace RenameTool;

public partial class App : Application
{
	/// <summary>当前实际生效的深色状态（由 <see cref="ThemeSetting"/> 与系统设置共同决定）。</summary>
	public static bool IsDarkMode { get; private set; }

	/// <summary>主题模式：system（跟随系统）/ light（日间）/ dark（夜间）。
	/// 不叫 ThemeMode 是为了不与 .NET 10 起 Application 自带的同名属性相撞（CS0108）。</summary>
	public static string ThemeSetting => SettingsStore.Current.Theme;

	/// <summary>单实例互斥体名称。用 Local\ 前缀限定在当前登录会话内，多用户互不影响。</summary>
	private const string SingleInstanceMutexName = @"Local\RenameTool.SingleInstance";

	private Mutex? _instanceMutex;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		// 只允许同时运行一个实例：设置文件与历史记录是整体读写的，多实例并行会互相覆盖。
		// 第二个实例先明确告知用户「已经有一个在跑」，再把已有窗口激活到前台，然后退出。
		// 注意：这里必须先 Load 设置并 ApplyTheme——App.xaml 只合并了 Icons/Controls，
		// 调色板（Colors*.xaml）是 ApplyTheme 运行时插进去的；不调的话提示框里的
		// DynamicResource 全都取不到画刷，窗口会是一片空白。
		// SettingsStore.Load 只读文件、不写盘，不会与在跑的实例抢配置文件。
		_instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
		if (!createdNew)
		{
			SettingsStore.Load();
			ApplyTheme();
			AppDialog.Notify(owner: null, "程序已在运行",
				"检测到已有一个窗口在运行，本次启动已取消。",
				DialogIcon.Info);
			ActivateExistingInstance();
			Shutdown();
			return;
		}

		SettingsStore.Load();

		DispatcherUnhandledException += (_, args) =>
		{
			ReportError(args.Exception);
			args.Handled = true;
		};
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex) ReportError(ex);
		};

		// 跟随系统模式下，系统切换浅 / 深色时立即生效
		SystemEvents.UserPreferenceChanged += (_, _) =>
		{
			if (ThemeSetting == "system") Current?.Dispatcher.Invoke(ApplyTheme);
		};

		// 全局屏蔽键盘焦点虚线框（Up/Down/Tab 之后出现的那圈点状虚线）。
		// 注意：在 Application.Resources 里挂 SystemParameters.FocusVisualStyleKey 的空模板
		// 是无效的——Control.FocusVisualStyle 的默认值不走资源查找，必须在焦点落到元素之前
		// 把它的 FocusVisualStyle 清空。用类处理器统一处理，覆盖所有窗口与弹出层。
		EventManager.RegisterClassHandler(typeof(FrameworkElement), UIElement.PreviewGotKeyboardFocusEvent,
			new KeyboardFocusChangedEventHandler(OnPreviewGotKeyboardFocus), true);

		// ToolTip 首现延迟：本程序大量使用「图标 + 长说明」提示，鼠标划过就连环弹出很打扰，
		// 因此统一在 FrameworkElement 层级覆盖默认值，拉长到 1200ms：
		// 只有「停下鼠标、真心想看」时才弹出，路过不弹。
		// 注意 InitialShowDelay 是附加属性、挂在“使用 ToolTip 的元素”上，写进 TargetType="ToolTip"
		// 的样式里是无效的，只能在 FrameworkElement 层级覆盖默认值。
		// BetweenShowDelay 保持默认 100ms：官方语义是“上一条 ToolTip 刚关闭的窗口期内移到另一元素上
		// 立即显示”，所以连续划过多项时不会每项都等 1200ms。
		// 必须在任何 FrameworkElement 实例化之前调用。
		ToolTipService.InitialShowDelayProperty.OverrideMetadata(
			typeof(FrameworkElement), new FrameworkPropertyMetadata(1200));

		ApplyTheme();

		var vm = new MainViewModel();
		var window = new MainWindow { DataContext = vm };
		MainWindow = window;
		window.Show();
	}

	/// <summary>焦点落到任意元素前清空其焦点框样式，等于全局关闭键盘焦点虚线框。</summary>
	private static void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (e.NewFocus is FrameworkElement fe) fe.FocusVisualStyle = null;
	}

	/// <summary>把已在运行的那个实例的主窗口还原并置前；找不到窗口时静默返回。</summary>
	private static void ActivateExistingInstance()
	{
		try
		{
			string self = Environment.ProcessPath ?? "";
			foreach (var p in Process.GetProcessesByName("RenameTool"))
			{
				using (p)
				{
					// 跳过自己（理论上此时尚无窗口），并且必须是自己这一个可执行文件
					if (p.Id == Environment.ProcessId) continue;
					if (!string.IsNullOrEmpty(self) && !string.Equals(p.MainModule?.FileName, self, StringComparison.OrdinalIgnoreCase))
						continue;
					var handle = p.MainWindowHandle;
					if (handle == IntPtr.Zero) continue;
					if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
					SetForegroundWindow(handle);
					return;
				}
			}
		}
		catch { /* 取进程信息可能因权限失败，忽略即可 */ }
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_instanceMutex is not null)
		{
			try { _instanceMutex.ReleaseMutex(); } catch { /* 未持有则忽略 */ }
			_instanceMutex.Dispose();
			_instanceMutex = null;
		}
		base.OnExit(e);
	}

	private const int SW_RESTORE = 9;

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr hWnd);

	/// <summary>切换主题模式并持久化；<paramref name="mode"/> 取值 system / light / dark。</summary>
	public static void SetThemeMode(string mode)
	{
		SettingsStore.Current.Theme = mode;
		SettingsStore.Save();
		ApplyTheme();
	}

	/// <summary>按当前模式应用调色板（不落盘）。</summary>
	public static void ApplyTheme()
	{
		IsDarkMode = ThemeSetting switch
		{
			"dark" => true,
			"light" => false,
			_ => IsSystemDark(),
		};
		if (Current is not App app) return;

		var source = IsDarkMode ? "Themes/ColorsDark.xaml" : "Themes/ColorsLight.xaml";
		var palette = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
		var merged = app.Resources.MergedDictionaries;
		for (int i = 0; i < merged.Count; i++)
		{
			var src = merged[i].Source?.OriginalString ?? "";
			if (src.Contains("Colors", StringComparison.OrdinalIgnoreCase))
			{
				merged[i] = palette;
				return;
			}
		}
		merged.Insert(0, palette);
	}

	private static bool IsSystemDark()
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(
				@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
			if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
		}
		catch { /* 忽略 */ }
		return false;
	}

	/// <summary>防止同一异常反复弹窗造成弹窗风暴。</summary>
	private static bool _reporting;

	/// <summary>友好提示未处理异常：写入日志文件并给出可定位的信息。</summary>
	private static void ReportError(Exception ex)
	{
		// 日志写入与弹窗都要在 UI 线程执行：非 UI 线程（后台线程 / 终结器线程）抛出异常时，
		// 直接构造 WPF 窗口会再次抛出跨线程访问异常，从而把原始错误淹没。
		var dispatcher = Current?.Dispatcher;
		if (dispatcher is not null && !dispatcher.CheckAccess())
		{
			dispatcher.BeginInvoke(() => ReportErrorCore(ex));
			return;
		}
		ReportErrorCore(ex);
	}

	private static void ReportErrorCore(Exception ex)
	{
		try
		{
			AppStorage.Write(AppStorage.ErrorLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
		}
		catch { /* 日志写不进去也不能再抛 */ }

		if (_reporting) return;
		_reporting = true;
		try
		{
			AppDialog.Error(owner: null, "发生错误",
				$"程序遇到一个未预期的错误，已被安全拦截。\n\n{ex.Message}\n\n详细信息已写入日志：\n{AppStorage.ErrorLogFile}");
		}
		catch { /* 弹窗失败时静默，避免递归 */ }
		finally { _reporting = false; }
	}
}
