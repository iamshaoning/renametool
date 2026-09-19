using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RenameTool;

/// <summary>
/// 非模态的进度提示窗口：用于耗时较长的导入扫描与批量改名。
/// 与 AppDialog 不同，它不阻塞调用方（<see cref="Open"/> 内部走 Show 而非 ShowDialog），
/// 后台线程只写入共享状态，窗口自身用计时器按固定节奏刷新界面，避免逐条跨线程调度。
/// 用户点「中断」（或直接关闭窗口）即通过 <see cref="Token"/> 请求取消。
/// 调用方可以只在操作确实耗时（超过预设阈值）时才 <see cref="Open"/>，避免窗口一闪而过。
/// </summary>
public sealed partial class ProgressWindow : ToolWindow
{
	private const int RefreshIntervalMs = 90;

	private readonly CancellationTokenSource _cts = new();
	private readonly DispatcherTimer _timer;
	private readonly bool _indeterminate;
	// System.Threading.Lock（.NET 9 起）：lock 语句会直接绑定到 Lock.EnterScope()，
	// 不再经过 Monitor。此处临界区极短，收益主要是意图明确而非性能。
	private readonly Lock _sync = new();

	// 后台线程写入 / UI 线程读取，统一由 _sync 保护
	private string _message;
	private int _current;
	private int _total;
	private bool _dirty;

	private bool _shown;
	private bool _finishing;
	private bool _closed;

	public ProgressWindow(string title, string message, bool indeterminate)
	{
		InitializeComponent();

		TitleText.Text = title;
		_message = message;
		MessageText.Text = message;
		_indeterminate = indeterminate;
		Bar.Visibility = indeterminate ? Visibility.Collapsed : Visibility.Visible;
		RunnerTrack.Visibility = indeterminate ? Visibility.Visible : Visibility.Collapsed;
		CounterText.Text = indeterminate ? "已扫描 0 个文件" : "";

		// 基类 ToolWindow 已调用 EnableRoundedWithShadow / EnableFadeTransition。
		// 这里必须挂在基类构造函数之后：淡入淡出会把首次 Closing 拦下来播退场动画，
		// 于是「用户直接关窗」也会先经过这里，等价于一次中断请求。
		Closing += Window_Closing;

		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
		_timer.Tick += (_, _) => Flush();
		_timer.Start();
	}

	/// <summary>取消令牌：交给后台任务用于随时中止。</summary>
	public CancellationToken Token => _cts.Token;

	/// <summary>
	/// 显示窗口（由调用方在界面线程上调用）。之所以不直接用 <see cref="Window.Show"/>，
	/// 是为了记住“从未显示”这一状态：耗时很短的操作可以直接 <see cref="Finish"/>，
	/// 不必先显示再关闭。
	/// </summary>
	public void Open()
	{
		if (_shown) return;
		_shown = true;
		Show();
	}

	/// <summary>已知总量的进度回报（可跨线程调用）。</summary>
	public void Report(string message, int current, int total)
	{
		lock (_sync)
		{
			_message = message;
			_current = current;
			_total = total;
			_dirty = true;
		}
	}

	/// <summary>未知总量的进度回报（可跨线程调用）：只累计已完成的数量。</summary>
	public void ReportScan(string message, int scanned)
	{
		lock (_sync)
		{
			_message = message;
			_current = scanned;
			_dirty = true;
		}
	}

	/// <summary>
	/// 流程结束并关窗。可重复调用：若窗口从未显示（操作太快，无需进度提示）或已经关闭，
	/// 则只做清理，不再触达窗口生命周期。退场动画结束前窗口仍会短暂可见，但不阻塞调用方。
	/// </summary>
	public void Finish()
	{
		if (_finishing) return;
		_finishing = true;
		_timer.Stop();
		if (!_shown || _closed)
		{
			_closed = true;
			_cts.Dispose();
			return;
		}
		Close();
	}

	private void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (e.Cancel)
		{
			// 首次 Closing 被退场动画拦下：若并非我方主动收尾，则视为用户中断
			if (!_finishing) RequestCancel();
		}
		else
		{
			_closed = true;
		}
	}

	private void Window_Closed(object? sender, EventArgs e)
	{
		_timer.Stop();
		_closed = true;
	}

	private void Flush()
	{
		string message;
		int current, total;
		lock (_sync)
		{
			if (!_dirty) return;
			_dirty = false;
			message = _message;
			current = _current;
			total = _total;
		}

		MessageText.Text = message;
		if (_indeterminate)
		{
			CounterText.Text = $"已扫描 {current} 个文件";
			return;
		}

		int max = Math.Max(total, 1);
		Bar.Maximum = max;
		Bar.Value = Math.Min(current, max);
		CounterText.Text = total > 0 ? $"{current} / {total}" : "";
	}

	private void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

	private void RequestCancel()
	{
		if (_cts.IsCancellationRequested) return;
		_cts.Cancel();
		CancelButton.IsEnabled = false;
		CancelButton.Content = "正在中断…";
		MessageText.Text = "正在中断，请稍候…";
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		if (!_indeterminate) return;
		double track = RunnerTrack.ActualWidth;
		if (track <= 0) track = 398;
		RunnerOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
		{
			From = -RunnerBar.Width,
			To = track,
			Duration = TimeSpan.FromMilliseconds(1100),
			RepeatBehavior = RepeatBehavior.Forever,
			EasingFunction = null,
		});
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_cts.Dispose();
	}
}
