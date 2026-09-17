using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RenameTool;

/// <summary>
/// 列表行的「入场 / 移除 / 上移接续」动画引擎，供全部列表共用（文件、规则、预览、日志、历史、预设）。
/// 之所以独立成静态类：这套实现原先私有在 MainWindow 里，二级窗口（历史 / 预设）拿不到，只能硬切。
/// 一律按「行数据 → 最外层可视容器」映射后再动画；找不到容器的行（被虚拟化回收、折叠中的文件夹）
/// 直接跳过，绝不影响数据操作本身。
/// </summary>
public static class RowAnimator
{
	private const int EnterMs = 170;        // 单行入场淡入时长
	private const int EnterStaggerMs = 24;  // 多行入场时相邻两行的错峰间隔
	private const int EnterStaggerCap = 7;  // 错峰最多累计几档，避免一次插入几十行时排到几秒后
	private const int RemoveMs = 130;       // 单行移除淡出时长
	private const int ShiftMs = 200;        // 下方行上移接续时长

	// ─────────────── 入场 ───────────────

	/// <summary>已请求入场、但尚未播放动画的行，按宿主列表分组。同一批同步操作里的多次新增在此合并。</summary>
	private static readonly Dictionary<DependencyObject, List<object>> PendingEnters = [];

	/// <summary>本批次内刚被整体清空重填过的列表，其后续新增不逐行动画（见 SuppressEnter）。</summary>
	private static readonly HashSet<DependencyObject> SuppressedEnters = [];

	/// <summary>
	/// 新增行淡入（多行时错峰入场，与 Toast 的入场节奏一致）。
	/// 行容器由 ItemsControl 在下一轮布局里生成，所以取容器要延后一拍；仍未生成的留到空闲再试一次，
	/// 两次都取不到就放弃动画（该行直接以正常状态出现），不会留下看不见的行。
	/// 连续逐条新增（如批量导入文件是一条条 Add）会合并成一批播放，避免每行各跑一遍取容器的遍历。
	/// </summary>
	public static void AnimateEnter(DependencyObject host, IList? added)
	{
		if (added is null || added.Count == 0) return;
		if (SuppressedEnters.Contains(host)) return;

		if (!PendingEnters.TryGetValue(host, out var rows))
		{
			PendingEnters[host] = rows = [];
			host.Dispatcher.BeginInvoke(new Action(() => FlushEnter(host)), DispatcherPriority.Loaded);
		}
		foreach (object? row in added) if (row is not null) rows.Add(row);
	}

	/// <summary>
	/// 通知「该列表刚被整体清空重填」（如预览列表每次重算都会先 Clear 再逐条 Add）。
	/// 这类变化逐行淡入等于每改一次规则就整列表闪一次，观感不如直接切换，故本批次内不播入场动画。
	/// 压制只对当前这一批同步操作有效：解除回调与入场播放同属 Loaded 优先级且排在本次操作之后，
	/// 因此下一批新增照常播放。
	/// </summary>
	public static void SuppressEnter(DependencyObject host)
	{
		PendingEnters.Remove(host);       // 已排队但尚未播放的行一并作废
		if (!SuppressedEnters.Add(host)) return;
		host.Dispatcher.BeginInvoke(new Action(() => SuppressedEnters.Remove(host)), DispatcherPriority.Loaded);
	}

	private static void FlushEnter(DependencyObject host)
	{
		if (!PendingEnters.Remove(host, out var rows) || rows.Count == 0) return;

		var containers = CollectContainers(host);
		List<object>? missing = null;
		int index = 0;
		foreach (object row in rows)
		{
			if (!containers.TryGetValue(row, out var container))
			{
				(missing ??= []).Add(row);
				continue;
			}
			FadeIn(container, index++);
		}
		// 容器可能晚一拍才生成；空闲时补一次，仍取不到（多为虚拟化未生成的远行）就放弃，该行直接以正常状态出现
		if (missing is not null)
			host.Dispatcher.BeginInvoke(new Action(() => FadeInMissing(host, missing)), DispatcherPriority.ApplicationIdle);
	}

	private static void FadeInMissing(DependencyObject host, List<object> rows)
	{
		var containers = CollectContainers(host);
		int index = 0;
		foreach (object row in rows)
			if (containers.TryGetValue(row, out var container)) FadeIn(container, index++);
	}

	private static void FadeIn(FrameworkElement container, int index)
	{
		var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(EnterMs))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			BeginTime = TimeSpan.FromMilliseconds(Math.Min(index, EnterStaggerCap) * EnterStaggerMs),
		};
		container.BeginAnimation(UIElement.OpacityProperty, fade);
	}

	// ─────────────── 移除 ───────────────

	/// <summary>
	/// 单条 / 多条行移除动画：先让被移除的行淡出，再真正移除数据，
	/// 随后让其下方的行从旧位置平滑「上移接续」到新位置，而不是瞬间跳变。
	/// </summary>
	/// <param name="host">承载行的列表容器，用于测量行的纵向位置。</param>
	/// <param name="removed">即将被移除的行数据。</param>
	/// <param name="apply">真正执行移除的委托。</param>
	public static void AnimateRemoval(DependencyObject host, IReadOnlyList<object> removed, Action apply)
	{
		if (removed.Count == 0) { apply(); return; }

		var removedSet = new HashSet<object>(removed);
		var containers = CollectContainers(host);
		var before = CapturePositions(host, containers, removedSet);   // 保留行的移除前位置

		var duration = TimeSpan.FromMilliseconds(RemoveMs);
		var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
		int pending = 0;
		bool finished = false;

		// 无论动画回调还是看门狗触发，都只执行一次
		void Complete()
		{
			if (finished) return;
			finished = true;
			apply();
			AnimateShift(host, before);
		}

		foreach (var row in removed)
		{
			if (!containers.TryGetValue(row, out var container)) continue;
			pending++;
			var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
			fade.Completed += (_, _) => { if (--pending == 0) Complete(); };
			container.BeginAnimation(UIElement.OpacityProperty, fade);
		}

		if (pending == 0) { Complete(); return; }

		// 兜底：动画回调未触发时（例如动画期间容器被回收）定时强制完成
		var watchdog = new DispatcherTimer { Interval = duration + TimeSpan.FromMilliseconds(120) };
		watchdog.Tick += (_, _) => { watchdog.Stop(); Complete(); };
		watchdog.Start();
	}

	/// <summary>
	/// 批量行移除的「上移接续」动画：由命令执行的移除无法插入前置淡出，
	/// 故在命令执行前调用本方法记录各行的当前纵向位置，命令执行并完成布局后再让上移的行平滑归位。
	/// </summary>
	public static void PrepareShift(DependencyObject host)
		=> AnimateShift(host, CapturePositions(host, CollectContainers(host), null));

	/// <summary>
	/// 布局更新后，把因移除而「跳变上移」的行先推回原位，再动画归位，
	/// 从而让下方内容平滑上移接续，而不是瞬间跳跃。
	///
	/// 时机是这段动画的成败关键：必须在「新布局已算完、但还没被渲染出去」的那一刻下手。
	/// 用 LayoutUpdated 正好卡在这一步（布局收尾时触发，渲染在其后）；若改用
	/// BeginInvoke(DispatcherPriority.Loaded) 之类的延后回调，新位置会先渲染出去一帧，
	/// 观感上就是「整块内容先闪一下、再滑回原位」——规则卡片这类高行尤其明显。
	/// </summary>
	public static void AnimateShift(DependencyObject host, Dictionary<object, double> before)
	{
		if (before.Count == 0) return;
		if (host is not FrameworkElement element) return;

		bool done = false;
		DispatcherTimer? watchdog = null;

		// 一次性收尾：无论已归位还是等不到布局，都要解除订阅
		void Detach()
		{
			if (done) return;
			done = true;
			element.LayoutUpdated -= OnLayoutUpdated;
			watchdog?.Stop();
		}

		void OnLayoutUpdated(object? sender, EventArgs e)
		{
			if (done) return;
			// 还没有行发生位移（例如 PrepareShift 是在命令执行前调用的），留到下一次布局再试
			if (!ApplyShift(element, before)) return;
			Detach();
		}

		element.LayoutUpdated += OnLayoutUpdated;

		// 兜底：始终没有行位移（如删掉的是最后一行）时，别让订阅一直挂着
		watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShiftMs + 300) };
		watchdog.Tick += (_, _) => Detach();
		watchdog.Start();

		// 布局也可能早在调用前就已结算（如命令已同步执行且布局已更新），先立刻试一次
		OnLayoutUpdated(null, EventArgs.Empty);
	}

	/// <summary>
	/// 把记录中的行从「已上移的新位置」推回旧位置并动画归位。
	/// 返回是否至少有一行真的发生了位移：没有位移时不收尾，继续等下一次布局。
	/// </summary>
	private static bool ApplyShift(DependencyObject host, Dictionary<object, double> before)
	{
		var containers = CollectContainers(host);
		var duration = TimeSpan.FromMilliseconds(ShiftMs);
		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
		bool moved = false;
		foreach (var (item, oldTop) in before)
		{
			if (!containers.TryGetValue(item, out var container)) continue;
			if (!TryGetTop(host, container, out double newTop)) continue;
			double dy = newTop - oldTop;          // 上移为负
			if (Math.Abs(dy) < 0.5) continue;     // 位置未变，无需动画
			moved = true;
			var offset = new TranslateTransform(0, -dy);   // 先推回旧位置
			container.RenderTransform = offset;
			offset.BeginAnimation(TranslateTransform.YProperty,
				new DoubleAnimation(-dy, 0, duration) { EasingFunction = ease });
		}
		return moved;
	}

	// ─────────────── 容器与坐标 ───────────────

	/// <summary>
	/// 收集宿主列表中的行容器（含嵌套容器内生成的行容器），返回「行数据 → 可视容器」映射。
	/// 广搜顺序保证同一个行数据取到的是最外层容器。判据有两条：
	///   1) Content 与 DataContext 为同一实例——生成的行容器必定满足。注意这条单独并不可靠：
	///      ContentPresenter 会把自身 DataContext 设成 Content，而复选框的文字、下拉框的选中项
	///      也是以 ContentPresenter 呈现的，同样满足该条；
	///   2) TemplatedParent 为 null——行容器由 ItemContainerGenerator 生成，不属于任何控件模板；
	///      而模板内部的呈现器都有 TemplatedParent。
	/// 缺第 2 条时，规则卡片里 4 个复选框与「作用」下拉框会被误判为行容器，位移计算落到错误元素上：
	/// 删除第一张卡片时表现为整块闪烁、下方卡片不接续上移。
	/// 不限定行数据的类型：一是不必为每种列表改白名单，二是越界风险由上述两条判据兜住。
	/// </summary>
	public static Dictionary<object, FrameworkElement> CollectContainers(DependencyObject host)
	{
		var map = new Dictionary<object, FrameworkElement>();
		foreach (var element in EnumerateDescendants(host))
		{
			if (element is not ListBoxItem and not ContentPresenter) continue;
			// 模板内部的呈现器（复选框文字、下拉框选中项、ContentControl 的内容区）不是行容器
			if (element.TemplatedParent is not null) continue;
			object? content = element is ListBoxItem item ? item.Content : ((ContentPresenter)element).Content;
			// 生成的行容器满足 Content 与 DataContext 为同一对象；模板内部的呈现器不满足
			if (content is null) continue;
			if (!ReferenceEquals(content, element.DataContext)) continue;
			map.TryAdd(content, element);
		}
		return map;
	}

	/// <summary>记录列表中各行相对容器的纵向位置（可排除本次将被移除的行）。</summary>
	private static Dictionary<object, double> CapturePositions(DependencyObject host,
		Dictionary<object, FrameworkElement> containers, HashSet<object>? exclude)
	{
		var map = new Dictionary<object, double>();
		foreach (var (item, container) in containers)
		{
			if (exclude is not null && exclude.Contains(item)) continue;
			if (TryGetTop(host, container, out double top)) map[item] = top;
		}
		return map;
	}

	/// <summary>取某行容器相对列表顶部的纵向位置；容器不在可视树中时返回 false。</summary>
	private static bool TryGetTop(DependencyObject host, FrameworkElement container, out double top)
	{
		top = 0;
		if (host is not Visual visual) return false;
		try
		{
			top = container.TransformToAncestor(visual).Transform(new Point(0, 0)).Y;
			return true;
		}
		catch (InvalidOperationException)
		{
			return false;   // 容器已被虚拟化移除，无法测量
		}
	}

	/// <summary>广度优先枚举可视树中的全部元素（自外向内的顺序，便于优先命中最外层容器）。</summary>
	public static IEnumerable<FrameworkElement> EnumerateDescendants(DependencyObject root)
	{
		var queue = new Queue<DependencyObject>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			int count = VisualTreeHelper.GetChildrenCount(current);
			for (int i = 0; i < count; i++)
			{
				if (VisualTreeHelper.GetChild(current, i) is not DependencyObject child) continue;
				queue.Enqueue(child);
				if (child is FrameworkElement element) yield return element;
			}
		}
	}
}
