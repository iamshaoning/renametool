using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shell;
using System.Windows.Threading;
using RenameTool.Models;
using RenameTool.Services;
using RenameTool.ViewModels;

namespace RenameTool;

/// <summary>主窗口代码后置：仅承载 XAML 声明的事件处理器，业务逻辑在 MainViewModel。</summary>
public partial class MainWindow : ToolWindow
{
	/// <summary>初始化外观滑块时抑制 ValueChanged 回调，避免与持久化设置相互触发。</summary>
	private bool _themeSliderInit;

	/// <summary>
	/// 拖拽遮罩的“延迟收起”定时器（周期性复核，而不是一次性延时到点就收起）。
	/// DragLeave 是冒泡事件：拖拽指针在窗口内各子元素（文件列表 / 规则区 / 预览区）之间移动时，
	/// 离开某个子元素同样会冒泡触发窗口的 DragLeave。若此时立刻收起遮罩，
	/// 紧接着的下一次 DragOver 又把它点亮 —— 观感就是随鼠标移动不停闪烁。
	/// 只用固定延时压制并不够：OLE 拖放循环仅在鼠标移动时回调 DragOver，
	/// 实测同一窗口内相邻 DragLeave → DragOver 的间隔最长可达 283ms，任何固定延时都可能落进空档里误收起。
	/// 所以改为按确定事实判断（见 <see cref="IsDragStillInsideWindow"/>）：
	/// 只要鼠标键仍按住且指针仍在窗口内，拖拽就还在进行，遮罩一律保持点亮。
	/// </summary>
	private readonly DispatcherTimer _dragLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

	public MainWindow()
	{
		InitializeComponent();
		_themeSliderInit = true;
		ThemeSlider.Value = App.ThemeSetting switch
		{
			"dark" => 1,
			"system" => 2,
			_ => 0,
		};
		_themeSliderInit = false;
		UpdateMaxIcon();
		UpdateWindowFrame();
		StateChanged += (_, _) =>
		{
			UpdateMaxIcon();
			UpdateWindowFrame();
		};
		Loaded += OnLoaded;
		Closing += Window_Closing;
		// 拖拽遮罩的延迟收起：周期性复核，拖拽仍在窗口内进行就保持点亮，确实结束/离开才收起
		_dragLeaveTimer.Tick += (_, _) =>
		{
			if (IsDragStillInsideWindow()) return;
			_dragLeaveTimer.Stop();
			DropOverlay.Visibility = Visibility.Collapsed;
		};
		// 窗口圆角投影与淡入淡出已由 ToolWindow 基类构造函数统一处理，这里不再重复调用
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		RestoreWindowState();
		// 规则类型下拉默认选中第一项，方便直接点“＋添加”
		if (RuleTypeCombo.Items.Count > 0 && RuleTypeCombo.SelectedIndex < 0)
			RuleTypeCombo.SelectedIndex = 0;
		// 通知浮层：由本窗口负责逐条播放退场动画，播完后通知 ViewModel 真正移除该条；
		// 同时监听集合增删，为新旧条目的位置变化播放“上举 / 下落”补偿动画
		if (Vm is { } vm)
		{
			vm.ToastExitAnimator = PlayToastExit;
			vm.Toasts.CollectionChanged += OnToastsChanged;
			// 日志是执行过程中逐条追加的，让每条新日志淡入，而不是硬生生地跳出来
			vm.Logs.CollectionChanged += OnLogsChanged;
			vm.Previews.CollectionChanged += OnPreviewsChanged;
			// 文件列表每次刷新都是「清空 + 重建」，重建后把滚动位置放回原处（导入 / 刷新 / 改名不该打断浏览）
			vm.RootFolders.CollectionChanged += OnRootFoldersChanged;
			vm.PropertyChanged += OnVmPropertyChanged;
		}
	}

	private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		// 筛选会整体换掉可见集合，此时回到顶部才是预期行为；其余刷新保持原位置
		if (e.PropertyName == nameof(MainViewModel.FilterText)) _resetFileListScroll = true;
	}

	// ─────────────── 文件列表滚动位置（C7） ───────────────

	/// <summary>下一次列表重建是否回到顶部。仅筛选置位，其余刷新沿用当下位置。</summary>
	private bool _resetFileListScroll;

	/// <summary>
	/// 文件列表的层级树被整体重建（根集合 Reset）时，把重建前的滚动位置记下来，等布局完成后再放回去。
	/// 清空集合本身会先把内容高度压掉，随后布局会把 VerticalOffset 归零；
	/// 因此必须在 Reset 事件的当下取值——此时还没走到布局，VerticalOffset 仍是重建前的位置。
	/// </summary>
	private void OnRootFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action != NotifyCollectionChangedAction.Reset) return;
		if (FindScrollViewer(FileListBox) is not { } scroll) return;

		double offset = _resetFileListScroll ? 0 : scroll.VerticalOffset;
		_resetFileListScroll = false;
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
		{
			if (FindScrollViewer(FileListBox) is not { } target) return;
			if (offset <= 0) target.ScrollToTop();
			else target.ScrollToVerticalOffset(offset);
		}));
	}

	/// <summary>取控件内部承载滚动的 ScrollViewer（列表模板未命名它，只能沿可视树找）。</summary>
	private static ScrollViewer? FindScrollViewer(DependencyObject root)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is ScrollViewer viewer) return viewer;
			if (FindScrollViewer(child) is { } found) return found;
		}
		return null;
	}

	/// <summary>日志新增时让该行淡入（批量清空不逐行动画）。</summary>
	private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add) RowAnimator.AnimateEnter(LogListBox, e.NewItems);
	}

	/// <summary>
	/// 预览列表的增删。注意刷新预览是「先整表清空、再逐条重填」，
	/// 逐行淡入等于每改一次规则就整列表闪一次，故整体重算时压制入场动画，只保留空状态的淡入淡出。
	/// </summary>
	private void OnPreviewsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			RowAnimator.SuppressEnter(PreviewListBox);
			return;
		}
		if (e.Action == NotifyCollectionChangedAction.Add) RowAnimator.AnimateEnter(PreviewListBox, e.NewItems);
	}

	// ─────────────── 通知浮层入场 / 退场动画（第6项：右下角、逐条滑入、上举 / 下落补位） ───────────────

	/// <summary>通知条目 → 已生成的可视卡片，用于按条目播放退场动画。</summary>
	private readonly Dictionary<ToastItem, FrameworkElement> _toastCards = new();

	/// <summary>下一条通知入场动画的起始时刻，让连续出现的多条通知错峰滑入而非同时涌入。</summary>
	private DateTime _nextToastEntrance = DateTime.MinValue;

	/// <summary>连续出现的通知之间，入场动画的错峰间隔。</summary>
	private static readonly TimeSpan ToastEntranceStagger = TimeSpan.FromMilliseconds(110);

	/// <summary>单条通知入场动画时长（自窗口右侧外向左滑入）。</summary>
	private static readonly TimeSpan ToastEntranceDuration = TimeSpan.FromMilliseconds(360);

	/// <summary>单条通知退场动画时长（向右划出窗口并淡出）。</summary>
	private static readonly TimeSpan ToastExitDuration = TimeSpan.FromMilliseconds(240);

	/// <summary>取卡片上承载位移的变换：X 用于滑入 / 滑出，Y 用于上举 / 下落补位，二者可并行。</summary>
	private static TranslateTransform? ToastTranslate(FrameworkElement card)
		=> card.RenderTransform as TranslateTransform;

	/// <summary>
	/// 单条通知入场：自窗口右侧外向左滑入并淡入；多条同时到达时按固定间隔错峰，形成“陆续滑入”的观感。
	/// </summary>
	private void ToastCard_Loaded(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement card) return;
		if (card.DataContext is ToastItem item) _toastCards[item] = card;

		double width = card.ActualWidth > 0 ? card.ActualWidth : 420;
		card.RenderTransformOrigin = new Point(0.5, 0.5);
		card.RenderTransform = new TranslateTransform(width + 60, 0);
		card.Opacity = 0;

		DateTime now = DateTime.UtcNow;
		if (_nextToastEntrance < now) _nextToastEntrance = now;
		TimeSpan delay = _nextToastEntrance - now;
		_nextToastEntrance += ToastEntranceStagger;

		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		if (ToastTranslate(card) is { } translate)
			translate.BeginAnimation(TranslateTransform.XProperty,
				new DoubleAnimation(width + 60, 0, ToastEntranceDuration)
				{ BeginTime = delay, EasingFunction = ease });
		card.BeginAnimation(OpacityProperty,
			new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay });
	}

	/// <summary>
	/// 通知列表增删后的“上举 / 下落”补偿：条目增删会让整个通知栈按新条目高度平移，
	/// 这里把受影响的卡片先摆回原位置再动画归位，避免整栈瞬移跳变。
	/// </summary>
	private void OnToastsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action != NotifyCollectionChangedAction.Add
			&& e.Action != NotifyCollectionChangedAction.Remove) return;
		if (!IsLoaded || _toastCards.Count == 0) return;

		// 参照物必须是本窗口而非 ToastHost：通知栈采用底部对齐，条目增删时改变的是
		// ToastHost 自身在窗口中的位置（栈整体上举 / 下落），栈内各卡片的相对偏移并未改变。
		// 若以 ToastHost 为参照，delta 恒为 0，动画永远不会播放（表现为整栈瞬移）。
		// 此刻尚未重新布局，读到的仍是变化前的位置
		var before = new Dictionary<ToastItem, double>();
		foreach (var pair in _toastCards)
			before[pair.Key] = pair.Value.TranslatePoint(new Point(0, 0), this).Y;

		// 同步刷新布局以取得变化后的位置；必须在本帧内完成，否则会先渲染出一次跳变
		ToastHost.UpdateLayout();

		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		var dur = TimeSpan.FromMilliseconds(280);
		foreach (var pair in _toastCards)
		{
			// 新加入的条目不在 before 中，它由入场动画负责
			if (!before.TryGetValue(pair.Key, out double oldY)) continue;
			if (ToastTranslate(pair.Value) is not { } translate) continue;
			double delta = pair.Value.TranslatePoint(new Point(0, 0), this).Y - oldY;
			if (Math.Abs(delta) < 0.5) continue;
			// 起点先摆回旧位置（相对新位置反向偏移），再动画归位
			translate.BeginAnimation(TranslateTransform.YProperty,
				new DoubleAnimation(-delta, 0, dur) { EasingFunction = ease });
		}
	}

	/// <summary>
	/// 单条通知退场：向右划出窗口并淡出（可与“下落”补位动画并行），播完后真正移除该条。
	/// </summary>
	private void PlayToastExit(ToastItem item, Action done)
	{
		// 卡片可能尚未生成（窗口未加载等），此时直接移除，保证通知不会永久滞留
		if (!_toastCards.Remove(item, out FrameworkElement? card) || ToastTranslate(card) is not { } translate)
		{
			done();
			return;
		}

		var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
		var fade = new DoubleAnimation(card.Opacity, 0, ToastExitDuration) { EasingFunction = ease };
		fade.Completed += (_, _) => done();
		card.BeginAnimation(OpacityProperty, fade);
		translate.BeginAnimation(TranslateTransform.XProperty,
			new DoubleAnimation(translate.X, card.ActualWidth + 80, ToastExitDuration) { EasingFunction = ease });
	}

	// ─────────────── 窗口状态记忆（尺寸 / 位置 / 最大化） ───────────────

	/// <summary>恢复上次的窗口尺寸 / 位置 / 最大化状态；位置已越界时回退到居中默认。</summary>
	private void RestoreWindowState()
	{
		var s = SettingsStore.Current;
		if (s.Width >= MinWidth) Width = s.Width;
		if (s.Height >= MinHeight) Height = s.Height;
		if (s.HasPosition && IsOnScreen(s.Left!.Value, s.Top!.Value))
		{
			WindowStartupLocation = WindowStartupLocation.Manual;
			Left = s.Left.Value;
			Top = s.Top.Value;
		}
		if (s.Maximized) WindowState = WindowState.Maximized;
		if (s.HasLayout) RestoreLayoutSizes(s);
	}

	/// <summary>还原主界面三栏的拖拽比例（第14项：记忆各区域拖拽尺寸）。</summary>
	private void RestoreLayoutSizes(AppSettings s)
	{
		var cols = MainContentGrid.ColumnDefinitions;
		if (cols.Count < 5) return;
		cols[0].Width = new GridLength(s.LayoutLeft!.Value, GridUnitType.Star);
		cols[2].Width = new GridLength(s.LayoutMid!.Value, GridUnitType.Star);
		cols[4].Width = new GridLength(s.LayoutRight!.Value, GridUnitType.Star);
	}

	/// <summary>记录主界面三栏当前宽度比例（第14项）。</summary>
	private void SaveLayoutSizes()
	{
		var cols = MainContentGrid.ColumnDefinitions;
		if (cols.Count < 5) return;
		double left = cols[0].ActualWidth, mid = cols[2].ActualWidth, right = cols[4].ActualWidth;
		if (left <= 0 || mid <= 0 || right <= 0) return;
		var s = SettingsStore.Current;
		s.LayoutLeft = left;
		s.LayoutMid = mid;
		s.LayoutRight = right;
	}

	/// <summary>判断窗口左上角是否仍位于可见屏幕范围内（多屏拔插后避免窗口跑到屏幕外）。</summary>
	private static bool IsOnScreen(double left, double top)
		=> left >= SystemParameters.VirtualScreenLeft - 200
		&& top >= SystemParameters.VirtualScreenTop - 50
		&& left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120
		&& top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120;

	/// <summary>关闭前记录窗口尺寸与位置（第14项：只记忆窗口状态，不记忆导入内容与规则）。</summary>
	private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		// 超大目录的递归扫描可能还要跑很久，关窗即取消，否则进程会拖到扫描结束才退出。
		Vm?.CancelPendingScan();

		var s = SettingsStore.Current;
		if (WindowState == WindowState.Normal)
		{
			s.Width = Width;
			s.Height = Height;
			s.Left = Left;
			s.Top = Top;
		}
		s.Maximized = WindowState == WindowState.Maximized;
		SaveLayoutSizes();
		SettingsStore.Save();
	}

	private static MainViewModel? Vm => Application.Current.MainWindow?.DataContext as MainViewModel;

	// ─────────────── 最大化尺寸约束（无边框窗口的必要修正） ───────────────
	// WindowStyle=None + WindowChrome 时，系统按“整个显示器”而非“工作区”最大化，
	// 窗口四周会溢出到任务栏外，导致底部状态栏被挤到屏幕外看不见。
	// 这里拦截 WM_GETMINMAXINFO，把最大化尺寸约束到当前显示器的工作区。

	private const int WM_GETMINMAXINFO = 0x0024;
	private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MinMaxInfo
	{
		public NativePoint Reserved;
		public NativePoint MaxSize;
		public NativePoint MaxPosition;
		public NativePoint MinTrackSize;
		public NativePoint MaxTrackSize;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo
	{
		public int Size;
		public NativeRect Monitor;
		public NativeRect Work;
		public int Flags;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		if (PresentationSource.FromVisual(this) is HwndSource source)
			source.AddHook(WndProc);
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

		var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
		IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
		if (monitor != IntPtr.Zero)
		{
			var mi = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
			if (GetMonitorInfo(monitor, ref mi))
			{
				info.MaxPosition.X = Math.Abs(mi.Work.Left - mi.Monitor.Left);
				info.MaxPosition.Y = Math.Abs(mi.Work.Top - mi.Monitor.Top);
				info.MaxSize.X = Math.Abs(mi.Work.Right - mi.Work.Left);
				info.MaxSize.Y = Math.Abs(mi.Work.Bottom - mi.Work.Top);
				info.MinTrackSize.X = (int)MinWidth;
				info.MinTrackSize.Y = (int)MinHeight;
			}
		}
		Marshal.StructureToPtr(info, lParam, true);
		handled = true;
		return IntPtr.Zero;
	}

	// ─────────────── 拖拽导入：从资源管理器拖入文件 / 文件夹 ───────────────

	/// <summary>拖拽经过窗口：仅接受文件系统对象，显示“复制”光标并点亮导入遮罩。</summary>
	private void Window_DragOver(object sender, DragEventArgs e)
	{
		bool acceptable = e.Data.GetDataPresent(DataFormats.FileDrop);
		e.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
		// 指针此刻确实还在窗口内：撤销可能已排队的收起动作（消除闪烁的关键一步）
		_dragLeaveTimer.Stop();
		DropOverlay.Visibility = acceptable ? Visibility.Visible : Visibility.Collapsed;
		e.Handled = true;
	}

	/// <summary>拖拽离开子元素或整个窗口（含中途按 Esc 取消）：交给定时器复核后再决定是否收起。</summary>
	private void Window_DragLeave(object sender, DragEventArgs e) => _dragLeaveTimer.Start();

	/// <summary>
	/// 拖拽是否仍在窗口内进行：鼠标键仍按住，且指针仍在窗口客户区内。
	/// 按键状态必须走 GetAsyncKeyState —— OLE 拖放期间鼠标被拖拽源窗口捕获，
	/// 本窗口收不到鼠标消息，WPF 的 Mouse.LeftButton 会停留在按下之前的状态。
	/// </summary>
	private bool IsDragStillInsideWindow()
	{
		const int VK_LBUTTON = 0x01;
		const int VK_RBUTTON = 0x02;
		bool pressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
			|| (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
		if (!pressed || !GetCursorPos(out var p)) return false;
		var topLeft = PointToScreen(new Point(0, 0));
		var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
		return p.X >= topLeft.X && p.X < bottomRight.X
			&& p.Y >= topLeft.Y && p.Y < bottomRight.Y;
	}

	/// <summary>取屏幕坐标下的指针位置（设备像素）。</summary>
	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out NativePoint point);

	/// <summary>取按键的物理状态（OLE 拖放期间唯一可靠的鼠标键状态来源）。</summary>
	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKey);

	/// <summary>放下：自动识别文件与文件夹（文件夹递归导入其中全部文件）。</summary>
	private void Window_Drop(object sender, DragEventArgs e)
	{
		_dragLeaveTimer.Stop();
		DropOverlay.Visibility = Visibility.Collapsed;
		e.Handled = true;
		if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
			Vm?.ImportDropped(paths);
	}

	/// <summary>外观滑块变化：0 明 / 1 暗 / 2 跟随系统。</summary>
	private void ThemeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (_themeSliderInit) return;
		string mode = (int)Math.Round(e.NewValue) switch
		{
			1 => "dark",
			2 => "system",
			_ => "light",
		};
		// 顺序很关键：波纹要先把“换色前的整窗”截成快照，而截图必须发生在拇指位移之前。
		// 拇指位移是 RenderTransform，此刻 Track 还没按新档位重新排布（布局要等本帧稍后执行），
		// 若先位移再截图，快照里的拇指会停在“比新档位再往外一档”的地方 ——
		// 暗→明时快照显示拇指在最右（跟随系统档），观感就是切换瞬间先闪到别的档位再滑回来。
		if (!string.Equals(mode, App.ThemeSetting, StringComparison.OrdinalIgnoreCase))
			PlayThemeRipple(() => App.SetThemeMode(mode), () => AnimateThemeThumb(e.OldValue, e.NewValue));
		else
			AnimateThemeThumb(e.OldValue, e.NewValue);
	}

	// ─────────────── 主题相关动画：滑块滑动 + 换色光圈波纹 ───────────────

	/// <summary>波纹代号：每发起一次加一，用于让被新波纹顶掉的旧动画回调失效。</summary>
	private int _rippleEpoch;

	/// <summary>滑块档位切换：让滑块从旧档位平滑滑到新档位（正在拖动时不做动画，直接跟随鼠标）。</summary>
	private void AnimateThemeThumb(double oldValue, double newValue)
	{
		if (ThemeSlider.Template?.FindName("PART_Track", ThemeSlider) is not Track track) return;
		if (track.Thumb is not { } thumb) return;
		if (thumb.RenderTransform is not TranslateTransform slide) return;
		// 位移层写在 ControlTemplate 里，模板被 seal 时其中的 Freezable 可能被一并冻结，
		// 对冻结对象调 BeginAnimation 会抛「对象已密封或已冻结」。冻结时换成可修改的副本再动画。
		if (slide.IsFrozen)
		{
			slide = slide.Clone();
			thumb.RenderTransform = slide;
		}

		// 先把“拇指此刻实际偏移轨道多少”读出来（有动画时拿到的是动画当前值），
		// 它就是拇指落后 / 超前轨道的量，下面要把它并进新起点。
		// 不这么做的话，上一段滑动没跑完就再次切档时，轨道已经又挪了一档而起点仍按上一档算，
		// 拇指会先倒退一档再重新滑出去 —— 观感就是“闪到别的位置”。
		double live = slide.X;

		slide.BeginAnimation(TranslateTransform.XProperty, null);
		if (thumb.IsDragging)
		{
			slide.X = 0;
			return;
		}

		// 滑动行程 = 滑块总宽 − 槽内左右各 5 的内边距 − 拇指宽；三档只有 2 段间隔
		double thumbWidth = thumb.Width > 0 ? thumb.Width : 28;
		double travel = Math.Max(1, ThemeSlider.ActualWidth - 10 - thumbWidth);
		// 起点 = 新档位往旧档位方向退一个间隔（视觉停在旧档位）+ 拇指当前的滞后量（接着上一段滑）
		double offset = (oldValue - newValue) * travel / 2.0 + live;
		if (Math.Abs(offset) < 0.5) { slide.X = 0; return; }

		// 起点先挪回旧档位，再以“慢起快走再缓停”的曲线滑向新档位，跨档越多走得越久
		slide.X = offset;
		double ms = 200 + 70 * Math.Min(2, Math.Abs(newValue - oldValue));
		var anim = new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(ms))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
		};
		slide.BeginAnimation(TranslateTransform.XProperty, anim);
	}

	/// <summary>
	/// 主题切换过渡：以外观滑块中心为圆心向外扩散一个圆形揭示区，
	/// 圆圈经过的地方立刻变成新主题，尚未到达的地方仍是旧主题。
	/// 做法是先把换色前的整窗渲染成一张快照压在最上层，再用径向遮罩让圆内变透明、
	/// 逐帧放大遮罩半径，于是下方已换色的实时界面随圆圈推进一块块显露出来。
	/// </summary>
	/// <remarks>
	/// 三个细节决定这套过渡是否“无感”：
	/// 1. 揭示层常年 Visible（见 XAML），代码只改图源与遮罩半径、从不切 Visibility。
	///    Hidden ⇄ Visible 会让整窗重新布局一次，若与换肤落在同一帧，就是一次丢帧，
	///    观感正是切换瞬间的轻微抖动。
	/// 2. 先把快照铺满整窗，再把换肤推迟到下一帧执行。换肤要重建整套资源字典，开销不小；
	///    而快照与换肤前的画面本来就一致，早一帧上屏看不出任何变化，
	///    等于把这段开销藏进一张静止画面里，不会拖累呈现。
	/// 3. 遮罩的透明段只到 0.94，即“完全揭示”的半径只有 0.94R。终点若只取到最远角的距离，
	///    最远角（左下）外侧会永远残留一圈没揭开的旧色，动画一结束就被整块掀掉 ——
	///    观感就是“扩散到左下角附近停住、卡一下、再瞬间补齐”。终点按 0.94 反算即可根除。
	/// </remarks>
	/// <param name="applyTheme">真正执行换肤的动作，推迟到快照上屏后的下一帧执行。</param>
	/// <param name="afterSnapshot">快照定格后、换肤之前执行的收尾动作（当前是滑块位移动画）。
	/// 必须在截图之后才做：它会立即改变画面，若早于截图就会被截进快照里。</param>
	private void PlayThemeRipple(Action applyTheme, Action? afterSnapshot = null)
	{
		if (ThemeRippleLayer.Parent is not FrameworkElement host
			|| host.ActualWidth < 1 || host.ActualHeight < 1)
		{
			afterSnapshot?.Invoke();
			applyTheme();
			return;
		}

		Point center;
		try
		{
			center = ThemeSlider.TransformToVisual(host)
				.Transform(new Point(ThemeSlider.ActualWidth / 2, ThemeSlider.ActualHeight / 2));
		}
		catch
		{
			afterSnapshot?.Invoke();
			applyTheme();
			return;
		}

		// 快照必须在换色之前取，否则截到的就是新主题，看不到“旧色被圆圈逐渐吃掉”的过程
		if (CaptureVisual(host) is not { } snapshot)
		{
			afterSnapshot?.Invoke();
			applyTheme();
			return;
		}

		// 覆盖整窗所需的最大半径（圆心到四个角的最远者）
		double far = 0;
		foreach (Point corner in new[]
		         {
			         new Point(0, 0), new Point(host.ActualWidth, 0),
			         new Point(0, host.ActualHeight), new Point(host.ActualWidth, host.ActualHeight),
		         })
		{
			double dx = corner.X - center.X, dy = corner.Y - center.Y;
			far = Math.Max(far, Math.Sqrt(dx * dx + dy * dy));
		}

		// 见方法注释第 3 条：完全揭示的半径是 0.94R，故终点取 far/0.94 再留 32 的余量，
		// 保证动画收尾时整窗（含最远的左下角）早已全部揭示完毕，收尾不留残色、也就不会“瞬间补齐”
		const double RevealStop = 0.94;
		double reach = far / RevealStop + 32;

		int epoch = ++_rippleEpoch;

		ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusXProperty, null);
		ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusYProperty, null);
		ThemeRippleMask.Center = center;
		ThemeRippleMask.GradientOrigin = center;
		// 半径 1：透明区只剩圆心一个点，快照铺满整窗，画面与换肤前像素级一致
		ThemeRippleMask.RadiusX = 1;
		ThemeRippleMask.RadiusY = 1;

		ThemeRippleSnapshot.Source = snapshot;

		// 快照已定格，此刻才允许做“滑块位移”这类会改变画面的动作（见调用处说明）：
		// 快照取的是换色前那一帧，滑块位移晚了半拍也不会被截进去。
		afterSnapshot?.Invoke();

		// Background 优先级在布局 / 渲染之后执行：本帧先把快照合成上屏，下一帧再换肤并起波纹
		Dispatcher.BeginInvoke(new Action(() =>
		{
			// 期间又切了档：交给后一次波纹处理，避免过期回调把新快照清掉
			if (epoch != _rippleEpoch) return;

			applyTheme();

			var dur = TimeSpan.FromMilliseconds(620);
			var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
			var radiusX = new DoubleAnimation(1, reach, dur) { EasingFunction = ease };
			radiusX.Completed += (_, _) =>
			{
				if (epoch != _rippleEpoch) return;
				ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusXProperty, null);
				ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusYProperty, null);
				ThemeRippleMask.RadiusX = 1;
				ThemeRippleMask.RadiusY = 1;
				ThemeRippleSnapshot.Source = null;
			};
			ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusXProperty, radiusX);
			ThemeRippleMask.BeginAnimation(RadialGradientBrush.RadiusYProperty,
				new DoubleAnimation(1, reach, dur) { EasingFunction = ease });
		}), DispatcherPriority.Background);
	}

	/// <summary>把元素当前的渲染结果截成位图（按屏幕设备像素取样，保证快照与原画面逐像素对齐）。</summary>
	private static ImageSource? CaptureVisual(FrameworkElement element)
	{
		try
		{
			double w = element.ActualWidth, h = element.ActualHeight;
			if (w < 1 || h < 1) return null;

			DpiScale dpi = VisualTreeHelper.GetDpi(element);
			// 取整而非向上取整：宽度是整数 DIP 时（UseLayoutRounding 下即常态）
			// 位图的设备像素与屏幕上的实际占位严格 1:1，快照上屏不会被重采样，
			// 否则整窗会有一到两个像素的位移，看起来就是切换瞬间画面轻微抖动
			int pw = (int)Math.Round(w * dpi.DpiScaleX);
			int ph = (int)Math.Round(h * dpi.DpiScaleY);
			if (pw < 1 || ph < 1) return null;

			// 这里刻意不走 bmp.Render(element)：Render 会把元素相对父级的 VisualOffset 一起算进去
			// （本窗口里被截的是自绘边框内偏移 1,1 的网格），截出的快照比实时画面右下各偏 1 像素，
			// 波纹跑完撤掉快照的瞬间整窗会“抖一下”。改用 VisualBrush 画进 DrawingVisual：
			// 画笔取的是元素自身坐标系，偏移不外泄，快照与画面逐像素重合。
			var bmp = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			var dv = new DrawingVisual();
			using (DrawingContext dc = dv.RenderOpen())
			{
				var brush = new VisualBrush(element)
				{
					Stretch = Stretch.None,
					AlignmentX = AlignmentX.Left,
					AlignmentY = AlignmentY.Top,
				};
				dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
			}
			bmp.Render(dv);
			bmp.Freeze();
			return bmp;
		}
		catch
		{
			return null;
		}
	}

	// ─────────────── 自绘标题栏：拖动与窗口控制 ───────────────

	/// <summary>最大化状态下按钮显示“还原”图标，否则显示“最大化”。</summary>
	private void UpdateMaxIcon()
	{
		string key = WindowState == WindowState.Maximized ? "Icon.WindowRestore" : "Icon.WindowMax";
		if (Application.Current?.TryFindResource(key) is System.Windows.Media.Geometry geo)
			MaxIconPath.Data = geo;
	}

	/// <summary>
	/// 最大化时去掉自绘外框（1px）与顶部 / 右侧的缩放热区，
	/// 让最小化 / 最大化 / 关闭按钮的识别区真正贴到屏幕顶边与右上角；
	/// 还原时恢复原样，保证窗口仍可正常拖拽缩放。
	/// 说明：WindowChrome 的 ResizeBorderThickness 在运行时直接改不会生效
	/// （框架只在窗口挂载 chrome 时读取一次），因此这里重新挂载一份 chrome 实例。
	/// </summary>
	private void UpdateWindowFrame()
	{
		bool maximized = WindowState == WindowState.Maximized;
		WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);

		var chrome = WindowChrome.GetWindowChrome(this);
		if (chrome is null) return;

		Thickness target = maximized ? new Thickness(6, 0, 0, 6) : new Thickness(6);
		if (chrome.ResizeBorderThickness == target) return;

		WindowChrome.SetWindowChrome(this, new WindowChrome
		{
			CaptionHeight = chrome.CaptionHeight,
			ResizeBorderThickness = target,
			CornerRadius = chrome.CornerRadius,
			GlassFrameThickness = chrome.GlassFrameThickness,
			UseAeroCaptionButtons = chrome.UseAeroCaptionButtons,
		});
	}

	/// <summary>Ctrl+F：把键盘焦点移到文件列表上方的筛选框。属视图行为，故用路由命令而非 ViewModel 命令。</summary>
	public static readonly RoutedCommand FocusFilterCommand = new();

	private void FocusFilter_Executed(object sender, ExecutedRoutedEventArgs e)
	{
		FilterBox.Focus();
		FilterBox.SelectAll();
	}

	// ─────────────── 键盘操作：筛选框 / 文件列表 / Esc（C3、A14、C5） ───────────────

	/// <summary>键盘光标当前所在的文件行；列表重建后按对象引用重新定位，找不到则从首行重新开始。</summary>
	private FileItem? _kbRow;

	/// <summary>主窗口的 Esc：优先清空筛选，其次关闭通知浮层（浮层原本只能用鼠标点掉）。</summary>
	private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Escape || Vm is not { } vm) return;

		// 焦点在筛选框时由 FilterBox_KeyDown 处理（那时还要顺带把焦点交还列表），这里不抢先。
		if (!FilterBox.IsKeyboardFocusWithin && !string.IsNullOrEmpty(vm.FilterText))
		{
			vm.FilterText = "";
			e.Handled = true;
			return;
		}
		if (vm.Toasts.Count > 0)
		{
			vm.DismissAllToasts();
			e.Handled = true;
		}
	}

	/// <summary>筛选框：回车把焦点交还列表（筛选本就随输入实时结算），Esc 清空筛选并回到列表。</summary>
	private void FilterBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key is not (Key.Enter or Key.Escape)) return;
		e.Handled = true;
		if (e.Key == Key.Escape && Vm is { } vm) vm.FilterText = "";
		FileListBox.Focus();
	}

	/// <summary>
	/// 文件列表的键盘导航：容器（ListBoxItem）不取焦点，上下方向键在「当前可见的行」之间移动，空格切换勾选。
	/// 列表是「文件夹虚拟化 + 行内 ItemsControl」两层结构，行并不是 ListBox 的直接项，
	/// 因此这里自行维护一个键盘光标，并把焦点落到目标行的复选框上——焦点环即“光标在第几行”的可视提示。
	/// </summary>
	private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key is not (Key.Down or Key.Up or Key.Home or Key.End or Key.Space)) return;

		List<FileItem> rows = CollectVisibleRows();
		if (rows.Count == 0) return;

		int index = _kbRow is null ? -1 : rows.IndexOf(_kbRow);
		switch (e.Key)
		{
			case Key.Down: index = index < 0 ? 0 : Math.Min(index + 1, rows.Count - 1); break;
			case Key.Up: index = index < 0 ? rows.Count - 1 : Math.Max(index - 1, 0); break;
			case Key.Home: index = 0; break;
			case Key.End: index = rows.Count - 1; break;
			case Key.Space:
				if (index < 0) index = 0;
				FileItem target = rows[index];
				if (!target.IsMissing) target.Selected = !target.Selected;   // 失效条目不可勾选，与鼠标一致
				break;
		}

		if (index < 0) index = 0;
		e.Handled = true;
		FocusRow(rows[index]);
	}

	/// <summary>按当前展开状态收集真正可见的文件行，顺序与界面自上而下一致。</summary>
	private static List<FileItem> CollectVisibleRows()
	{
		List<FileItem> rows = [];
		if (Vm is not { } vm) return rows;
		foreach (FolderNode root in vm.RootFolders) Collect(root, rows);
		return rows;

		static void Collect(FolderNode node, List<FileItem> into)
		{
			if (!node.IsExpanded) return;
			foreach (object row in node.Rows)
			{
				if (row is FileItem file) into.Add(file);
				else if (row is FolderNode sub) Collect(sub, into);
			}
		}
	}

	/// <summary>把键盘光标移到指定行：先确保该行已生成（顶层文件夹可能被虚拟化回收），再滚入视野并聚焦。</summary>
	private void FocusRow(FileItem item)
	{
		_kbRow = item;
		if (FindRowAnchor(FileListBox, item) is null && Vm is { } vm)
		{
			foreach (FolderNode root in vm.RootFolders)
			{
				if (!root.AllFiles().Contains(item)) continue;
				FileListBox.ScrollIntoView(root);
				FileListBox.UpdateLayout();
				break;
			}
		}
		if (FindRowAnchor(FileListBox, item) is { } anchor)
		{
			anchor.BringIntoView();
			anchor.Focus();
		}
	}

	/// <summary>在可视树中找该行的可聚焦锚点：正常行是勾选框，失效行是行内的删除按钮。</summary>
	private static FrameworkElement? FindRowAnchor(DependencyObject root, object item)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is FrameworkElement { Focusable: true, IsVisible: true } anchor
				&& anchor is CheckBox or Button
				&& ReferenceEquals(anchor.DataContext, item))
				return anchor;
			if (FindRowAnchor(child, item) is { } found) return found;
		}
		return null;
	}

	/// <summary>标题栏右侧的最小化 / 最大化 / 关闭按钮区域不参与拖动，否则按下按钮会连带把窗口拖走。</summary>
	protected override bool BlockTitleBarDrag(MouseButtonEventArgs e)
		=> IsWithinButton(e.OriginalSource as DependencyObject);

	/// <summary>最大化状态下不拖动窗口，与系统标题栏行为一致。</summary>
	protected override bool CanDragTitleBar => WindowState == WindowState.Normal;

	/// <summary>双击标题栏切换最大化 / 还原。</summary>
	protected override void OnTitleBarDoubleClick() => ToggleMaximize();

	private void WindowMin_Click(object sender, RoutedEventArgs e)
		=> WindowState = WindowState.Minimized;

	private void WindowMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

	private void WindowClose_Click(object sender, RoutedEventArgs e) => Close();

	private void ToggleMaximize()
		=> WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

	/// <summary>判断元素是否位于按钮内（标题栏拖动时需排除，避免抢占按钮点击）。</summary>
	private static bool IsWithinButton(DependencyObject? node)
	{
		while (node is not null)
		{
			if (node is ButtonBase) return true;
			node = GetParent(node);
		}
		return false;
	}

	/// <summary>按右侧下拉所选类型新增一条规则。</summary>
	private void AddRuleButton_Click(object sender, RoutedEventArgs e)
	{
		if (Vm is not { } vm) return;
		if (RuleTypeCombo.SelectedValue is RuleType type)
			vm.AddRuleCommand.Execute(type);
	}

	/// <summary>清空执行日志（现有日志行整体淡出后再真正清空）。</summary>
	private void ClearLogButton_Click(object sender, RoutedEventArgs e)
	{
		if (Vm is not { } vm) return;
		RowAnimator.AnimateRemoval(LogListBox, vm.Logs.Cast<object>().ToList(), () => vm.ClearLogCommand.Execute(null));
	}

	/// <summary>打开规则预设面板。</summary>
	private void PresetButton_Click(object sender, RoutedEventArgs e)
		=> OpenDialog(new PresetWindow());

	/// <summary>清空规则链中的全部规则。</summary>
	private void ClearAllRules_Click(object sender, RoutedEventArgs e)
		=> Vm?.ClearAllRulesCommand.Execute(null);

	/// <summary>打开改名历史面板。</summary>
	private void HistoryButton_Click(object sender, RoutedEventArgs e)
		=> OpenDialog(new HistoryWindow());

	/// <summary>打开「关于」窗口（程序信息 / 快捷键 / 存储说明）。</summary>
	private void AboutButton_Click(object sender, RoutedEventArgs e)
		=> OpenDialog(new AboutWindow());

	/// <summary>以本窗口为主窗口、共享同一 ViewModel 打开模态对话框。</summary>
	private void OpenDialog(Window dialog)
	{
		dialog.Owner = this;
		dialog.DataContext = Vm;
		PushModalScrim();
		try { dialog.ShowDialog(); }
		finally { PopModalScrim(); }
	}

	// ─────────────── 模态遮罩（E3） ───────────────

	/// <summary>当前压着遮罩的模态层数。弹窗里再弹提示框时会叠到 2，逐层关掉才恢复。</summary>
	private int _modalScrimDepth;

	/// <summary>
	/// 模态窗口打开时压暗主窗内容。<see cref="AppDialog"/> 这类不经 <see cref="OpenDialog"/> 的
	/// 提示框也会调用它，故按深度计数：只在最外层真正淡入、最后一层真正淡出，
	/// 避免嵌套弹窗时前一个的关闭把遮罩提前收掉。
	/// 高对比度模式下不压暗——那里应当保持系统配色与足够对比，叠一层遮罩只会削弱可读性。
	/// </summary>
	internal void PushModalScrim()
	{
		if (SystemParameters.HighContrast) return;
		if (++_modalScrimDepth > 1) return;
		FadeModalScrim(0.32);
	}

	/// <summary>模态窗口关闭时恢复主窗亮度（与 <see cref="PushModalScrim"/> 配对调用）。</summary>
	internal void PopModalScrim()
	{
		if (SystemParameters.HighContrast || _modalScrimDepth == 0) return;
		if (--_modalScrimDepth > 0) return;
		FadeModalScrim(0);
	}

	/// <summary>遮罩透明度过渡：与全局节奏一致（160ms、CubicEase 缓出）。</summary>
	private void FadeModalScrim(double to)
	{
		ModalScrim.BeginAnimation(OpacityProperty,
			new DoubleAnimation(to, TimeSpan.FromMilliseconds(160))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
				FillBehavior = FillBehavior.HoldEnd,
			});
	}

	// ─────────────── 数字输入框：上下箭头调整数值 ───────────────

	/// <summary>数字输入框右侧的上下箭头：按 1 递增 / 递减（RepeatButton 支持按住连续触发），且不低于 TextBox.Tag 指定的最小值。</summary>
	private void NumberSpin_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not RepeatButton { Tag: string direction } button) return;
		if (button.TemplatedParent is not TextBox box) return;

		long min = 0;
		if (box.Tag is string tag && long.TryParse(tag, out long parsed)) min = parsed;

		if (!long.TryParse(box.Text?.Trim(), out long value)) value = min;
		value += direction == "-1" ? -1 : 1;
		if (value < min) value = min;

		box.Text = value.ToString();
		box.CaretIndex = box.Text.Length;
	}

	// ─────────────── 名称过长：点击切换自动换行 ───────────────

	/// <summary>点击名称：切换该行的自动换行展开状态（文件列表 / 预览 / 日志共用）。</summary>
	private void NameText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: INameExpandable item })
		{
			item.IsNameExpanded = !item.IsNameExpanded;
			e.Handled = true;
		}
	}

	// ─────────────── 右键菜单（B2）：文件行 / 预览行 ───────────────

	/// <summary>在资源管理器中定位该行对应的文件（文件已删除时退回到其所在目录）。</summary>
	private void RevealInExplorer_Click(object sender, RoutedEventArgs e)
	{
		if (ItemPath(sender) is not { Length: > 0 } path) return;
		string? dir = System.IO.Path.GetDirectoryName(path);
		if (dir is null or { Length: 0 }) return;
		try
		{
			string args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{dir}\"";
			Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			AppDialog.Notify(this, "打开失败", $"无法打开所在文件夹：{TextFlow.Shorten(ex.Message, 40)}", DialogIcon.Warning);
		}
	}

	/// <summary>复制该行对应文件的完整路径。</summary>
	private void CopyFullPath_Click(object sender, RoutedEventArgs e)
		=> CopyText(ItemPath(sender), "路径");

	/// <summary>复制该行的原始文件名。</summary>
	private void CopyFileName_Click(object sender, RoutedEventArgs e)
	{
		string? name = DataOf(sender) switch
		{
			FileItem f => f.Name,
			PreviewItem p => p.OriginalName,
			_ => null,
		};
		CopyText(name, "文件名");
	}

	/// <summary>复制预览行计算出的新文件名。</summary>
	private void CopyNewName_Click(object sender, RoutedEventArgs e)
		=> CopyText(DataOf(sender) is PreviewItem p ? p.NewName : null, "新文件名");

	/// <summary>复制“旧名 → 新名”对照文本。</summary>
	private void CopyPair_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is PreviewItem p) CopyText($"{p.OriginalName} → {p.NewName}", "对照文本");
	}

	/// <summary>把该行从文件列表中移除（带「淡出 + 下方上移接续」动画）。</summary>
	private void RemoveFileRow_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is not FileItem file || Vm is not { } vm) return;
		RowAnimator.AnimateRemoval(FileListBox, new object[] { file }, () => vm.RemoveFileCommand.Execute(file));
	}

	/// <summary>文件夹表头右键：展开 / 折叠该文件夹。</summary>
	private void FolderToggle_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is FolderNode node) AnimateFolderRoll(node);
	}

	/// <summary>文件夹表头右键：在资源管理器中打开该文件夹。</summary>
	private void FolderReveal_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is not FolderNode node) return;
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", $"\"{node.FullPath}\"") { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			AppDialog.Notify(this, "打开失败", $"无法打开该文件夹：{TextFlow.Shorten(ex.Message, 40)}", DialogIcon.Warning);
		}
	}

	/// <summary>文件夹表头右键：移除该文件夹（含子文件夹）下的全部文件（带行消失 + 上移接续动画）。</summary>
	private void RemoveFolderRows_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is not FolderNode node || Vm is not { } vm) return;
		// 仅对当前已展开（有可见行容器）的文件做动画；折叠中的文件会被自动跳过
		var rows = node.AllFiles().Cast<object>().ToList();
		RowAnimator.AnimateRemoval(FileListBox, rows, () => vm.RemoveFolderCommand.Execute(node));
	}

	/// <summary>规则卡片右键：复制该规则。</summary>
	private void RuleDuplicate_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is RenameRule rule) Vm?.DuplicateRuleCommand.Execute(rule);
	}

	/// <summary>规则卡片右键：上移该规则。</summary>
	private void RuleMoveUp_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is RenameRule rule) Vm?.MoveRuleUpCommand.Execute(rule);
	}

	/// <summary>规则卡片右键：下移该规则。</summary>
	private void RuleMoveDown_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is RenameRule rule) Vm?.MoveRuleDownCommand.Execute(rule);
	}

	/// <summary>规则卡片右键：删除该规则（带「淡出 + 下方上移接续」动画）。</summary>
	private void RuleRemove_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is not RenameRule rule || Vm is not { } vm) return;
		RowAnimator.AnimateRemoval(RuleItemsHost, new object[] { rule }, () => vm.RemoveRuleCommand.Execute(rule));
	}

	/// <summary>规则卡片头部按钮：删除该规则（带「淡出 + 下方上移接续」动画）。</summary>
	private void RuleRemoveButton_Click(object sender, RoutedEventArgs e)
	{
		if (DataOf(sender) is not RenameRule rule || Vm is not { } vm) return;
		RowAnimator.AnimateRemoval(RuleItemsHost, new object[] { rule }, () => vm.RemoveRuleCommand.Execute(rule));
	}

	/// <summary>工具栏「移除所选」：命令执行前记录行位置，执行后让下方行上移接续。</summary>
	private void DeleteSelected_Click(object sender, RoutedEventArgs e) => RowAnimator.PrepareShift(FileListBox);

	/// <summary>工具栏「清除失效」：命令执行前记录行位置，执行后让下方行上移接续。</summary>
	private void ClearMissing_Click(object sender, RoutedEventArgs e) => RowAnimator.PrepareShift(FileListBox);

	/// <summary>取菜单项所绑定行的数据对象（文件行 → FileItem，预览行 → PreviewItem）。</summary>
	private static object? DataOf(object sender) => (sender as FrameworkElement)?.DataContext;

	/// <summary>取该行对应文件在磁盘上的完整路径（预览行取其源文件，文件夹行取其目录）。</summary>
	private static string? ItemPath(object sender) => DataOf(sender) switch
	{
		FileItem f => f.FullPath,
		FolderNode d => d.FullPath,
		PreviewItem p => p.File.FullPath,
		_ => null,
	};

	/// <summary>复制文本到剪贴板；剪贴板被其他程序占用等异常时给出提示。</summary>
	private void CopyText(string? text, string label)
	{
		if (string.IsNullOrEmpty(text)) return;
		try
		{
			Clipboard.SetText(text);
		}
		catch (Exception ex)
		{
			AppDialog.Notify(this, "复制失败", $"复制{label}失败：{TextFlow.Shorten(ex.Message, 40)}", DialogIcon.Warning);
		}
	}

	// ─────────────── 模块化编辑区：变量插入与模块拖拽 ───────────────

	/// <summary>切换“使用变量”：普通输入框 ⇄ 变量块编辑区，重新按当前文本解析编辑区内容。</summary>
	private void VarMode_CheckBox_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: RenameRule rule })
			rule.ReloadEditors();
	}

	private TemplateSegment? _chipDragSegment;
	private FrameworkElement? _chipDragElement;
	private ItemsControl? _chipDragHost;
	private TranslateTransform? _chipDragTransform;
	private Point _chipDragStartMouse;    // 按下时鼠标相对编辑区的位置
	private Point _chipDragStartTopLeft;  // 按下时模块左上角相对编辑区的位置
	private bool _chipDragActive;

	/// <summary>点击变量按钮：以模块形式插入到所在编辑区（由祖先元素的 Tag 指明目标）。</summary>
	private void VariableButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: string token } button) return;
		if (FindVariableTarget(button) is { } editor)
			editor.InsertVariable(token);
	}

	/// <summary>沿视觉树向上查找目标编辑区：祖先元素的 Tag 为 ReplaceTarget / InsertTarget / TemplateTarget。</summary>
	private static SegmentEditor? FindVariableTarget(DependencyObject? node)
	{
		while (node is not null)
		{
			if (node is FrameworkElement { Tag: string marker } element &&
				FindRule(element) is { } rule)
			{
				SegmentEditor? editor = marker switch
				{
					"ReplaceTarget" => rule.ReplaceEditor,
					"InsertTarget" => rule.InsertEditor,
					"TemplateTarget" => rule.TemplateEditor,
					_ => null,
				};
				if (editor is not null) return editor;
			}
			node = GetParent(node);
		}
		return null;
	}

	/// <summary>沿视觉树向上查找承载当前编辑区的 SegmentEditor。</summary>
	private static SegmentEditor? FindEditor(DependencyObject? node)
	{
		while (node is not null)
		{
			if (node is FrameworkElement { DataContext: SegmentEditor editor }) return editor;
			node = GetParent(node);
		}
		return null;
	}

	/// <summary>点击“自定义文本”：在所在编辑区末尾插入一个内含可编辑文本框的自定义文本模块。</summary>
	private void CustomTextButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button button) return;
		if (FindVariableTarget(button) is { } editor)
			editor.InsertCustomText();
	}

	// ─────────────── 文件列表：文件夹折叠与批量勾选 ───────────────

	/// <summary>点击文件夹表头：展开 / 折叠该文件夹（其下子文件夹一并折叠），并播放整块容器的卷起 / 卷开动画。</summary>
	private void FolderHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is not FrameworkElement { DataContext: FolderNode node }) return;
		if (IsInteractive(e.OriginalSource as DependencyObject)) return;   // 点击复选框等控件时不触发折叠
		AnimateFolderRoll(node);
	}

	/// <summary>
	/// 正在播放折叠 / 展开动画的文件夹集合。按模块（而非全局）加锁：同一个文件夹在动画期间
	/// 忽略重复点击，避免状态与动画不同步；不同文件夹可以各自动画、互不吞掉点击。
	/// </summary>
	private readonly HashSet<FolderNode> _folderAnimating = new();

	/// <summary>
	/// 文件夹展开 / 折叠动画。
	/// 每个文件夹都是一个独立模块：表头 + 自带容器。折叠时把该容器的可见高度从满高收到 0，
	/// 内容随裁剪边缘被整块卷起（而不是每个文件各自卷起），下方内容同步上移接续；
	/// 展开时反向卷开。动画结束才真正隐藏容器，因此不会出现「整体突然消失」。
	/// </summary>
	private void AnimateFolderRoll(FolderNode node)
	{
		if (_folderAnimating.Contains(node) || Vm is not { } vm) return;

		if (FindFolderBody(node) is not { } body)
		{
			// 容器不在可视树中（被虚拟化回收）：不做动画，直接切换状态
			vm.ToggleFolder(node);
			return;
		}

		bool expand = !node.IsExpanded;
		double to;
		if (expand)
		{
			vm.ToggleFolder(node);       // 先展开，让容器可见
			body.UpdateLayout();         // 同步完成布局，读出容器的自然高度（无渲染，不会闪现）
			to = body.ActualHeight;
			if (to < 0.5) return;
		}
		else
		{
			to = 0;
		}

		double from = expand ? 0 : body.ActualHeight;
		if (Math.Abs(from - to) < 0.5)
		{
			if (!expand) vm.ToggleFolder(node);
			return;
		}

		_folderAnimating.Add(node);
		bool finished = false;
		void Finish()
		{
			if (finished) return;
			finished = true;
			body.BeginAnimation(FrameworkElement.HeightProperty, null);   // 交回自适应高度
			if (!expand) vm.ToggleFolder(node);                          // 折叠：动画结束后才真正隐藏整块容器
			_folderAnimating.Remove(node);
		}

		var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.HoldEnd,
		};
		anim.Completed += (_, _) => Finish();
		body.BeginAnimation(FrameworkElement.HeightProperty, anim);

		// 兜底：动画回调未触发时（例如动画期间容器被回收）定时强制收尾
		var watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(340) };
		watchdog.Tick += (_, _) => { watchdog.Stop(); Finish(); };
		watchdog.Start();
	}

	// ─────────────── 可视树辅助：在嵌套容器中定位文件夹模块 / 行容器 ───────────────

	/// <summary>
	/// 取某文件夹模块自带的容器：模板根 Grid 中 Grid.Row=1 的那一块。
	/// 模块或容器不在可视树中（被虚拟化回收）时返回 null。
	/// </summary>
	private FrameworkElement? FindFolderBody(FolderNode node)
	{
		foreach (var element in RowAnimator.EnumerateDescendants(FileListBox))
		{
			if (element is Grid { RowDefinitions.Count: 2 } module
				&& ReferenceEquals(module.DataContext, node))
				return FindRowChild(module, 1);
		}
		return null;
	}

	/// <summary>取容器的第 row 行子元素（文件夹模块的模板根 Grid 只有表头与容器两个子元素）。</summary>
	private static FrameworkElement? FindRowChild(DependencyObject parent, int row)
	{
		int count = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < count; i++)
		{
			if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement child && Grid.GetRow(child) == row)
				return child;
		}
		return null;
	}

	// ─────────────── 列表行移除动画：淡出 + 下方上移接续 ───────────────
	// 具体实现已抽到 RowAnimator（文件 / 规则 / 预览 / 日志 / 历史 / 预设共用），此处只保留调用点。

	/// <summary>点击文件夹级复选框：批量选中 / 取消选择该文件夹（含子文件夹）下的全部文件。</summary>
	private void FolderCheckBox_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not CheckBox { DataContext: FolderNode node } box) return;
		bool select = box.IsChecked == true;   // 点击后已切换为最终意图
		foreach (var file in node.AllFiles())
			if (!file.IsMissing) file.Selected = select;
	}

	// ─────────────── 模块化编辑区：变量模块拖动排序 ───────────────

	/// <summary>在模块上按下：记录起点（在输入框 / 按钮等交互控件上按下不启动拖拽）。</summary>
	private void SegmentChip_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_chipDragActive) return;
		_chipDragSegment = null;
		_chipDragElement = null;
		if (sender is not FrameworkElement { DataContext: TemplateSegment segment } element) return;
		if (IsInteractive(e.OriginalSource as DependencyObject)) return;
		_chipDragSegment = segment;
		_chipDragElement = element;
		_chipDragStartMouse = e.GetPosition(this);
	}

	/// <summary>按住模块移动超过系统阈值：提起模块跟随鼠标，并实时挤出空位。</summary>
	private void SegmentChip_MouseMove(object sender, MouseEventArgs e)
	{
		if (_chipDragActive)
		{
			UpdateChipDrag(e);
			return;
		}
		if (e.LeftButton != MouseButtonState.Pressed || _chipDragSegment is null || _chipDragElement is null)
			return;

		Vector delta = e.GetPosition(this) - _chipDragStartMouse;
		if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;

		BeginChipDrag(e);
	}

	/// <summary>开始拖拽：给模块施加位移变换并提升层级，使其浮在其它模块之上。</summary>
	private void BeginChipDrag(MouseEventArgs e)
	{
		if (_chipDragElement is null) return;
		if (FindAncestor<ItemsControl>(_chipDragElement) is not { } host) return;

		_chipDragHost = host;
		_chipDragStartMouse = e.GetPosition(host);
		_chipDragStartTopLeft = _chipDragElement.TranslatePoint(new Point(0, 0), host);
		_chipDragTransform = new TranslateTransform();
		ShowDragGhost(_chipDragElement);
		_chipDragElement.RenderTransform = _chipDragTransform;
		Lift(_chipDragElement);
		_chipDragElement.LostMouseCapture += SegmentChip_LostCapture;
		_chipDragElement.MouseLeftButtonUp += SegmentChip_MouseUp;
		_chipDragElement.CaptureMouse();
		_chipDragActive = true;
	}

	/// <summary>拖拽过程中：模块跟随鼠标，跨过其它模块中心时按阅读顺序实时重排。</summary>
	private void UpdateChipDrag(MouseEventArgs e)
	{
		if (_chipDragHost is null || _chipDragElement is null || _chipDragSegment is null ||
			_chipDragTransform is null) return;
		if (FindEditor(_chipDragElement) is not { } editor) return;

		Point mouse = e.GetPosition(_chipDragHost);
		var target = new Point(
			_chipDragStartTopLeft.X + (mouse.X - _chipDragStartMouse.X),
			_chipDragStartTopLeft.Y + (mouse.Y - _chipDragStartMouse.Y));

		// 目标索引：统计在“阅读顺序”上位于拖拽模块之前的模块数量（换行即进入下一行）。
		double centerX = target.X + _chipDragElement.ActualWidth / 2;
		int from = editor.Segments.IndexOf(_chipDragSegment);
		int desired = 0;
		for (int i = 0; i < editor.Segments.Count; i++)
		{
			if (i == from) continue;
			if (ItemContainer(_chipDragHost, editor.Segments[i]) is not FrameworkElement container) continue;
			// 折叠 / 零宽度的片段（如变量模式下被隐藏的末尾空文本段）不参与落点判定，
			// 否则拖到最右端时模块会被排到它之后，形成“空白跑到模块左侧”的错觉。
			if (container.Visibility != Visibility.Visible || container.ActualWidth <= 0) continue;
			Point origin = container.TranslatePoint(new Point(0, 0), _chipDragHost);
			bool before = origin.Y < target.Y - 4 ||
				(Math.Abs(origin.Y - target.Y) <= 4 && origin.X + container.ActualWidth / 2 < centerX);
			if (before) desired++;
		}
		if (from >= 0 && desired != from)
		{
			editor.MoveTo(_chipDragSegment, desired);
			_chipDragHost.UpdateLayout();
		}

		// 校正位移：使模块在重排后仍精确跟随鼠标
		Point current = _chipDragElement.TranslatePoint(new Point(0, 0), _chipDragHost);
		_chipDragTransform.X += target.X - current.X;
		_chipDragTransform.Y += target.Y - current.Y;
		UpdateDragGhost(_chipDragElement);
	}

	private void SegmentChip_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (_chipDragActive) EndChipDrag();
	}

	private void SegmentChip_LostCapture(object sender, MouseEventArgs e)
	{
		if (_chipDragActive) EndChipDrag();
	}

	/// <summary>结束拖拽：解除捕获并让模块平滑落回布局位置。</summary>
	private void EndChipDrag()
	{
		_chipDragActive = false;
		FrameworkElement? element = _chipDragElement;
		TranslateTransform? transform = _chipDragTransform;
		_chipDragElement = null;
		_chipDragTransform = null;
		_chipDragHost = null;
		_chipDragSegment = null;

		HideDragGhost();
		if (element is null) return;
		element.LostMouseCapture -= SegmentChip_LostCapture;
		element.MouseLeftButtonUp -= SegmentChip_MouseUp;
		if (element.IsMouseCaptured) element.ReleaseMouseCapture();
		Settle(element, transform);
	}

	/// <summary>点击模块上的 × ：整体删除该变量模块。</summary>
	private void TemplateChipRemove_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: TemplateSegment segment })
			FindEditor(sender as DependencyObject)?.Remove(segment);
	}

	// ─────────────── 规则卡片：抓取提起式拖动排序 ───────────────

	private Point _ruleDragStartMouse;            // 按下时鼠标相对规则区的位置
	private Point _ruleDragStartTopLeft;          // 按下时卡片左上角相对规则区的位置
	private RenameRule? _ruleDragRule;
	private FrameworkElement? _ruleDragElement;   // 被拖拽的卡片（模板根 Border）
	private ItemsControl? _ruleDragHost;          // 承载规则列表的 ItemsControl
	private TranslateTransform? _ruleDragTransform;
	private bool _ruleDragActive;

	/// <summary>在规则卡片上按下：记录起点（交互控件上按下不启动拖拽）。</summary>
	private void RuleCard_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_ruleDragActive) return;
		_ruleDragRule = null;
		_ruleDragElement = null;
		if (sender is not FrameworkElement { DataContext: RenameRule rule } element) return;
		if (IsInteractive(e.OriginalSource as DependencyObject)) return;
		_ruleDragRule = rule;
		_ruleDragElement = element;
		_ruleDragStartMouse = e.GetPosition(this);
	}

	/// <summary>按住卡片拖动超过系统阈值：把卡片整体“提起”并跟随鼠标，实时挤开其他卡片。</summary>
	private void RuleCard_MouseMove(object sender, MouseEventArgs e)
	{
		if (_ruleDragActive)
		{
			UpdateRuleDrag(e);
			return;
		}
		if (e.LeftButton != MouseButtonState.Pressed || _ruleDragRule is null || _ruleDragElement is null)
			return;

		Vector delta = e.GetPosition(this) - _ruleDragStartMouse;
		if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;

		BeginRuleDrag(e);
	}

	/// <summary>开始拖拽：给卡片施加位移变换并提升层级，使其浮在其它卡片之上。</summary>
	private void BeginRuleDrag(MouseEventArgs e)
	{
		if (_ruleDragElement is null) return;
		if (FindAncestor<ItemsControl>(_ruleDragElement) is not { } host) return;

		_ruleDragHost = host;
		_ruleDragStartMouse = e.GetPosition(host);
		_ruleDragStartTopLeft = _ruleDragElement.TranslatePoint(new Point(0, 0), host);
		_ruleDragTransform = new TranslateTransform();
		ShowDragGhost(_ruleDragElement);
		_ruleDragElement.RenderTransform = _ruleDragTransform;
		_ruleDragElement.Cursor = Cursors.SizeAll;
		Lift(_ruleDragElement);
		_ruleDragElement.LostMouseCapture += RuleCard_LostCapture;
		_ruleDragElement.MouseLeftButtonUp += RuleCard_MouseUp;
		_ruleDragElement.CaptureMouse();
		_ruleDragActive = true;
	}

	/// <summary>拖拽过程中：卡片跟随鼠标，跨过其它卡片中线时实时重排（挤出空位）。</summary>
	private void UpdateRuleDrag(MouseEventArgs e)
	{
		if (_ruleDragHost is null || _ruleDragElement is null || _ruleDragRule is null ||
			_ruleDragTransform is null) return;
		if (Vm is not { } vm) return;

		Point mouse = e.GetPosition(_ruleDragHost);
		var target = new Point(
			_ruleDragStartTopLeft.X + (mouse.X - _ruleDragStartMouse.X),
			_ruleDragStartTopLeft.Y + (mouse.Y - _ruleDragStartMouse.Y));

		// 目标索引：统计“中线位于拖拽卡片中线之上”的其它卡片数量。
		double centerY = target.Y + _ruleDragElement.ActualHeight / 2;
		int from = vm.Rules.IndexOf(_ruleDragRule);
		int desired = 0;
		for (int i = 0; i < vm.Rules.Count; i++)
		{
			if (i == from) continue;
			if (ItemContainer(_ruleDragHost, vm.Rules[i]) is not FrameworkElement container) continue;
			Point origin = container.TranslatePoint(new Point(0, 0), _ruleDragHost);
			if (origin.Y + container.ActualHeight / 2 < centerY) desired++;
		}
		if (from >= 0 && desired != from)
		{
			vm.Rules.Move(from, desired);
			_ruleDragHost.UpdateLayout();
		}

		// 校正位移：使卡片在重排后仍精确跟随鼠标
		Point current = _ruleDragElement.TranslatePoint(new Point(0, 0), _ruleDragHost);
		_ruleDragTransform.X += target.X - current.X;
		_ruleDragTransform.Y += target.Y - current.Y;
		UpdateDragGhost(_ruleDragElement);
	}

	private void RuleCard_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (_ruleDragActive) EndRuleDrag();
	}

	private void RuleCard_LostCapture(object sender, MouseEventArgs e)
	{
		if (_ruleDragActive) EndRuleDrag();
	}

	/// <summary>结束拖拽：解除捕获并让卡片平滑落回布局位置。</summary>
	private void EndRuleDrag()
	{
		_ruleDragActive = false;
		FrameworkElement? element = _ruleDragElement;
		TranslateTransform? transform = _ruleDragTransform;
		_ruleDragElement = null;
		_ruleDragTransform = null;
		_ruleDragHost = null;
		_ruleDragRule = null;

		HideDragGhost();
		if (element is null) return;
		element.LostMouseCapture -= RuleCard_LostCapture;
		element.MouseLeftButtonUp -= RuleCard_MouseUp;
		element.Cursor = null;
		if (element.IsMouseCaptured) element.ReleaseMouseCapture();
		Settle(element, transform);
	}

	// ─────────────── 拖拽排序：共享工具 ───────────────

	/// <summary>提升元素层级：沿父链找到第一个承载它的 Panel，并抬高其直接子级的渲染次序。</summary>
	private static void Lift(FrameworkElement element)
	{
		for (DependencyObject? node = element; node is not null; node = GetParent(node))
			if (GetParent(node) is Panel) { Panel.SetZIndex((UIElement)node, 1000); return; }
	}

	/// <summary>还原由 <see cref="Lift"/> 提升的层级。</summary>
	private static void Unlift(FrameworkElement element)
	{
		for (DependencyObject? node = element; node is not null; node = GetParent(node))
			if (GetParent(node) is Panel) { ((UIElement)node).ClearValue(Panel.ZIndexProperty); return; }
	}

	/// <summary>拖拽投影：窗口级浮层上的一张位图，保证被拖元素渲染在文件列表 / 规则区 / 预览区之上。</summary>
	private Image? _dragGhost;

	/// <summary>开始拖拽时为元素生成投影（在施加位移变换之前截取，避免把变换一并画进去）。</summary>
	private void ShowDragGhost(FrameworkElement element)
	{
		HideDragGhost();
		int w = (int)Math.Ceiling(element.ActualWidth);
		int h = (int)Math.Ceiling(element.ActualHeight);
		if (w <= 0 || h <= 0) return;

		DpiScale dpi = VisualTreeHelper.GetDpi(element);
		var bitmap = new RenderTargetBitmap(
			(int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
			(int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
			96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
		bitmap.Render(element);

		_dragGhost = new Image
		{
			Source = bitmap,
			Width = element.ActualWidth,
			Height = element.ActualHeight,
			IsHitTestVisible = false,
		};
		DragLayer.Children.Add(_dragGhost);
		UpdateDragGhost(element);
	}

	/// <summary>让浮层上的投影跟随被拖元素的当前位置。</summary>
	private void UpdateDragGhost(FrameworkElement element)
	{
		if (_dragGhost is null) return;
		Point origin = element.TranslatePoint(new Point(0, 0), DragLayer);
		Canvas.SetLeft(_dragGhost, origin.X);
		Canvas.SetTop(_dragGhost, origin.Y);
	}

	/// <summary>移除拖拽投影。</summary>
	private void HideDragGhost()
	{
		if (_dragGhost is null) return;
		DragLayer.Children.Remove(_dragGhost);
		_dragGhost = null;
	}

	/// <summary>拖拽结束：让元素从当前位移平滑落回布局位置，随后清除位移与提升的层级。</summary>
	private static void Settle(FrameworkElement element, TranslateTransform? transform)
	{
		if (transform is null) { Unlift(element); return; }

		var duration = TimeSpan.FromMilliseconds(160);
		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		var x = new DoubleAnimation(transform.X, 0, duration) { EasingFunction = ease };
		var y = new DoubleAnimation(transform.Y, 0, duration) { EasingFunction = ease };
		y.Completed += (_, _) =>
		{
			element.ClearValue(UIElement.RenderTransformProperty);
			Unlift(element);
		};
		transform.BeginAnimation(TranslateTransform.XProperty, x);
		transform.BeginAnimation(TranslateTransform.YProperty, y);
	}

	/// <summary>获取列表项对应的容器元素（用于计算重排目标）。</summary>
	private static FrameworkElement? ItemContainer(ItemsControl host, object item)
		=> host.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;

	/// <summary>判断来源是否落在交互控件内（输入框 / 按钮 / 下拉框），避免与拖拽排序冲突。</summary>
	private static bool IsInteractive(DependencyObject? node)
	{
		while (node is not null)
		{
			if (node is ButtonBase or TextBoxBase or ComboBox) return true;
			node = GetParent(node);
		}
		return false;
	}

	/// <summary>沿视觉 / 逻辑树向上查找承载 <see cref="RenameRule"/> 的元素。</summary>
	private static RenameRule? FindRule(DependencyObject? node)
	{
		while (node is not null)
		{
			if (node is FrameworkElement { DataContext: RenameRule rule }) return rule;
			node = GetParent(node);
		}
		return null;
	}

	private static DependencyObject? GetParent(DependencyObject node)
		=> node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

	/// <summary>沿视觉 / 逻辑树向上查找指定类型的祖先元素。</summary>
	private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
	{
		while (node is not null)
		{
			if (node is T target) return target;
			node = GetParent(node);
		}
		return null;
	}
}
