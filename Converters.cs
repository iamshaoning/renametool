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

public sealed class IssueBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var issue = value is PreviewIssue i ? i : PreviewIssue.None;
		return issue switch
		{
			PreviewIssue.None => Brushes.Transparent,
			PreviewIssue.Conflict => (Brush)Application.Current.FindResource("WarningSoftBrush"),
			_ => (Brush)Application.Current.FindResource("DangerSoftBrush"),
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
			PreviewIssue.Conflict => (Brush)Application.Current.FindResource("WarningBrush"),
			_ => (Brush)Application.Current.FindResource("DangerBrush"),
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
			"success" => (Brush)Application.Current.FindResource("SuccessBrush"),
			"failed" => (Brush)Application.Current.FindResource("DangerBrush"),
			_ => (Brush)Application.Current.FindResource("MutedTextBrush"),
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
		// 只平滑纵向滚动条：横向滑块的“长度”是宽度，套用同一套高度动画会把它压成一小块
		if (thumb.TemplatedParent is not ScrollBar { Orientation: Orientation.Vertical }) return;
		// 滑块本身是模板里那个具名 Border（Thumb 模板根是 Canvas，中间还夹着一层定位用 Grid，
		// 所以不能再用「第一个子元素」去取）
		if (thumb.ActualHeight < 0.5) return;
		if (thumb.Template?.FindName("Th", thumb) is not FrameworkElement bar) return;

		// 首次上屏时可视条还没被赋过长度：直接落位，避免滑块从 0 长出来
		if (double.IsNaN(bar.Height))
		{
			bar.Height = thumb.ActualHeight;
			return;
		}

		// 不给 From：动画自动从当前长度（含正在进行的动画的当前值）出发，连续两次变化也能接得上
		bar.BeginAnimation(FrameworkElement.HeightProperty,
			new DoubleAnimation(thumb.ActualHeight, TimeSpan.FromMilliseconds(DurationMs))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			});
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
