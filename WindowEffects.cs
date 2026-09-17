using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RenameTool;

/// <summary>
/// 无边框窗口的原生视觉增强：由 DWM 绘制窗口圆角与系统投影。
/// 刻意不使用 AllowsTransparency —— 它会让窗口失去硬件加速，并破坏边缘拖拽缩放，
/// 因此这里改用 DWM 窗口属性实现同样的「圆角 + 层级阴影」观感。
/// </summary>
internal static class WindowEffects
{
	// DWMWINDOWATTRIBUTE
	private const int DWMWA_NCRENDERING_POLICY = 2;
	private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	// DWMNCRENDERINGPOLICY
	private const int DWMNCRP_ENABLED = 2;

	// DWM_WINDOW_CORNER_PREFERENCE
	private const int DWMWCP_ROUND = 2;

	// 分层窗口相关：用于在窗口显示前就把整窗 alpha 置 0
	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_LAYERED = 0x00080000;
	private const int LWA_ALPHA = 0x00000002;
	private const int WM_STYLECHANGING = 0x007C;

	[StructLayout(LayoutKind.Sequential)]
	private struct Margins
	{
		public int Left;
		public int Right;
		public int Top;
		public int Bottom;
	}

	[DllImport("dwmapi.dll", PreserveSig = true)]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("dwmapi.dll")]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, int flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out int flags);

	internal static void Diag(string message)
	{
		if (Environment.GetEnvironmentVariable("RT_DIAG") != "1") return;
		try
		{
			System.IO.File.AppendAllText(
				System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rt_diag.log"),
				DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
		}
		catch
		{
			// 诊断日志失败不影响程序
		}
	}

	/// <summary>
	/// 为窗口启用 DWM 圆角与系统投影：Windows 11 呈现圆角，Windows 10 及以上呈现投影；
	/// 更早的系统或调用失败时静默忽略，不影响程序运行。
	/// 可在构造函数中调用（此时句柄尚未创建，会等到 SourceInitialized 再应用）。
	/// </summary>
	internal static void EnableRoundedWithShadow(Window window)
	{
		IntPtr handle = new WindowInteropHelper(window).Handle;
		if (handle != IntPtr.Zero)
		{
			Apply(handle);
			return;
		}

		window.SourceInitialized += (_, _) =>
		{
			IntPtr hwnd = new WindowInteropHelper(window).Handle;
			if (hwnd != IntPtr.Zero) Apply(hwnd);
		};
	}

	/// <summary>
	/// 为窗口启用「出现淡入 / 关闭淡出」过渡，构造函数中调用即可：
	/// 出现时由透明渐显；关闭时先取消本次关闭，待透明度降到 0 后再真正关闭，
	/// 期间保留原本的 DialogResult，确保模态对话框返回值不受影响。
	/// </summary>
	/// <remarks>
	/// 这里刻意不使用 WPF 的 <see cref="UIElement.Opacity"/> 动画。实测表明，
	/// 在这类「无边框 + DWM 非客户区渲染」的窗口上，Window.Opacity 并不表现为
	/// 真正的 alpha 渐隐，而是先呈现一块不透明的黑色圆角底、再让内容渐显，
	/// 关闭时也会先整体压黑；这正是「出现时先闪纯黑块、关闭时先变黑」的成因。
	/// 因此改为自己接管分层窗口：把窗口声明为 WS_EX_LAYERED，
	/// 用 SetLayeredWindowAttributes 直接驱动整窗 alpha 做缓动斜坡，
	/// 窗口自身的 Opacity 恒为 1 且全程不再改动。
	/// </remarks>
	internal static void EnableFadeTransition(Window window)
	{
		// 时长偏长一点，短了会显得「啪」地跳出来而不像过渡
		const double FadeInMs = 260;
		const double FadeOutMs = 200;
		const double FrameMs = 10;

		IntPtr hwnd = IntPtr.Zero;
		bool started = false;
		bool closing = false;
		bool? savedResult = null;
		double currentAlpha = 0;

		DispatcherTimer? rampTimer = null;
		var rampWatch = new Stopwatch();
		double rampFrom = 0;
		double rampTo = 1;
		double rampMs = FadeInMs;
		bool rampEaseIn = false;
		Action? rampDone = null;

		void SetAlpha(double value)
		{
			if (hwnd == IntPtr.Zero) return;
			int level = (int)Math.Round(value * 255.0);
			if (level < 0) level = 0;
			if (level > 255) level = 255;
			currentAlpha = level / 255.0;
			if (!SetLayeredWindowAttributes(hwnd, 0, (byte)level, LWA_ALPHA))
				Diag("SetLayeredWindowAttributes failed err=" + Marshal.GetLastWin32Error()
					+ " ex=0x" + GetWindowLong(hwnd, GWL_EXSTYLE).ToString("X8"));
		}

		// 与 WPF 动画无关的纯手工斜坡：由渲染优先级的定时器推进，三阶缓动
		void StartRamp(double from, double to, double milliseconds, bool easeIn, Action done)
		{
			rampFrom = from;
			rampTo = to;
			rampMs = milliseconds;
			rampEaseIn = easeIn;
			rampDone = done;
			rampWatch.Restart();

			if (rampTimer == null)
			{
				rampTimer = new DispatcherTimer(DispatcherPriority.Render)
				{
					Interval = TimeSpan.FromMilliseconds(FrameMs),
				};
				rampTimer.Tick += (_, _) =>
				{
					double t = rampWatch.Elapsed.TotalMilliseconds / rampMs;
					if (t >= 1.0) t = 1.0;
					// 最小化时不可见，直接把斜坡落到终点，避免恢复后停在半透明状态
					if (window.WindowState == WindowState.Minimized) t = 1.0;
					double eased = rampEaseIn
						? t * t * t
						: 1.0 - Math.Pow(1.0 - t, 3.0);
					SetAlpha(rampFrom + (rampTo - rampFrom) * eased);
					if (t < 1.0) return;
					rampTimer!.Stop();
					Action? done = rampDone;
					rampDone = null;
					done?.Invoke();
				};
			}
			rampTimer.Start();
		}

		// 窗口本体始终保持完全不透明：渐显渐隐完全交给上面的分层 alpha
		window.Opacity = 1;

		window.SourceInitialized += (_, _) =>
		{
			hwnd = new WindowInteropHelper(window).Handle;
			if (hwnd == IntPtr.Zero) return;

			// 关键：WPF 会在窗口过程的 WM_STYLECHANGING 阶段把 WS_EX_LAYERED 位改写掉，
			// 因此外部对这个窗口调用 SetWindowLong 永远加不上分层位（实测：调用返回成功、
			// 但随即读回的样式里该位已被抹掉，SetLayeredWindowAttributes 随之报 87）。
			// 这里注册一个窗口过程钩子，在样式变更消息上补回该位并标记已处理，
			// 让 WPF 的样式改写不再覆盖它；此后窗口全程保持分层，整窗 alpha 才可控。
			if (PresentationSource.FromVisual(window) is HwndSource source)
			{
				source.AddHook((IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
				{
					if (message == WM_STYLECHANGING && wParam.ToInt32() == GWL_EXSTYLE)
					{
						int requested = Marshal.ReadInt32(lParam, 4);
						if ((requested & WS_EX_LAYERED) == 0)
							Marshal.WriteInt32(lParam, 4, requested | WS_EX_LAYERED);
						handled = true;
					}
					return IntPtr.Zero;
				});
			}

			// 必须在窗口首次呈现前就转为分层窗口并置 alpha 为 0，
			// 否则第一帧会以完全不透明的黑底闪出
			int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
			if ((exStyle & WS_EX_LAYERED) == 0)
				SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
			SetAlpha(0);
		};

		void FinishAndClose()
		{
			try { if (savedResult.HasValue) window.DialogResult = savedResult; }
			catch (InvalidOperationException) { /* 非模态窗口不能设 DialogResult */ }
			window.Close();
		}

		void StartFadeIn()
		{
			if (started) return;
			started = true;
			StartRamp(0, 1, FadeInMs, easeIn: false, done: () =>
			{
				bool ok = GetLayeredWindowAttributes(hwnd, out uint _, out byte alpha, out int flags);
				Diag("fadein done ok=" + ok + " alpha=" + alpha + " flags=0x" + flags.ToString("X8")
					+ " ex=0x" + GetWindowLong(hwnd, GWL_EXSTYLE).ToString("X8"));
			});
		}

		// 关键：不能在 Loaded 就起播。Loaded 触发时窗口内容尚未上色，
		// 此时开始渐显会先露出底层黑底，形成「黑闪一下再淡入」。
		// ContentRendered 发生在内容真正绘制完成之后，从这一帧开始渐显才是干净的。
		window.ContentRendered += (_, _) => StartFadeIn();
		// 兜底：若 ContentRendered 因故未触发（例如窗口被最小化显示），
		// 用一次渲染优先级之后的空闲回调补上，避免窗口永久停在透明状态。
		window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(
			new Action(StartFadeIn), DispatcherPriority.ContextIdle);

		window.Closing += (_, e) =>
		{
			if (closing) return;
			closing = true;
			savedResult = window.DialogResult;   // 非对话框窗口读取返回 null
			e.Cancel = true;

			// 句柄都没建（构造后被直接关闭）或本就透明：无需过渡，调度一次真实关闭即可
			if (hwnd == IntPtr.Zero || currentAlpha <= 0.01)
			{
				window.Dispatcher.BeginInvoke(new Action(FinishAndClose), DispatcherPriority.Normal);
				return;
			}

			// 淡入尚未结束就点了关闭：从当前 alpha 平滑接续，避免跳变
			StartRamp(currentAlpha, 0, FadeOutMs, easeIn: true, done: FinishAndClose);
		};
	}

	private static void Apply(IntPtr hwnd)
	{
		try
		{
			// 圆角：仅在 Windows 11 生效，旧系统上该调用失败但不抛异常
			int corner = DWMWCP_ROUND;
			DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

			// 让 DWM 继续渲染窗口非工作区，从而恢复无边框窗口丢失的系统投影。
			// 注意：这里必须只向外扩展「1 像素」的边框，绝不能传全 0 的边距——
			// 全 0 边距会被 DWM 理解为「把整个客户区都当作玻璃」，而 Windows 11 已无
			// Aero 毛玻璃，该区域会被画成不透明纯黑，且不受 WPF 窗口 Opacity 影响，
			// 于是渐显时会先闪出一整块黑底、渐隐时又会先变黑再消失。
			int ncRendering = DWMNCRP_ENABLED;
			if (DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncRendering, sizeof(int)) == 0)
			{
				var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
				DwmExtendFrameIntoClientArea(hwnd, ref margins);
			}
		}
		catch (DllNotFoundException)
		{
			// 极旧系统缺少 dwmapi.dll：忽略
		}
		catch (EntryPointNotFoundException)
		{
			// 缺少对应导出函数：忽略
		}
	}
}
