using System.Collections.Specialized;
using System.Windows;
using RenameTool.ViewModels;

namespace RenameTool;

/// <summary>改名历史面板：列出历史批次并支持整体回滚。</summary>
public partial class HistoryWindow : ToolWindow
{
	public HistoryWindow()
	{
		InitializeComponent();
		// DataContext 由主窗口在 ShowDialog 前注入，故等 Loaded 再订阅集合变化
		Loaded += OnLoaded;
		Closed += OnClosed;
	}

	private MainViewModel? Vm => DataContext as MainViewModel;

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (Vm is { } vm) vm.History.CollectionChanged += OnHistoryChanged;
	}

	// 窗口每次都是新建的，关闭时必须退订，否则 ViewModel 会一直持有已关闭窗口的处理器
	private void OnClosed(object? sender, EventArgs e)
	{
		if (Vm is { } vm) vm.History.CollectionChanged -= OnHistoryChanged;
	}

	/// <summary>新批次总是插在列表最前面：让该行淡入，同时让下方各行平滑下移补位。</summary>
	private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action != NotifyCollectionChangedAction.Add) return;
		// 此刻尚未重新布局，PrepareShift 记录到的仍是各行的旧位置，布局更新后再动画归位
		RowAnimator.PrepareShift(HistoryListBox);
		RowAnimator.AnimateEnter(HistoryListBox, e.NewItems);
	}

	/// <summary>清空历史（现有行整体淡出后再真正清空）。</summary>
	private void ClearHistory_Click(object sender, RoutedEventArgs e)
	{
		if (Vm is not { } vm) return;
		RowAnimator.AnimateRemoval(HistoryListBox, vm.History.Cast<object>().ToList(), () => vm.ClearHistoryCommand.Execute(null));
	}
}
