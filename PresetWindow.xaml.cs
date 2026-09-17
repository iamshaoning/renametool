using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RenameTool.Services;
using RenameTool.ViewModels;

namespace RenameTool;

/// <summary>规则预设面板：保存当前规则链为预设，或载入 / 删除已有预设。</summary>
public partial class PresetWindow : ToolWindow
{
	public PresetWindow()
	{
		InitializeComponent();
		// DataContext 由主窗口在 ShowDialog 前注入，故等 Loaded 再订阅集合变化
		Loaded += OnLoaded;
		Closed += OnClosed;
	}

	private MainViewModel? Vm => DataContext as MainViewModel;

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		NameBox.Focus();
		if (Vm is { } vm) vm.Presets.CollectionChanged += OnPresetsChanged;
	}

	// 窗口每次都是新建的，关闭时必须退订，否则 ViewModel 会一直持有已关闭窗口的处理器
	private void OnClosed(object? sender, EventArgs e)
	{
		if (Vm is { } vm) vm.Presets.CollectionChanged -= OnPresetsChanged;
	}

	/// <summary>新增预设时让该行淡入；删除时没有插入淡出的时机（见下），退而让剩余各行平滑上移补位。</summary>
	private void OnPresetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add)
		{
			RowAnimator.AnimateEnter(PresetList, e.NewItems);
			return;
		}
		// 删除 / 清空由命令直达 ViewModel，且中间夹着二次确认弹窗 —— 若在确认前就把行淡出，
		// 用户一旦取消，行已经看不见了。所以这里只处理「剩余各行上移补位」，不插入淡出。
		if (e.Action == NotifyCollectionChangedAction.Remove) RowAnimator.PrepareShift(PresetList);
	}

	/// <summary>点击已有预设时把它选中，并自动把名称带入输入框，便于覆盖保存（第8项）。</summary>
	private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (PresetList.SelectedItem is RulePreset preset) NameBox.Text = preset.Name;
	}

	/// <summary>把当前规则链保存为预设。</summary>
	private void SavePreset_Click(object sender, RoutedEventArgs e)
	{
		string name = NameBox.Text.Trim();
		if (name.Length == 0)
		{
			AppDialog.Notify(this, "规则预设", "请先输入预设名称，再点击保存。", DialogIcon.Info);
			NameBox.Focus();
			return;
		}
		// 同名时 SaveCurrentAsPreset 内部会弹窗确认覆盖，取消则不保存、不清空输入框
		if (Vm?.SaveCurrentAsPreset(name) == true) NameBox.Clear();
	}

	/// <summary>在名称框中按回车即保存。</summary>
	private void NameBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter) SavePreset_Click(sender, e);
	}
}
