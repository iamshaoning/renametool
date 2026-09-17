using System.Windows;
using System.Windows.Input;

namespace RenameTool;

/// <summary>
/// 二级窗口（历史 / 预设 / 关于 / 提示框 / 进度）的公共基类，抽取各窗口原本逐份重复的：
/// 「DWM 圆角投影 + 淡入淡出」初始化、ESC 关闭、拖动窗口、关闭按钮回调。
/// 各窗口只需在 XAML 里沿用同名事件处理方法即可，无需再写一行实现。
/// </summary>
public class ToolWindow : Window
{
	protected ToolWindow()
	{
		// 两者都允许在构造期调用：此时句柄尚未创建，内部会等到 SourceInitialized 再应用
		WindowEffects.EnableRoundedWithShadow(this);
		WindowEffects.EnableFadeTransition(this);
	}

	/// <summary>按 ESC 关闭窗口（二级窗口统一行为）。</summary>
	protected virtual void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Escape) return;
		e.Handled = true;
		Close();
	}

	/// <summary>
	/// 拖动自绘标题栏前的拦截判定：返回 true 表示本次按下不用于拖动。
	/// 例如主窗口要排除标题栏右侧的最小化 / 最大化 / 关闭按钮区域，否则按下按钮会连带把窗口拖走。
	/// </summary>
	protected virtual bool BlockTitleBarDrag(MouseButtonEventArgs e) => false;

	/// <summary>是否允许拖动窗口。主窗口在最大化状态下不拖动（与系统行为一致）。</summary>
	protected virtual bool CanDragTitleBar => true;

	/// <summary>双击标题栏时的行为。基类不处理，由主窗口覆写为最大化 / 还原切换。</summary>
	protected virtual void OnTitleBarDoubleClick()
	{
	}

	/// <summary>拖动自绘标题栏移动窗口（五个窗口共用；差异部分由上面三个钩子覆写）。</summary>
	protected void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (BlockTitleBarDrag(e)) return;
		if (e.ClickCount == 2)
		{
			OnTitleBarDoubleClick();
			return;
		}
		if (CanDragTitleBar) DragMove();
	}

	/// <summary>按住窗口任意位置拖动（没有标题栏的进度窗使用）。</summary>
	protected void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState != MouseButtonState.Pressed) return;
		try { DragMove(); } catch (InvalidOperationException) { /* 鼠标已抬起，忽略 */ }
	}

	/// <summary>关闭当前窗口（标题栏的 × 与底部「关闭」按钮共用）。</summary>
	protected void Close_Click(object sender, RoutedEventArgs e) => Close();
}
