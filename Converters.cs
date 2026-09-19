using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RenameTool.Models;

namespace RenameTool;

/// <summary>层级深度 → 缩进指示线序列（0..depth-1）：每深入一级，行首绘制一条 14 像素宽的竖向参考线。</summary>
public sealed class IndentGuidesConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		int depth = value is int d ? d : 0;
		if (depth <= 0) return Array.Empty<int>();
		var guides = new int[depth];
		for (int i = 0; i < depth; i++) guides[i] = i;
		return guides;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>
/// 主题画刷取值器：转换器原先直接 <c>Application.Current.FindResource(...)</c>，拿到的是调色板里
/// 那一个已冻结的画刷实例；而 <c>App.ApplyTheme</c> 只替换资源字典中的字典项、不会重新触发转换器，
/// 于是已存在的预览行徽标底色、日志状态色会一直停在切换前的旧主题色（其余 DynamicResource 已换）。
/// 这里改为「取一次当前颜色、自己新建一个可变画刷并登记」，主题切换后由 <see cref="RefreshAll"/>
/// 把颜色同步到新调色板；Freezable 的颜色变化会自行触发重绘，不需要重建绑定。
/// </summary>
internal static class ThemeBrush
{
	private static readonly List<(WeakReference<SolidColorBrush> Brush, string Key)> Tracked = new();

	public static Brush Get(string key)
	{
		var brush = new SolidColorBrush(Resolve(key));
		Tracked.Add((new WeakReference<SolidColorBrush>(brush), key));
		if (Tracked.Count > 256) Prune();
		return brush;
	}

	private static Color Resolve(string key)
		=> Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : Colors.Transparent;

	/// <summary>主题切换后调用：把登记过的画刷颜色同步到新调色板（已回收的条目顺带清理）。</summary>
	public static void RefreshAll()
	{
		for (int i = 0; i < Tracked.Count; i++)
		{
			if (!Tracked[i].Brush.TryGetTarget(out var brush)) continue;
			var color = Resolve(Tracked[i].Key);
			if (brush.Color != color) brush.Color = color;
		}
		Prune();
	}

	private static void Prune()
	{
		for (int i = Tracked.Count - 1; i >= 0; i--)
			if (!Tracked[i].Brush.TryGetTarget(out _)) Tracked.RemoveAt(i);
	}
}

/// <summary>
/// 问题徽标底色。按「能否自动挽救」分三档着色，而不是原先的「冲突=警告色、其余一律危险色」：
/// 可自动编号解决的冲突为提示蓝，可截断 / 调整参数挽救的为注意黄，必须由用户动手改名的为危险红。
/// </summary>
public sealed class IssueBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var issue = value is PreviewIssue i ? i : PreviewIssue.None;
		return issue switch
		{
			PreviewIssue.None => Brushes.Transparent,
			// 仅重名：勾选自动编号即可继续，属提示
			PreviewIssue.Conflict => ThemeBrush.Get("InfoSoftBrush"),
			// 超长可截断、序号越界可调参数，属需要处理但能挽救
			PreviewIssue.TooLong or PreviewIssue.SequenceOverflow => ThemeBrush.Get("WarningSoftBrush"),
			// 空名与非法字符（含保留名）必须由用户改名
			_ => ThemeBrush.Get("DangerSoftBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>问题徽标文字色（与 IssueBrush 的浅底配对，保证深/浅主题均可读）。</summary>
public sealed class IssueTextBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var issue = value is PreviewIssue i ? i : PreviewIssue.None;
		return issue switch
		{
			// 无问题时徽标已折叠，这里给中性色兜底，避免落到危险红
			PreviewIssue.None => ThemeBrush.Get("MutedTextBrush"),
			PreviewIssue.Conflict => ThemeBrush.Get("InfoBrush"),
			PreviewIssue.TooLong or PreviewIssue.SequenceOverflow => ThemeBrush.Get("WarningBrush"),
			_ => ThemeBrush.Get("DangerBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class StatusBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string s = value as string ?? "";
		return s switch
		{
			"success" => ThemeBrush.Get("SuccessBrush"),
			"failed" => ThemeBrush.Get("DangerBrush"),
			_ => ThemeBrush.Get("MutedTextBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class StatusTextConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return (value as string) switch
		{
			"success" => "成功",
			"failed" => "失败",
			_ => "跳过",
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>规则类型 → Lucide 图标几何（Themes/Icons.xaml 中的资源键）。</summary>
public sealed class RuleIconConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string key = (value is RuleType t ? t : RuleType.FindReplace) switch
		{
			RuleType.FindReplace => "Icon.Search",
			RuleType.Insert => "Icon.Type",
			RuleType.Sequence => "Icon.ListOrdered",
			RuleType.NameTemplate => "Icon.LayoutTemplate",
			RuleType.CaseStyle => "Icon.CaseSensitive",
			RuleType.RemoveCleanup => "Icon.Eraser",
			RuleType.PairSwap => "Icon.ArrowLeftRight",
			_ => "Icon.Square",
		};
		return Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class BoolVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		bool v = value is bool b && b;
		if (parameter is string p && p == "invert") v = !v;
		return v ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>
/// 滚动条 Maximum（0 表示内容未溢出，无需滚动）→ 滑块可见性。
/// 各滚动区始终保留滚动条槽位（VerticalScrollBarVisibility=Visible），
/// 内容未溢出时把滑块收起，从而避免滚动条出现/消失导致内容宽度反复抖动。
/// </summary>
public sealed class ScrollThumbVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		double max = value is double d ? d : 0;
		return max > 0.001 ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>
/// 让滚动条滑块的“长度变化”变成连续动画。
/// 滑块长度由 Track 按「视口 / 内容」比例算出后直接写进布局；列表折叠或展开时内容高度是分段变化的，
/// 布局要走过几个中间态，比例便连跳两三次，滑块长度就一截一截地闪。
/// 这里在滑块长度变化时，用动画让模板里的可视条追向新长度：终值仍是布局算出的真实值，
/// 过程却是连续的。纯视觉过渡，不参与滚动计算，也不改变命中区域。
/// E5：动画只作用在渲染层的纵向缩放上，不再逐帧改写可视条的 Height——Height 是布局属性，
/// 改它会触发测量 / 排列；缩放不触布局，长短变化却同样连续（与进度条的补间同一套做法）。
/// </summary>
public static class SmoothScrollThumb
{
	private const double DurationMs = 180;

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
		"Enabled", typeof(bool), typeof(SmoothScrollThumb), new PropertyMetadata(false, OnEnabledChanged));

	public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not Thumb thumb) return;
		thumb.SizeChanged -= OnThumbSizeChanged;
		if (e.NewValue is true) thumb.SizeChanged += OnThumbSizeChanged;
	}

	private static void OnThumbSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (!e.HeightChanged || sender is not Thumb thumb) return;
		// 只平滑纵向滚动条：横向滑块的“长度”是宽度，套用同一套高度逻辑会把它压成一小块
		if (thumb.TemplatedParent is not ScrollBar { Orientation: Orientation.Vertical }) return;
		// 滑块本身是模板里那个具名 Border（Thumb 模板根是 Canvas，中间还夹着一层定位用 Grid，
		// 所以不能再用「第一个子元素」去取）
		if (thumb.ActualHeight < 0.5) return;
		if (thumb.Template?.FindName("Th", thumb) is not FrameworkElement bar) return;
		if (bar.RenderTransform is not ScaleTransform scale) return;   // 缩放层由模板提供
		scale = EnsureAnimatable(bar, scale);

		double height = thumb.ActualHeight;
		// 上一次的长度变化可能还在动画中：以「当前实际渲染出来的长度」为起点，连续两次变化也能接得上。
		// 没有显式长度说明还没上过屏，此时不留起点，直接落位，避免滑块从 0 长出来。
		double rendered = double.IsNaN(bar.Height) ? double.NaN : bar.ActualHeight * scale.ScaleY;

		bar.Height = height;                                          // 布局值永远是权威长度
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);    // 清掉在飞的动画，好取基准

		if (double.IsNaN(rendered) || Math.Abs(height - rendered) < 0.5)
		{
			scale.ScaleY = 1;
			return;
		}

		scale.ScaleY = 1;
		scale.BeginAnimation(ScaleTransform.ScaleYProperty,
			new DoubleAnimation(rendered / height, 1, TimeSpan.FromMilliseconds(DurationMs))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			});
	}

	/// <summary>
	/// 取可做动画的缩放层。滚动条模板定义在 ResourceDictionary 里，模板被 sealed 时其中的 Freezable
	/// 会被一并冻结——这层缩放没有被任何 Storyboard 按名引用，WPF 不会替它保留可写性，
	/// 对冻结对象调 BeginAnimation 会抛「对象已密封或已冻结」（E5 改缩放动画后暴露）。
	/// 冻结时换成可修改的副本并写回元素，此后动画都作用在这份副本上。
	/// </summary>
	private static ScaleTransform EnsureAnimatable(FrameworkElement host, ScaleTransform scale)
	{
		if (!scale.IsFrozen) return scale;
		var mutable = scale.Clone();
		host.RenderTransform = mutable;
		return mutable;
	}
}

/// <summary>
/// 让进度条填充段（PART_Indicator）的长度变化连续过渡，而不是一格一格地跳。
/// 填充段的像素宽度是 ProgressBar 自己按 值/总量 算好后直接写进 Width 的，
/// 所以不能对 Width 做动画——动画值会盖住布局随后写入的值，进度就再也不动了；
/// 这里改成在填充段上挂一层缩放：新宽度照旧由布局写入，动画只负责把「上一帧的宽度」
/// 用 scale = 旧宽/新宽 补回来，再连续追到 1。渲染上是连续生长，且完全不触碰布局。
/// </summary>
public static class SmoothProgress
{
	private const double DurationMs = 220;

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
		"Enabled", typeof(bool), typeof(SmoothProgress), new PropertyMetadata(false, OnEnabledChanged));

	/// <summary>上一次的填充宽度，仅用于换算缩放起点（挂在同一个元素上，随模板实例存活）。</summary>
	private static readonly DependencyProperty LastWidthProperty = DependencyProperty.RegisterAttached(
		"LastWidth", typeof(double), typeof(SmoothProgress), new PropertyMetadata(double.NaN));

	public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not FrameworkElement indicator) return;
		indicator.SizeChanged -= OnIndicatorSizeChanged;
		if (e.NewValue is true) indicator.SizeChanged += OnIndicatorSizeChanged;
	}

	private static void OnIndicatorSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (!e.WidthChanged || sender is not FrameworkElement indicator) return;

		double width = indicator.ActualWidth;
		double previous = (double)indicator.GetValue(LastWidthProperty);
		indicator.SetValue(LastWidthProperty, width);

		// 首次上屏：布局给的就是真实长度，直接落位，避免进度条从 0 长出来
		if (double.IsNaN(previous) || Math.Abs(width - previous) < 0.5) return;

		if (indicator.RenderTransform is not ScaleTransform scale) return;
		scale = EnsureAnimatable(indicator, scale);
		scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		if (width <= 0.5)
		{
			// 归零时没有可缩放的基准（旧宽/新宽 会发散），直接贴合
			scale.ScaleX = 1;
			return;
		}

		scale.ScaleX = 1;
		scale.BeginAnimation(ScaleTransform.ScaleXProperty,
			new DoubleAnimation(previous / width, 1, TimeSpan.FromMilliseconds(DurationMs))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			});
	}

	/// <summary>
	/// 取可做动画的缩放层。进度条模板同样定义在 ResourceDictionary 里，模板 sealed 时
	/// 内联的 Freezable 会被冻结，对冻结对象调 BeginAnimation 会抛「对象已密封或已冻结」。
	/// 冻结时换成可修改的副本并写回元素。
	/// </summary>
	private static ScaleTransform EnsureAnimatable(FrameworkElement host, ScaleTransform scale)
	{
		if (!scale.IsFrozen) return scale;
		var mutable = scale.Clone();
		host.RenderTransform = mutable;
		return mutable;
	}
}

/// <summary>
/// 输入出错时让目标元素横向抖一下（E4）。
/// 只在「本来没提示 → 现在有提示」的那一刻抖：一直开着提示时继续编辑不会再晃，
/// 否则敲出半截正则（每键都变错误信息）会变成连续抖动，反而干扰输入。
/// 走 RenderTransform 平移，不触碰布局，抖动过程不影响周围元素的位置。
/// </summary>
public static class Shake
{
	private const double DurationMs = 400;

	/// <summary>绑定要监视的提示文本（非空字符串 = 有问题），或布尔值。</summary>
	public static readonly DependencyProperty OnProperty = DependencyProperty.RegisterAttached(
		"On", typeof(object), typeof(Shake), new PropertyMetadata(null, OnChanged));

	public static void SetOn(DependencyObject element, object? value) => element.SetValue(OnProperty, value);

	/// <summary>上一次是否已处于「有问题」状态。</summary>
	private static readonly DependencyProperty WasAlertProperty = DependencyProperty.RegisterAttached(
		"WasAlert", typeof(bool), typeof(Shake), new PropertyMetadata(false));

	/// <summary>抖动用的平移层，与元素自身的 RenderTransform 并存。</summary>
	private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
		"Offset", typeof(TranslateTransform), typeof(Shake), new PropertyMetadata(null));

	private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not FrameworkElement target) return;

		bool alert = e.NewValue switch
		{
			null => false,
			string s => s.Length > 0,
			bool b => b,
			_ => true,
		};

		bool was = (bool)target.GetValue(WasAlertProperty);
		target.SetValue(WasAlertProperty, alert);
		if (alert && !was) Play(target);
	}

	private static void Play(FrameworkElement target)
	{
		TranslateTransform offset = GetOffset(target);
		offset.BeginAnimation(TranslateTransform.XProperty, null);
		offset.X = 0;

		// 左右各一次、幅度递减：像撞了一下停住，而不是来回晃
		double[] steps = [0, -7, 6, -5, 4, -2, 1, 0];
		var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(DurationMs) };
		for (int i = 0; i < steps.Length; i++)
		{
			animation.KeyFrames.Add(new LinearDoubleKeyFrame(
				steps[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(DurationMs * i / (steps.Length - 1)))));
		}
		offset.BeginAnimation(TranslateTransform.XProperty, animation);
	}

	/// <summary>取（必要时建）抖动平移层：包一层 TransformGroup，保留元素原有的变换。</summary>
	private static TranslateTransform GetOffset(FrameworkElement target)
	{
		if (target.GetValue(OffsetProperty) is TranslateTransform existing) return existing;

		var offset = new TranslateTransform();
		var group = new TransformGroup();
		if (target.RenderTransform is { } current && !ReferenceEquals(current, Transform.Identity))
			group.Children.Add(current);
		group.Children.Add(offset);
		target.RenderTransform = group;
		target.SetValue(OffsetProperty, offset);
		return offset;
	}
}

/// <summary>
/// 交给控件模板做「悬停底色淡入」的附加属性。模板里覆一层填该色的透明层，
/// 鼠标进入 / 离开时只动画这一层的 Opacity，于是底色变化是渐变的。
/// 之所以不直接对底色做 ColorAnimation：主题画刷经 Style + DynamicResource 下发后会被 WPF 冻结，
/// 对已冻结画刷调 BeginAnimation(ColorProperty) 会抛「对象已密封或已冻结」。
/// 用静态 Setter 而非触发器下发，派生样式（主按钮 / 危险按钮）覆盖时行为更直观。
/// </summary>
public static class HoverFade
{
	public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
		"Background", typeof(Brush), typeof(HoverFade), new FrameworkPropertyMetadata(null));

	public static Brush? GetBackground(DependencyObject element) => (Brush?)element.GetValue(BackgroundProperty);

	public static void SetBackground(DependencyObject element, Brush? value) => element.SetValue(BackgroundProperty, value);
}

/// <summary>
/// 给 Visibility 加淡入淡出：绑到本属性（而不是直接绑 Visibility）后，
/// 转为可见时先置为 Visible 再从当前透明度渐显；转为不可见时先渐隐，动画结束后才真正折叠。
/// 之所以不能直接对 Visibility 做动画：Visibility 不是可动画属性，且折叠与动画同时发生的话，
/// 淡出那一半永远看不到。用法上元素自身要给初始 <c>Visibility="Collapsed" Opacity="0"</c>。
/// </summary>
public static class FadeVisibility
{
	private const int FadeInMs = 160;
	private const int FadeOutMs = 120;

	/// <summary>默认取 Collapsed（与元素初始值一致）：首帧不产生多余的淡出，也不会把本该显示的空状态漏掉。</summary>
	public static readonly DependencyProperty VisibleProperty = DependencyProperty.RegisterAttached(
		"Visible", typeof(Visibility), typeof(FadeVisibility),
		new PropertyMetadata(Visibility.Collapsed, OnVisibleChanged));

	/// <summary>每次淡入 / 淡出递增，用来识别「本次动画是否已被后来的动画取代」。</summary>
	private static readonly DependencyProperty TokenProperty = DependencyProperty.RegisterAttached(
		"Token", typeof(int), typeof(FadeVisibility), new PropertyMetadata(0));

	/// <summary>
	/// 最近一次请求的方向：0 = 尚未收到过请求，1 = 期望显示，-1 = 期望隐藏。
	/// 不能拿 element.Visibility 当方向依据 —— 淡出动画在途时元素仍是 Visible（折叠被刻意延后到动画结束），
	/// 此时再来一次「显示」请求会被误判成「已经是可见的，无需处理」，在途的折叠收尾于是照常执行，元素最终错误地消失。
	/// </summary>
	private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
		"State", typeof(int), typeof(FadeVisibility), new PropertyMetadata(0));

	public static Visibility GetVisible(DependencyObject element) => (Visibility)element.GetValue(VisibleProperty);

	public static void SetVisible(DependencyObject element, Visibility value) => element.SetValue(VisibleProperty, value);

	private static void OnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not UIElement element) return;

		int desired = (Visibility)e.NewValue == Visibility.Visible ? 1 : -1;
		if ((int)element.GetValue(StateProperty) == desired) return;
		element.SetValue(StateProperty, desired);

		if (desired == 1)
		{
			element.Visibility = Visibility.Visible;
			// 基准值先归位到 1：动画用 FillBehavior.Stop，结束后自然落到基准值，不会闪一帧
			Start(element, 1.0, FadeInMs, null);
			return;
		}

		if (element.Visibility != Visibility.Visible)
		{
			element.Opacity = 0;
			return;
		}
		// 基准值先归位到 0：动画结束时元素已不可见，收尾里再折叠，同样不会闪
		Start(element, 0.0, FadeOutMs, () => element.Visibility = Visibility.Collapsed);
	}

	private static void Start(UIElement element, double target, int milliseconds, Action? whenDone)
	{
		double from = element.Opacity;
		element.Opacity = target;

		int token = (int)element.GetValue(TokenProperty) + 1;
		element.SetValue(TokenProperty, token);

		var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(milliseconds))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop,
		};
		animation.Completed += (_, _) =>
		{
			// 期间又被切成另一个方向时，本次动画已被顶掉，收尾交给新的那次
			if ((int)element.GetValue(TokenProperty) == token) whenDone?.Invoke();
		};
		element.BeginAnimation(UIElement.OpacityProperty, animation);
	}
}

/// <summary>非空字符串 → 可见（用于正则错误等条件提示）。</summary>
public sealed class NotEmptyVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		=> string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
