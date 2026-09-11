using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RenameTool.Models;
using RenameTool.ViewModels;

namespace RenameTool;

/// <summary>主窗口代码后置：仅承载 XAML 声明的事件处理器，业务逻辑在 MainViewModel。</summary>
public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
		UpdateThemeIcon();
		UpdateMaxIcon();
		StateChanged += (_, _) => UpdateMaxIcon();
		Loaded += OnLoaded;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 规则类型下拉默认选中第一项，方便直接点“＋添加”
		if (RuleTypeCombo.Items.Count > 0 && RuleTypeCombo.SelectedIndex < 0)
			RuleTypeCombo.SelectedIndex = 0;
	}

	private static MainViewModel? Vm => Application.Current.MainWindow?.DataContext as MainViewModel;

	/// <summary>日间显示月亮（点击进入夜间），夜间显示太阳（点击回到日间）。</summary>
	private void UpdateThemeIcon()
	{
		string key = App.IsDarkMode ? "Icon.Sun" : "Icon.Moon";
		if (Application.Current?.TryFindResource(key) is System.Windows.Media.Geometry geo)
			ThemeIconPath.Data = geo;
		ThemeButton.ToolTip = App.IsDarkMode ? "切换到日间模式" : "切换到夜间模式";
	}

	/// <summary>切换日间 / 夜间主题。</summary>
	private void ThemeButton_Click(object sender, RoutedEventArgs e)
	{
		App.SetDarkMode(!App.IsDarkMode);
		UpdateThemeIcon();
	}

	// ─────────────── 自绘标题栏：拖动与窗口控制 ───────────────

	/// <summary>最大化状态下按钮显示“还原”图标，否则显示“最大化”。</summary>
	private void UpdateMaxIcon()
	{
		string key = WindowState == WindowState.Maximized ? "Icon.WindowRestore" : "Icon.WindowMax";
		if (Application.Current?.TryFindResource(key) is System.Windows.Media.Geometry geo)
			MaxIconPath.Data = geo;
	}

	/// <summary>按住标题栏空白处拖动窗口；双击切换最大化 / 还原（按钮区域除外）。</summary>
	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (IsWithinButton(e.OriginalSource as DependencyObject)) return;
		if (e.ClickCount == 2)
		{
			ToggleMaximize();
			return;
		}
		if (WindowState == WindowState.Normal) DragMove();
	}

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

	/// <summary>清空执行日志。</summary>
	private void ClearLogButton_Click(object sender, RoutedEventArgs e)
		=> Vm?.ClearLogCommand.Execute(null);

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

	// ─────────────── 文件列表：文件夹分组的批量勾选 ───────────────

	/// <summary>分组表头加载：绑定三态勾选，并随组内文件的勾选变化刷新显示。</summary>
	private void GroupHeader_Loaded(object sender, RoutedEventArgs e)
	{
		if (sender is not CheckBox box || box.DataContext is not CollectionViewGroup group) return;

		void Refresh() => box.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
		void OnItemChanged(object? s, PropertyChangedEventArgs pe)
		{
			if (pe.PropertyName == nameof(FileItem.Selected)) Refresh();
		}

		foreach (object item in group.Items)
			if (item is FileItem file) file.PropertyChanged += OnItemChanged;

		void OnUnloaded(object? s, RoutedEventArgs e2)
		{
			foreach (object item in group.Items)
				if (item is FileItem file) file.PropertyChanged -= OnItemChanged;
			box.Unloaded -= OnUnloaded;
		}
		box.Unloaded += OnUnloaded;
	}

	/// <summary>点击文件夹级复选框：批量选中 / 取消选择该文件夹下的全部文件。</summary>
	private void GroupCheckBox_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not CheckBox { DataContext: CollectionViewGroup group } box) return;
		bool select = box.IsChecked == true;   // 点击后已切换为最终意图
		foreach (object item in group.Items)
			if (item is FileItem file && !file.IsMissing) file.Selected = select;
		box.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
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
