using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RenameTool.Engine;
using RenameTool.Models;
using RenameTool.Services;

namespace RenameTool.ViewModels;

public enum PreviewFilter { All, Affected, Conflict }

/// <summary>轻提示类型：决定配色与停留时长（成功 2s / 警告 3s / 报错 5s）。</summary>
public enum ToastKind { Success, Warning, Error }

/// <summary>
/// 一条非阻塞通知。多条通知可同时并列显示：各自独立计时、独立关闭，
/// 到点或被点击时先由视图播放退场动画，动画结束后再从列表移除（其余条目自动上移补位）。
/// </summary>
public sealed class ToastItem
{
	/// <summary>
	/// 通知正文每行可容纳的全角字数：卡片宽 420 − 左右内边距 36 − 左侧竖条 21 − 图标 61 ≈ 302，
	/// 正文 13.5 号字，302 / 13.5 ≈ 22，取 21 留余量。超过这个宽度就均衡折行，避免长句挤成一条。
	/// </summary>
	private const double ToastTextUnitsPerLine = 21;

	private readonly DispatcherTimer _timer;
	private readonly Action<ToastItem> _dismiss;
	private bool _dismissed;

	internal ToastItem(string message, ToastKind kind, Action<ToastItem> dismiss)
	{
		Message = TextFlow.Balance(message, ToastTextUnitsPerLine);
		Kind = kind;
		_dismiss = dismiss;
		DismissCommand = new RelayCommand(RequestDismiss);
		_timer = new DispatcherTimer
		{
			Interval = kind switch
			{
				ToastKind.Success => TimeSpan.FromSeconds(2),
				ToastKind.Warning => TimeSpan.FromSeconds(3),
				_ => TimeSpan.FromSeconds(5),
			},
		};
		_timer.Tick += (_, _) => RequestDismiss();
		_timer.Start();
	}

	/// <summary>通知正文。</summary>
	public string Message { get; }

	/// <summary>通知类型：决定配色与自动关闭时长。</summary>
	public ToastKind Kind { get; }

	/// <summary>点击整条通知即关闭它。</summary>
	public RelayCommand DismissCommand { get; }

	/// <summary>是否为警告样式（黄色，用于文件名冲突等）。</summary>
	public bool ToastIsWarning => Kind == ToastKind.Warning;

	/// <summary>是否为报错样式（红色，用于文件被占用 / 不存在等严重错误）。</summary>
	public bool ToastIsError => Kind == ToastKind.Error;

	/// <summary>通知标题：随类型变化。</summary>
	public string ToastTitle => Kind switch
	{
		ToastKind.Error => "操作失败",
		ToastKind.Warning => "需要注意",
		_ => "操作成功",
	};

	/// <summary>停掉计时器（条目已被移除时调用）。</summary>
	internal void Stop() => _timer.Stop();

	/// <summary>请求关闭：计时到点与用户点击可能几乎同时发生，这里保证只生效一次。</summary>
	private void RequestDismiss()
	{
		if (_dismissed) return;
		_dismissed = true;
		_timer.Stop();
		_dismiss(this);
	}
}

/// <summary>主窗口 ViewModel：文件/规则/预览/执行。</summary>
public sealed class MainViewModel : ObservableObject
{
	private readonly RenameService _service = new();

	/// <summary>因 Ctrl+Z 撤销而被标记回滚的历史批次（后进先出），供重做时还原标记。</summary>
	private readonly Stack<HistoryEntry> _redoHistory = new();

	public ObservableCollection<FileItem> Files { get; } = [];
	/// <summary>文件列表 UI 绑定的可见子集（跟随搜索过滤）。</summary>
	public ObservableCollection<FileItem> FilesView { get; } = [];

	private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 文件列表的顶层文件夹模块。每个文件夹都是独立模块，模块自带一个容器承载其下的文件与子文件夹，
	/// 子文件夹又自带容器，可层层嵌套；折叠时只需把该模块的容器整体卷起。
	/// </summary>
	public ObservableCollection<FolderNode> RootFolders { get; } = [];
	public ObservableCollection<RenameRule> Rules { get; } = [];
	public ObservableCollection<PreviewItem> Previews { get; } = [];
	public ObservableCollection<LogItem> Logs { get; } = [];

	/// <summary>已保存的规则预设（跨会话持久化）。</summary>
	public ObservableCollection<RulePreset> Presets { get; } = [];
	/// <summary>历史改名批次（跨会话持久化，支持整体回滚）。</summary>
	public ObservableCollection<HistoryEntry> History { get; } = [];

	private PreviewFilter _previewFilter = PreviewFilter.All;
	public PreviewFilter PreviewFilter
	{
		get => _previewFilter;
		set { if (SetProperty(ref _previewFilter, value)) RefreshPreviewList(); }
	}

	private string _filterText = "";
	public string FilterText
	{
		get => _filterText;
		set
		{
			if (SetProperty(ref _filterText, value)) { ScheduleFilter(); }
		}
	}

	private bool _isExecuting;
	public bool IsExecuting
	{
		get => _isExecuting;
		set
		{
			if (SetProperty(ref _isExecuting, value))
			{
				// 这些判断只作为命令谓词使用（未直接绑定），须显式通知命令重新评估。
				ImportFilesCommand.RaiseCanExecuteChanged();
				ImportFolderCommand.RaiseCanExecuteChanged();
				RefreshCommand.RaiseCanExecuteChanged();
				ClearCommand.RaiseCanExecuteChanged();
				DeleteSelectedCommand.RaiseCanExecuteChanged();
				ExecuteCommand.RaiseCanExecuteChanged();
				// 执行状态变化后重新评估撤销/重做：
				// HistoryChanged 只在执行过程中触发（此时 IsExecuting 仍为 true），
				// 这里补一次评估，确保执行结束后按钮能正确启用。
				UndoCommand.RaiseCanExecuteChanged();
				RedoCommand.RaiseCanExecuteChanged();
				RetryFailedCommand.RaiseCanExecuteChanged();
				RaisePropertyChanged(nameof(CanRetryFailed));
				RaisePropertyChanged(nameof(CanClearMissing));
				RaisePropertyChanged(nameof(ExecuteTooltip));
			}
		}
	}

	private int _progressCurrent;
	public int ProgressCurrent
	{
		get => _progressCurrent;
		private set
		{
			if (SetProperty(ref _progressCurrent, value))
			{
				RaisePropertyChanged(nameof(ProgressPercent));
				RaisePropertyChanged(nameof(HasProgress));
			}
		}
	}

	private int _progressTotal;
	public int ProgressTotal
	{
		get => _progressTotal;
		private set
		{
			if (SetProperty(ref _progressTotal, value))
			{
				RaisePropertyChanged(nameof(HasProgress));
				RaisePropertyChanged(nameof(ProgressPercent));
			}
		}
	}

	public int ProgressPercent => ProgressTotal <= 0 ? 0 : (int)(ProgressCurrent * 100d / ProgressTotal);
	public bool HasProgress => ProgressTotal > 0;

	/// <summary>
	/// 进度条左侧的状态文字。完成后进度会在满格上停留一小段时间（见 ProgressHoldMs），
	/// 那一刻活已经干完，再写「执行中…」就是在骗人，故用属性单独承载文案。
	/// </summary>
	public string ProgressText
	{
		get => _progressText;
		private set => SetProperty(ref _progressText, value);
	}
	private string _progressText = "执行中…";

	// 派生统计
	private int _affected;
	private int _conflicts;
	public int Conflicts => _conflicts;
	public int OkChangeCount => _affected - _conflicts;
	public int FileTotal => Files.Count;

	public string FileTotalText => $"共 {FileTotal} 个文件";
	public string SelectedText => $"{FilesView.Count(f => f.Selected)} / {FilesView.Count}";

	/// <summary>
	/// 是否已有勾选的文件。文件列表的「全选 / 全不选」合并为同一个按钮：
	/// 没有任何勾选时显示「全选」，已有勾选（部分或全部）时显示「全不选」。
	/// </summary>
	public bool AnyFileSelected => FilesView.Any(f => f.Selected);

	// 注意：这里刻意不把「中止执行 + 存在冲突」算作不可执行。
	// 否则按钮直接被置灰，用户只能看到灰按钮与提示文字，点击后“已取消执行”的说明永远不会触发。
	// 现在保留可点击状态，由 Execute() 体检后弹出明确的中止原因，策略语义才真正闭环。
	public bool CanExecute => !IsExecuting && _affected > 0;
	public bool CanImport => !IsExecuting;

	/// <summary>是否存在可重试的失败项：文件仍在磁盘上且当前未在执行。同时控制按钮的显隐与可用。</summary>
	public bool CanRetryFailed => !IsExecuting && Logs.Any(l => l.Status == "failed" && l.File is { IsMissing: false });

	/// <summary>“开始重命名”按钮的即时提示，说明当前策略与可执行性。</summary>
	public string ExecuteTooltip
	{
		get
		{
			if (IsExecuting) return "正在执行改名…";
			if (_affected == 0) return "当前没有需要改名的文件";
			if (Policy == ConflictPolicy.Abort && _conflicts > 0)
				return $"存在 {_conflicts} 处冲突，点击后将按“中止执行”策略取消本次改名；请改用“跳过冲突项”或“自动加序号”（快捷键 Ctrl+Enter）";
			return Policy switch
			{
				ConflictPolicy.AutoNumber => "执行改名；重名文件将自动追加序号（Ctrl+Enter）",
				ConflictPolicy.Skip => "执行改名；存在冲突的文件将被跳过（Ctrl+Enter）",
				_ => "执行改名（Ctrl+Enter）",
			};
		}
	}

	/// <summary>状态栏任务摘要：实时反映当前规则将改名多少项、其中多少项存在命名问题。</summary>
	public string TaskSummary =>
		_affected == 0
			? "按当前规则无文件需要改名"
			: _conflicts == 0
				? $"预计改名 {_affected} 项 · 全部可执行"
				: $"预计改名 {_affected} 项 · 可执行 {OkChangeCount} 项 · 冲突 {_conflicts} 项";

	/// <summary>存在失效条目时才允许“清除失效”。</summary>
	public bool CanClearMissing => !IsExecuting && Files.Any(f => f.IsMissing);
	public bool HasFiles => Files.Count > 0;
	public bool HasRules => Rules.Count > 0;
	public bool HasPreviews => Previews.Count > 0;
	public bool HasLogs => Logs.Count > 0;

	/// <summary>
	/// 是否已有启用的规则。规则区的「全启用 / 全停用」合并为同一个按钮：
	/// 没有任何启用项时显示「全启用」，已有启用项（部分或全部）时显示「全停用」。
	/// </summary>
	public bool AnyRuleEnabled => Rules.Any(r => r.Enabled);
	public bool HasPresets => Presets.Count > 0;
	public bool HasHistory => History.Count > 0;

	// 命令
	public RelayCommand ImportFilesCommand { get; }
	public RelayCommand ImportFolderCommand { get; }
	public RelayCommand RefreshCommand { get; }
	public RelayCommand ClearCommand { get; }
	public RelayCommand SelectAllCommand { get; }
	public RelayCommand SelectNoneCommand { get; }
	public RelayCommand DeleteSelectedCommand { get; }
	public RelayCommand ExecuteCommand { get; }
	public RelayCommand UndoCommand { get; }
	public RelayCommand RedoCommand { get; }
	public RelayCommand ClearLogCommand { get; }
	public RelayCommand<RuleType> AddRuleCommand { get; }
	public RelayCommand<RenameRule> RemoveRuleCommand { get; }
	public RelayCommand<RenameRule> DuplicateRuleCommand { get; }
	public RelayCommand<RenameRule> MoveRuleUpCommand { get; }
	public RelayCommand<RenameRule> MoveRuleDownCommand { get; }
	public RelayCommand EnableAllRulesCommand { get; }
	public RelayCommand DisableAllRulesCommand { get; }
	public RelayCommand ClearAllRulesCommand { get; }
	public RelayCommand<FileItem> RemoveFileCommand { get; }
	public RelayCommand<FolderNode> RemoveFolderCommand { get; }
	public RelayCommand<PreviewItem> LockPreviewNameCommand { get; }
	public RelayCommand<PreviewItem> UnlockPreviewNameCommand { get; }
	public RelayCommand<RulePreset> LoadPresetCommand { get; }
	public RelayCommand<RulePreset> DeletePresetCommand { get; }
	public RelayCommand ImportPresetsCommand { get; }
	public RelayCommand ExportPresetsCommand { get; }
	public RelayCommand ClearPresetsCommand { get; }
	public RelayCommand<HistoryEntry> RollbackHistoryCommand { get; }
	public RelayCommand ClearHistoryCommand { get; }
	public RelayCommand RetryFailedCommand { get; }

	public IReadOnlyList<Option> SortModes { get; } =
	[
		new() { Value = SortMode.Import, Label = "导入顺序" },
		new() { Value = SortMode.NameAsc, Label = "名称 A-Z" },
		new() { Value = SortMode.NameDesc, Label = "名称 Z-A" },
		new() { Value = SortMode.ExtAsc, Label = "扩展名 A-Z" },
		new() { Value = SortMode.ExtDesc, Label = "扩展名 Z-A" },
	];

	public IReadOnlyList<Option> PreviewFilters { get; } =
	[
		new() { Value = PreviewFilter.All, Label = "全部" },
		new() { Value = PreviewFilter.Affected, Label = "有变化" },
		new() { Value = PreviewFilter.Conflict, Label = "冲突" },
	];

	public IReadOnlyList<Option> ConflictPolicies { get; } =
	[
		new() { Value = ConflictPolicy.Skip, Label = "跳过冲突项" },
		new() { Value = ConflictPolicy.AutoNumber, Label = "自动加序号" },
		new() { Value = ConflictPolicy.Abort, Label = "中止执行" },
	];

	private ConflictPolicy _policy = ConflictPolicy.Skip;
	/// <summary>执行前体检发现命名冲突时的处理策略。</summary>
	public ConflictPolicy Policy
	{
		get => _policy;
		set
		{
			if (!SetProperty(ref _policy, value)) return;
			RaisePropertyChanged(nameof(ExecuteTooltip));
			ExecuteCommand.RaiseCanExecuteChanged();
		}
	}

	private SortMode _sortMode;
	public SortMode SortMode
	{
		get => _sortMode;
		set
		{
			if (SetProperty(ref _sortMode, value)) { Resort(); }
		}
	}

	public MainViewModel()
	{
		ImportFilesCommand = new RelayCommand(ImportFiles, () => CanImport);
		ImportFolderCommand = new RelayCommand(ImportFolder, () => CanImport);
		RefreshCommand = new RelayCommand(async () => await RefreshFromSources(), () => CanImport && Files.Count > 0);
		ClearCommand = new RelayCommand(ClearMissingFiles, () => CanClearMissing);
		SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => Files.Count > 0);
		SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => Files.Count > 0);
		DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => !IsExecuting && Files.Any(f => f.Selected));
		ExecuteCommand = new RelayCommand(Execute, () => CanExecute);
		UndoCommand = new RelayCommand(Undo, () => _service.CanUndo && !IsExecuting);
		RedoCommand = new RelayCommand(Redo, () => _service.CanRedo && !IsExecuting);
		ClearLogCommand = new RelayCommand(() => Logs.Clear());
		AddRuleCommand = new RelayCommand<RuleType>(AddRule);
		RemoveRuleCommand = new RelayCommand<RenameRule>(r => { if (r is null) return; Rules.Remove(r); RecomputeAll(); });
		DuplicateRuleCommand = new RelayCommand<RenameRule>(DuplicateRule);
		MoveRuleUpCommand = new RelayCommand<RenameRule>(MoveUp);
		MoveRuleDownCommand = new RelayCommand<RenameRule>(MoveDown);
		EnableAllRulesCommand = new RelayCommand(() => SetAllRulesEnabled(true), () => Rules.Count > 0);
		DisableAllRulesCommand = new RelayCommand(() => SetAllRulesEnabled(false), () => Rules.Count > 0);
		ClearAllRulesCommand = new RelayCommand(ClearAllRules, () => Rules.Count > 0);
		RemoveFileCommand = new RelayCommand<FileItem>(RemoveFile);
		RemoveFolderCommand = new RelayCommand<FolderNode>(RemoveFolder);
		LockPreviewNameCommand = new RelayCommand<PreviewItem>(LockPreviewName);
		UnlockPreviewNameCommand = new RelayCommand<PreviewItem>(UnlockPreviewName);
		LoadPresetCommand = new RelayCommand<RulePreset>(LoadPreset);
		DeletePresetCommand = new RelayCommand<RulePreset>(DeletePreset);
		ImportPresetsCommand = new RelayCommand(ImportPresets);
		ExportPresetsCommand = new RelayCommand(ExportPresets);
		ClearPresetsCommand = new RelayCommand(ClearPresets, () => Presets.Count > 0);
		RollbackHistoryCommand = new RelayCommand<HistoryEntry>(RollbackHistory);
		ClearHistoryCommand = new RelayCommand(ClearHistory);
		RetryFailedCommand = new RelayCommand(RetryFailed, () => CanRetryFailed);

		Logs.CollectionChanged += (_, _) =>
		{
			RaisePropertyChanged(nameof(HasLogs));
			RaisePropertyChanged(nameof(CanRetryFailed));
			RetryFailedCommand.RaiseCanExecuteChanged();
		};

		_service.HistoryChanged += () => OnUi(() =>
		{
			UndoCommand.RaiseCanExecuteChanged();
			RedoCommand.RaiseCanExecuteChanged();
		});

		Files.CollectionChanged += OnFilesChanged;
		Rules.CollectionChanged += OnRulesChanged;
		Previews.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasPreviews));

		// 防抖计时器：连续改动只在停顿后结算一次，避免逐次全量重算（流畅度优化）
		_recomputeDebounce.Tick += (_, _) => { _recomputeDebounce.Stop(); RefreshPreviewList(); };
		_filterDebounce.Tick += (_, _) => { _filterDebounce.Stop(); ApplyFilter(); };

		// 规则卡片不随窗口状态记忆（第14项），启动时规则链保持为空。
		foreach (RulePreset preset in PresetStore.LoadPresets()) Presets.Add(preset);
		foreach (HistoryEntry entry in HistoryStore.Load()) History.Add(entry);
		Presets.CollectionChanged += (_, _) =>
		{
			RaisePropertyChanged(nameof(HasPresets));
			ClearPresetsCommand.RaiseCanExecuteChanged();
		};
		History.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasHistory));
	}

	// ─────────────── 规则预设（A1） ───────────────

	/// <summary>把当前规则链存为具名预设；同名时弹窗确认后覆盖。返回是否已保存。</summary>
	public bool SaveCurrentAsPreset(string name)
	{
		name = name?.Trim() ?? "";
		if (name.Length == 0) return false;

		int existing = IndexOfPreset(name);
		if (existing >= 0 && !AppDialog.Confirm(owner: null, "覆盖预设",
				$"已存在名为「{name}」的预设（{Presets[existing].CountText}）。\n" +
				"覆盖后原预设内容将被当前规则链替换，是否继续？",
				okText: "覆盖", icon: DialogIcon.Warning))
			return false;

		var preset = new RulePreset { Name = name, Rules = PresetStore.Snapshot(Rules) };
		if (existing >= 0) Presets[existing] = preset;
		else Presets.Add(preset);
		PersistPresets();
		return true;
	}

	public bool HasRulesForPreset => Rules.Count > 0;

	/// <summary>载入预设：弹窗确认后以预设内容替换当前规则链（第9项）。</summary>
	private void LoadPreset(RulePreset? preset)
	{
		if (preset is null) return;
		string current = Rules.Count > 0 ? $"，当前 {Rules.Count} 条规则将被替换" : "";
		if (!AppDialog.Confirm(owner: null, "载入预设",
				$"载入预设「{preset.Name}」（{preset.CountText}）{current}，是否继续？",
				okText: "载入"))
			return;

		Rules.Clear();
		foreach (RenameRule rule in PresetStore.Materialize(preset.Rules)) Rules.Add(rule);
	}

	/// <summary>删除预设：弹窗二次确认（第9项）。</summary>
	private void DeletePreset(RulePreset? preset)
	{
		if (preset is null) return;
		if (!AppDialog.Confirm(owner: null, "删除预设",
				$"确定要删除预设「{preset.Name}」（{preset.CountText}）吗？",
				okText: "删除", icon: DialogIcon.Warning, danger: true))
			return;

		Presets.Remove(preset);
		PersistPresets();
	}

	private int IndexOfPreset(string name)
	{
		for (int i = 0; i < Presets.Count; i++)
			if (string.Equals(Presets[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
		return -1;
	}

	/// <summary>为导入的同名预设生成不冲突的备选名称：原名 (导入)、原名 (导入 2) …</summary>
	private string MakeUniquePresetName(string name)
	{
		string candidate = $"{name} (导入)";
		int n = 2;
		while (IndexOfPreset(candidate) >= 0) candidate = $"{name} (导入 {n++})";
		return candidate;
	}

	private void PersistPresets() => PresetStore.SavePresets([.. Presets]);

	/// <summary>清空全部预设：弹窗二次确认（第7项）。</summary>
	private void ClearPresets()
	{
		if (Presets.Count == 0) return;
		if (!AppDialog.Confirm(owner: null, "清空预设",
				$"确定要清空全部 {Presets.Count} 个预设吗？",
				okText: "清空", icon: DialogIcon.Warning, danger: true))
			return;

		Presets.Clear();
		PersistPresets();
	}

	/// <summary>从 JSON 文件导入预设（跨机器迁移）；同名预设提示对比后可覆盖 / 改名另存 / 跳过（第8项）。</summary>
	private void ImportPresets()
	{
		var dialog = new OpenFileDialog
		{
			Title = "导入规则预设",
			Filter = "规则预设 (*.json)|*.json|所有文件 (*.*)|*.*",
			CheckFileExists = true,
		};
		if (dialog.ShowDialog() != true) return;

		List<RulePreset> imported;
		try { imported = PresetStore.ImportFromFile(dialog.FileName); }
		catch (Exception ex)
		{
			ShowToast($"导入失败：{TextFlow.Shorten(ex.Message, 40)}", ToastKind.Error);
			return;
		}

		int added = 0, updated = 0, skipped = 0;
		foreach (RulePreset preset in imported)
		{
			if (string.IsNullOrWhiteSpace(preset.Name)) continue;
			int existing = IndexOfPreset(preset.Name);
			if (existing < 0)
			{
				Presets.Add(preset);
				added++;
				continue;
			}

			(int choice, string newName) = AppDialog.ResolveNameConflict(
				preset.Name, Presets[existing].CountText, preset.CountText, MakeUniquePresetName(preset.Name));
			if (choice == 0)
			{
				Presets[existing] = preset;
				updated++;
			}
			else if (choice == 1)
			{
				preset.Name = IndexOfPreset(newName) >= 0 ? MakeUniquePresetName(newName) : newName;
				Presets.Add(preset);
				added++;
			}
			else skipped++;
		}

		if (added + updated == 0)
		{
			ShowToast(skipped > 0 ? $"导入已取消：跳过 {skipped} 个同名预设。" : "所选文件中没有可导入的预设。", ToastKind.Warning);
			return;
		}
		PersistPresets();
		ShowToast($"导入完成：新增 {added} 个，覆盖 {updated} 个" + (skipped > 0 ? $"，跳过 {skipped} 个" : ""));
	}

	/// <summary>把全部预设导出为 JSON 文件，便于备份或迁移到其它机器。</summary>
	private void ExportPresets()
	{
		if (Presets.Count == 0)
		{
			ShowToast("当前没有可导出的预设。", ToastKind.Warning);
			return;
		}
		var dialog = new SaveFileDialog
		{
			Title = "导出规则预设",
			Filter = "规则预设 (*.json)|*.json",
			FileName = "RenameTool-presets.json",
			DefaultExt = ".json",
		};
		if (dialog.ShowDialog() != true) return;

		try
		{
			PresetStore.ExportToFile(dialog.FileName, [.. Presets]);
			ShowToast($"已导出 {Presets.Count} 个预设到 {TextFlow.Shorten(Path.GetFileName(dialog.FileName), 24)}");
		}
		catch (Exception ex)
		{
			ShowToast($"导出失败：{TextFlow.Shorten(ex.Message, 36)}", ToastKind.Error);
		}
	}

	// ─────────────── 批次历史与整体回滚（A2） ───────────────

	/// <summary>把一次成功执行的改名记为历史批次。</summary>
	private void RecordHistory(IReadOnlyList<RenameOutcome> outcomes)
	{
		var ops = outcomes.Where(o => o.Success)
			.Select(o => new HistoryOp
			{
				Directory = Path.GetDirectoryName(o.NewPath) ?? "",
				OldName = o.OldName,
				NewName = o.NewFileName,
			})
			.ToList();
		if (ops.Count == 0) return;

		string summary = string.Join(" + ", Rules.Where(r => r.Enabled).Select(r => r.TypeLabel));
		var entry = new HistoryEntry { RuleSummary = summary, Ops = ops };
		History.Insert(0, entry);
		HistoryStore.Save(History);
	}

	/// <summary>整体回滚某次历史批次（把文件从新名改回原名）。回滚前展示对照预览并二次确认。</summary>
	private async void RollbackHistory(HistoryEntry? entry)
	{
		if (entry is null || entry.RolledBack || IsExecuting) return;

		var pairs = entry.Ops.Select(o => (From: o.NewName, To: o.OldName)).ToList();
		string summary = $"确认将该批次（{entry.TimeText}）的 {pairs.Count} 个文件文件名还原吗？";
		if (!AppDialog.ConfirmRollback(null, summary, pairs)) return;

		int failed = HistoryStore.Rollback(entry, out string? error);
		HistoryStore.Save(History);
		Logs.Clear();
		foreach (var op in entry.Ops)
		{
			bool rolledBack = File.Exists(op.OldPath) || !File.Exists(op.NewPath);
			Logs.Add(new LogItem
			{
				Original = op.NewName,
				NewName = op.OldName,
				Status = rolledBack ? "success" : "failed",
				Reason = rolledBack ? null : error,
			});
		}
		if (failed == 0)
		{
			entry.RolledBack = true;
			ShowToast($"已还原 {entry.Ops.Count} 个文件名", ToastKind.Success);
		}
		else
		{
			// E6：回滚失败原先只落到日志里，窗口又立刻刷新，用户扫一眼列表会误以为整批都还原成功了。
			// 这里给出明确失败提示，并指明明细去处。
			ShowToast($"回滚未完成：{failed} 个文件还原失败，原因见下方日志", ToastKind.Error);
		}
		// 撤销/重做只在当前会话内有效，磁盘已由历史面板改回，内存撤销栈随之失效
		// （部分失败时同样清空：此时磁盘状态已被改写，内存里那条改名链不再与磁盘对应）
		_redoHistory.Clear();
		_service.ClearHistory();
		RaisePropertyChanged(nameof(History));
		await RefreshFromSources();
		RecomputeAll();
	}

	private void ClearHistory()
	{
		History.Clear();
		HistoryStore.Save(History);
		_redoHistory.Clear();
		_service.ClearHistory(); // 历史清空后内存撤销栈一并清空
	}

	// ─────────────── 规则变更订阅 ───────────────

	private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Move 仅改变位置，OldItems 与 NewItems 为同一项、成员关系未变，无需增删订阅。
		if (e.Action != NotifyCollectionChangedAction.Move)
		{
			if (e.NewItems is not null)
			{
				foreach (RenameRule item in e.NewItems)
				{
					item.PropertyChanged += OnRulePropertyChanged;
					item.Config.PropertyChanged += OnConfigPropertyChanged;
				}
			}
			if (e.OldItems is not null)
			{
				foreach (RenameRule item in e.OldItems)
				{
					item.PropertyChanged -= OnRulePropertyChanged;
					item.Config.PropertyChanged -= OnConfigPropertyChanged;
				}
			}
		}
		RaisePropertyChanged(nameof(HasRules));
		RaisePropertyChanged(nameof(HasRulesForPreset));
		RaisePropertyChanged(nameof(AnyRuleEnabled));
		EnableAllRulesCommand.RaiseCanExecuteChanged();
		DisableAllRulesCommand.RaiseCanExecuteChanged();
		ClearAllRulesCommand.RaiseCanExecuteChanged();
		RecomputeAll();
	}

	private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(RenameRule.Enabled) or nameof(RenameRule.Scope))
		{
			if (e.PropertyName == nameof(RenameRule.Enabled)) RaisePropertyChanged(nameof(AnyRuleEnabled));
			ScheduleRecompute();
		}
	}

	/// <summary>规则参数变化后重算预览（各编辑器的绑定都会写回 Config 属性）。</summary>
	private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e) => ScheduleRecompute();

	// ─────────────── 界面线程与耗时操作 ───────────────

	/// <summary>扫描 / 改名耗时超过这个时长才弹出进度窗口，避免瞬间完成的操作让窗口一闪而过。</summary>
	private const int ProgressWindowDelayMs = 300;

	/// <summary>
	/// 执行结束后进度条的停留时长（E3）：完成后不立刻把进度复位，而是先把“满格”状态保持这么久，
	/// 让用户看清进度走到 100% 的完成瞬间；否则复位与最后一次进度更新落在同一帧里，进度条根本来不及绘制。
	/// </summary>
	private const int ProgressHoldMs = 1200;

	/// <summary>
	/// 界面线程调度器。耗时操作在后台线程执行，凡是要触碰绑定对象或命令的更新都必须切回这里，
	/// 否则会触发 WPF 的跨线程访问异常。
	/// </summary>
	private static Dispatcher UiDispatcher => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

	/// <summary>把界面更新切回 UI 线程执行（已在 UI 线程时直接同步执行，避免无谓的排队）。</summary>
	private static void OnUi(Action action)
	{
		Dispatcher dispatcher = UiDispatcher;
		if (dispatcher.CheckAccess()) action();
		else dispatcher.BeginInvoke(action);
	}

	/// <summary>把进度窗口挂到主窗口上作为其所有者，保证居中位置与层级正确。</summary>
	private static void AttachOwner(Window window)
	{
		Window? owner = Application.Current?.MainWindow;
		if (owner is not null && owner != window && owner.IsLoaded) window.Owner = owner;
	}

	/// <summary>
	/// 在后台线程执行 <paramref name="work"/>，并只在它确实耗时（超过
	/// <see cref="ProgressWindowDelayMs"/>）时才显示进度窗口——这样小批量操作不会闪出一个窗口。
	/// 窗口显示后界面依然可交互，用户随时可以点「中断」。
	/// </summary>
	private static async Task RunInBackgroundAsync(ProgressWindow window, Action work)
	{
		Task task = Task.Run(work);
		if (await Task.WhenAny(task, Task.Delay(ProgressWindowDelayMs)) != task) window.Open();
		await task;
	}

	// ─────────────── 导入 ───────────────

	private async void ImportFiles()
	{
		var dlg = new OpenFileDialog
		{
			Multiselect = true,
			Title = "选择要重命名的文件",
		};
		if (dlg.ShowDialog() != true) return;
		await AddPaths(dlg.FileNames);
	}

	private async void ImportFolder()
	{
		var dlg = new OpenFolderDialog { Title = "选择文件夹（将递归导入其中全部文件）" };
		if (dlg.ShowDialog() != true) return;
		await ImportFoldersAsync([dlg.FolderName]);
	}

	/// <summary>
	/// 导入拖入窗口的路径：自动识别类型——文件直接导入，文件夹递归导入其中全部文件。
	/// </summary>
	public async void ImportDropped(IEnumerable<string> paths)
	{
		if (!CanImport) return;

		List<string> droppedFiles = [];
		List<string> droppedFolders = [];
		foreach (string path in paths)
		{
			if (Directory.Exists(path)) droppedFolders.Add(path);
			else if (File.Exists(path)) droppedFiles.Add(path);
		}
		if (droppedFiles.Count > 0) await AddPaths(droppedFiles);
		await ImportFoldersAsync(droppedFolders);
	}

	/// <summary>
	/// 递归导入若干文件夹。扫描在后台线程进行，耗时较久时才弹出可中断的加载窗口；
	/// 用户一旦中断就放弃本次导入——已扫描到的结果全部丢弃，不向列表加入任何文件。
	/// </summary>
	private async Task ImportFoldersAsync(IReadOnlyList<string> roots)
	{
		if (roots.Count == 0) return;

		string label = roots.Count == 1
			? $"正在扫描：{TextFlow.Shorten(roots[0], 40)}"
			: $"正在扫描 {roots.Count} 个文件夹…";
		var window = new ProgressWindow("正在载入文件夹", label, indeterminate: true);
		AttachOwner(window);
		CancellationToken token = window.Token;

		var collected = new List<(string Root, List<string> Paths)>(roots.Count);
		int scanned = 0;
		try
		{
			await RunInBackgroundAsync(window, () =>
			{
				foreach (string root in roots)
				{
					List<string> paths = [];
					// scanned 只在后台的这一条线程上递增，无需原子操作
					CollectFiles(root, paths, 0, token, () => window.ReportScan(label, ++scanned));
					if (token.IsCancellationRequested) break;
					collected.Add((root, paths));
				}
			});

			if (token.IsCancellationRequested)
			{
				ShowToast("已中断导入：本次未加入任何文件", ToastKind.Warning);
				return;
			}

			int added = 0;
			foreach (var (root, paths) in collected)
			{
				if (paths.Count == 0) continue;
				await AddPaths(paths, fromFolder: true, root: root);
				added += paths.Count;
			}
			if (added == 0) ShowToast("所选文件夹中没有可导入的文件", ToastKind.Warning);
		}
		finally
		{
			window.Finish();
		}
	}

	/// <summary>递归扫描的最大目录深度，用于防御异常深的嵌套结构。</summary>
	private const int MaxScanDepth = 64;

	/// <summary>
	/// 递归收集 <paramref name="root"/> 下的全部文件。
	/// 三点与旧实现不同：
	/// ① 每个子目录单独处理，某个目录无权限时只跳过它自己，同层的其它目录仍会继续扫描
	///    （旧实现把 try 包住整个方法体，一个无权限子目录会连带漏掉同层所有兄弟目录的文件）；
	/// ② 遇到重解析点（junction / 符号链接）不再深入。这类目录可以指回祖先目录形成环，
	///    旧实现会无限递归直到栈溢出——栈溢出在 .NET 中无法捕获，会直接终止进程；
	/// ③ 支持取消：每扫描到一个文件就检查一次，便于在后台扫描时随时中断。
	/// 收集到每个文件时都会回调 <c>onFile</c>（用于刷新“已扫描 N 个文件”）。
	/// </summary>
	private static void CollectFiles(string root, List<string> results)
		=> CollectFiles(root, results, 0, default, null);

	/// <summary>递归收集的实际实现（带取消与逐文件回调）。</summary>
	private static void CollectFiles(string root, List<string> results, int depth,
		CancellationToken token, Action? onFile)
	{
		if (depth > MaxScanDepth || token.IsCancellationRequested) return;

		try
		{
			foreach (string file in Directory.EnumerateFiles(root))
			{
				if (token.IsCancellationRequested) return;
				if (IsTransientFile(file)) continue;
				results.Add(file);
				onFile?.Invoke();
			}
		}
		catch
		{
			// 该目录的文件列表不可读：跳过文件，但仍继续尝试它的子目录
		}

		List<string> subdirs;
		try
		{
			subdirs = [.. Directory.EnumerateDirectories(root)];
		}
		catch
		{
			return; // 子目录列表不可读，到此为止
		}

		foreach (string dir in subdirs)
		{
			if (token.IsCancellationRequested) return;
			try
			{
				if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
			}
			catch
			{
				continue; // 取不到属性的一律不深入
			}

			CollectFiles(dir, results, depth + 1, token, onFile);
		}
	}

	/// <summary>
	/// 是否为 Office 等程序生成的临时 / 缓存文件（如 ~$xxx.docx、$xxx.tmp）。
	/// 这类文件在编辑期间短暂出现、保存后即被宿主程序删除，若被扫描进列表会在改名前报“文件不存在”，故一律忽略。
	/// </summary>
	private static bool IsTransientFile(string path)
	{
		string name = Path.GetFileName(path);
		if (name.Length == 0) return false;
		return name.StartsWith("~$", StringComparison.Ordinal)
			|| name.StartsWith("$", StringComparison.Ordinal);
	}

	/// <summary>判断 <paramref name="directory"/> 是否位于 <paramref name="root"/> 之下（含自身）。</summary>
	private static bool IsUnder(string directory, string root)
	{
		try
		{
			string d = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return d.Equals(r, StringComparison.OrdinalIgnoreCase)
				|| d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private bool _suspend; // 批量增删期间挂起预览重算

	// ─────────────── 防抖（流畅度优化） ───────────────

	/// <summary>规则参数连续变化（敲击文本框、批量勾选等）时合并重算请求，避免逐次全量重算预览。</summary>
	private readonly DispatcherTimer _recomputeDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

	/// <summary>搜索框连续输入时合并“过滤 + 文件夹树重建”请求。</summary>
	private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

	/// <summary>请求稍后重算预览：同一批连续修改只会在最后一次停顿后触发一次重算。</summary>
	private void ScheduleRecompute()
	{
		if (_suspend) return; // 批量阶段结束由调用方统一刷新
		_recomputeDebounce.Stop();
		_recomputeDebounce.Start();
	}

	/// <summary>请求稍后按当前搜索词刷新列表。</summary>
	private void ScheduleFilter()
	{
		if (_suspend) return;
		_filterDebounce.Stop();
		_filterDebounce.Start();
	}

	/// <summary>把挂起的防抖请求立即结算（执行 / 保存前调用，保证用的是最新结果）。</summary>
	private void FlushPending()
	{
		if (_recomputeDebounce.IsEnabled)
		{
			_recomputeDebounce.Stop();
			RefreshPreviewList();
		}
		if (_filterDebounce.IsEnabled)
		{
			_filterDebounce.Stop();
			ApplyFilter();
		}
	}

	// ─────────────── 轻提示（非阻塞执行反馈） ───────────────

	/// <summary>当前正在展示的通知，按出现顺序自上而下排列；多条可同时存在。</summary>
	public ObservableCollection<ToastItem> Toasts { get; } = [];

	/// <summary>
	/// 由主窗口注册：为某条通知播放退场动画，动画播完后调用 done 把它从列表中真正移除。
	/// 未注册时（例如窗口尚未加载）直接移除，保证通知不会永久滞留。
	/// </summary>
	internal Action<ToastItem, Action>? ToastExitAnimator { get; set; }

	/// <summary>显示一条非阻塞轻提示；多次调用会并列显示多条，各自计时后逐条消失。</summary>
	public void ShowToast(string message, ToastKind kind = ToastKind.Success)
		=> Toasts.Add(new ToastItem(message, kind, DismissToast));

	/// <summary>关闭某条通知：先播退场动画，再移除该条，其余条目自动上移补位。</summary>
	private void DismissToast(ToastItem item)
	{
		item.Stop();
		if (ToastExitAnimator is { } animator) animator(item, () => Toasts.Remove(item));
		else Toasts.Remove(item);
	}

	/// <summary>
	/// 把若干路径加入列表。去重在界面线程完成（只读内存），<see cref="FileItem"/> 的构造
	/// ——它会访问磁盘取大小与三个时间戳——放到后台线程执行，最后再回界面线程入列。
	/// 上万文件的导入若整段留在界面线程，窗口会完全冻住。
	/// </summary>
	private async Task AddPaths(IEnumerable<string> paths, bool fromFolder = false, string? root = null)
	{
		var existing = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
		var newPaths = new List<string>();
		foreach (string p in paths)
			if (existing.Add(p)) newPaths.Add(p);

		List<FileItem> created = [];
		if (newPaths.Count > 0)
		{
			await Task.Run(() =>
			{
				created = new List<FileItem>(newPaths.Count);
				foreach (string p in newPaths)
					created.Add(new FileItem(p) { IsFolderSource = fromFolder, SourceRoot = fromFolder ? root : null });
			});
		}

		bool added = false, merged = false;
		_suspend = true;
		try
		{
			// 先导入单个文件、后又导入其所在文件夹：自动合并为文件夹来源
			if (fromFolder && root is not null)
			{
				foreach (var f in Files)
				{
					if (!f.IsFolderSource && IsUnder(f.Directory, root))
					{
						f.IsFolderSource = true;
						f.SourceRoot = root;
						merged = true;
					}
				}
			}

			// 后台构造期间列表可能已被别的操作改动，这里按最新状态再核对一次
			var byPath = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
			foreach (var f in Files) byPath.TryAdd(f.FullPath, f);

			foreach (var item in created)
			{
				if (byPath.TryAdd(item.FullPath, item))
				{
					Files.Add(item);
					added = true;
				}
				else if (fromFolder && root is not null && byPath.TryGetValue(item.FullPath, out var hit) && !hit.IsFolderSource)
				{
					// 已存在但尚未标记为文件夹来源的条目，合并之
					hit.IsFolderSource = true;
					hit.SourceRoot = root;
					merged = true;
				}
			}
		}
		finally
		{
			_suspend = false;
		}
		if (!added && !merged) return;
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	/// <summary>
	/// 刷新文件列表：
	/// 文件夹来源的按根目录重新扫描为最新状态；文件来源的逐个刷新存在状态，不存在则标记失效。
	/// 扫描目录与读取磁盘状态都在后台线程完成（纯读盘、不碰绑定对象），只有最后的赋值在界面线程，
	/// 避免根目录下文件很多时整段刷新把界面卡死。
	/// </summary>
	private async Task RefreshFromSources(bool folderScanOnly = false)
	{
		if (Files.Count == 0) return;

		var roots = Files.Where(f => f.IsFolderSource && !string.IsNullOrEmpty(f.SourceRoot))
			.Select(f => f.SourceRoot!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (roots.Count == 0 && folderScanOnly) return;

		// 快照：ObservableCollection 不能在后台线程上枚举
		var snapshot = Files.ToList();

		var latest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var states = new Dictionary<FileItem, FileItem.DiskState>();
		List<FileItem> newItems = [];

		await Task.Run(() =>
		{
			// 汇总各文件夹根的最新文件集合
			foreach (string root in roots)
			{
				List<string> paths = [];
				CollectFiles(root, paths);
				foreach (string p in paths) latest.TryAdd(p, root);
			}

			var known = new HashSet<string>(snapshot.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
			foreach (var (path, root) in latest)
			{
				if (known.Contains(path)) continue;
				// 根目录下新出现的文件：构造同样要读盘，一并放在这里
				newItems.Add(new FileItem(path) { IsFolderSource = true, SourceRoot = root });
			}

			foreach (var f in snapshot) states[f] = FileItem.ReadDisk(f.FullPath);
		});

		_suspend = true;
		try
		{
			// 文件夹来源：逐个刷新存在状态；磁盘上已不存在的仅标记失效并保留，不清除报错条目
			if (roots.Count > 0)
			{
				foreach (var f in snapshot.Where(f => f.IsFolderSource))
					if (states.TryGetValue(f, out var st)) f.ApplyDiskState(st);

				// 补充根目录下新出现的文件；期间列表可能已被改动，故按最新状态再核对一次
				var present = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
				foreach (var item in newItems)
					if (present.Add(item.FullPath)) Files.Add(item);
			}

			// 文件来源：逐个刷新（不存在的标记为失效并保留）
			if (!folderScanOnly)
			{
				foreach (var f in snapshot.Where(f => !f.IsFolderSource))
					if (states.TryGetValue(f, out var st)) f.ApplyDiskState(st);
			}

			// 安全网：同一路径在列表里只允许存在一条。
			// 改名的界面同步回调是异步排队的，极端时序下仍可能与本次刷新交错，留下
			// 「已改名的旧条目」与「扫描新插入的同名条目」这一对（前者往往被按旧路径读盘
			// 误判为失效）。这里按完整路径收敛：同路径两条时丢弃带失效标记的那条。
			if (Files.Count > 1)
			{
				var seen = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
				List<FileItem>? dupes = null;
				foreach (var f in Files)
				{
					if (!seen.TryGetValue(f.FullPath, out var kept))
					{
						seen[f.FullPath] = f;
						continue;
					}
					FileItem drop = kept.IsMissing && !f.IsMissing ? kept : f;
					if (ReferenceEquals(drop, kept)) seen[f.FullPath] = f;
					(dupes ??= []).Add(drop);
				}
				if (dupes is not null)
					foreach (var f in dupes) Files.Remove(f);
			}
		}
		finally
		{
			_suspend = false;
		}

		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	// ─────────────── 规则 ───────────────

	private void AddRule(RuleType type)
	{
		var rule = new RenameRule { Type = type, Config = RuleConfig.CreateDefault(type) };
		// 成对交换的默认意图是「整个名称互换」；其余规则默认只作用于文件名主干
		if (type == RuleType.PairSwap) rule.Scope = ExtensionScope.Full;
		Rules.Add(rule);
	}

	/// <summary>一次性启用或停用规则链中的全部规则（批量操作期间挂起重算，结束时统一刷新）。</summary>
	private void SetAllRulesEnabled(bool enabled)
	{
		if (Rules.Count == 0) return;
		_suspend = true;
		try
		{
			foreach (RenameRule rule in Rules) rule.Enabled = enabled;
		}
		finally
		{
			_suspend = false;
		}
		RecomputeAll();
	}

	/// <summary>清空规则链中的全部规则。</summary>
	private void ClearAllRules()
	{
		if (Rules.Count == 0) return;
		Rules.Clear();
	}

	private void DuplicateRule(RenameRule? rule)
	{
		if (rule is null) return;
		int idx = Rules.IndexOf(rule);
		var copy = new RenameRule { Type = rule.Type, Config = CloneConfig(rule.Config) };
		copy.Scope = rule.Scope;
		copy.Enabled = rule.Enabled;
		Rules.Insert(idx + 1, copy);
	}

	private static RuleConfig CloneConfig(RuleConfig src)
	{
		var copy = new RuleConfig { Type = src.Type };
		foreach (var p in typeof(RuleConfig).GetProperties())
		{
			if (p.CanWrite) p.SetValue(copy, p.GetValue(src));
		}
		return copy;
	}

	private void MoveUp(RenameRule? rule)
	{
		if (rule is null) return;
		int i = Rules.IndexOf(rule);
		if (i <= 0) return;
		Rules.Move(i, i - 1);
	}

	private void MoveDown(RenameRule? rule)
	{
		if (rule is null) return;
		int i = Rules.IndexOf(rule);
		if (i < 0 || i >= Rules.Count - 1) return;
		Rules.Move(i, i + 1);
	}

	// ─────────────── 选择 / 排序 / 过滤 ───────────────

	private void SetAllSelected(bool value)
	{
		// 失效条目不可选中，全选 / 全不选都跳过
		_suspend = true; // 避免逐条勾选触发逐次重算（大列表下为 O(n²)）
		try
		{
			foreach (var f in Files.Where(f => f.IsVisible && !f.IsMissing)) f.Selected = value;
		}
		finally
		{
			_suspend = false;
		}
		RaisePropertyChanged(nameof(SelectedText));
		RaisePropertyChanged(nameof(AnyFileSelected));
		DeleteSelectedCommand.RaiseCanExecuteChanged();
		RecomputeAll();
	}

	/// <summary>从列表中移除单个文件（文件列表行内的删除按钮；仅影响列表，不涉及磁盘文件）。</summary>
	private void RemoveFile(FileItem? file)
	{
		if (file is null) return;
		Files.Remove(file);
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	/// <summary>从列表中移除某个文件夹（含其子文件夹）下的全部文件（仅影响列表，不涉及磁盘文件）。</summary>
	private void RemoveFolder(FolderNode? node)
	{
		if (node is null) return;
		var targets = node.AllFiles().ToList();
		if (targets.Count == 0) return;

		_suspend = true;
		try
		{
			foreach (var f in targets) Files.Remove(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	/// <summary>锁定该预览行的当前目标名：此后规则变化不再影响它，直到手动解除。</summary>
	private void LockPreviewName(PreviewItem? item)
	{
		if (item is null || item.File.LockedName is not null) return;
		item.File.LockedName = item.NewName;
		RecomputeAll();
	}

	/// <summary>解除该预览行的目标名锁定，恢复为按规则计算。</summary>
	private void UnlockPreviewName(PreviewItem? item)
	{
		if (item is null || item.File.LockedName is null) return;
		item.File.LockedName = null;
		RecomputeAll();
	}

	/// <summary>从列表中移除当前勾选的文件（仅影响列表，不涉及磁盘文件）。</summary>
	private void DeleteSelected()
	{
		var selected = Files.Where(f => f.Selected).ToList();
		if (selected.Count == 0) return;

		_suspend = true;
		try
		{
			foreach (var f in selected) Files.Remove(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void Resort()
	{
		if (Files.Count < 2) return;
		var list = Files.ToList();
		list = SortMode switch
		{
			SortMode.NameAsc => [.. list.OrderBy(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.NameDesc => [.. list.OrderByDescending(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.ExtAsc => [.. list.OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.ExtDesc => [.. list.OrderByDescending(f => f.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(f => f.Name, NaturalStringComparer.Instance)],
			_ => list,
		};
		if (SortMode == SortMode.Import) return;
		_suspend = true;
		try
		{
			Files.Clear();
			foreach (var f in list) Files.Add(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void ApplyFilter()
	{
		var spec = new FilterSpec { Value = FilterText };
		foreach (var f in Files) f.IsVisible = spec.Matches(f);
		RefreshFilesView();
		RefreshPreviewList();
		RaisePropertyChanged(nameof(SelectedText));
		RaisePropertyChanged(nameof(AnyFileSelected));
	}

	private void RefreshFilesView()
	{
		FilesView.Clear();
		foreach (var f in Files)
			if (f.IsVisible) FilesView.Add(f);
		RebuildFileTree();
	}

	// ─────────────── 文件列表：文件夹层级与折叠 ───────────────

	/// <summary>
	/// 展开 / 折叠该文件夹。仅切换视图状态，行数据完全不动：
	/// 该文件夹模块自带的容器会随 <see cref="FolderNode.IsExpanded"/> 隐藏或显示，动画由视图层负责。
	/// </summary>
	public void ToggleFolder(FolderNode node)
	{
		if (node.IsExpanded)
		{
			node.IsExpanded = false;
			_collapsedFolders.Add(node.FullPath);
		}
		else
		{
			node.IsExpanded = true;
			_collapsedFolders.Remove(node.FullPath);
		}
	}

	/// <summary>依据当前可见文件重建文件夹层级树，并为每个文件夹模块填充自己的子行容器。</summary>
	private void RebuildFileTree()
	{
		foreach (var root in RootFolders) DetachTree(root);
		RootFolders.Clear();
		if (FilesView.Count == 0) return;

		var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
		var order = new List<FolderNode>();
		foreach (var file in FilesView)
		{
			string directory = file.Directory;
			if (!nodes.TryGetValue(directory, out var node))
			{
				node = new FolderNode(directory) { IsExpanded = !_collapsedFolders.Contains(directory) };
				nodes[directory] = node;
				order.Add(node);
			}
			node.Files.Add(file);
		}

		// 建立父子关系：父文件夹取“同样出现在列表中的最近祖先目录”
		foreach (var node in order)
		{
			if (FindParent(node.FullPath, nodes) is { } parent)
			{
				node.Parent = parent;
				parent.Folders.Add(node);
			}
			else
			{
				RootFolders.Add(node);
			}
		}

		foreach (var root in RootFolders)
		{
			SetDepth(root, 0);
			AttachTree(root);
			BuildRows(root);
		}
	}

	/// <summary>为每个文件夹模块填充自己的行容器内容：先本层文件，再子文件夹（子文件夹递归填充自己的容器）。</summary>
	private static void BuildRows(FolderNode node)
	{
		node.Rows.Clear();
		foreach (var file in node.Files) node.Rows.Add(file);
		foreach (var folder in node.Folders)
		{
			node.Rows.Add(folder);
			BuildRows(folder);
		}
	}

	/// <summary>向上查找最近的、同样出现在列表中的祖先文件夹。</summary>
	private static FolderNode? FindParent(string directory, Dictionary<string, FolderNode> nodes)
	{
		string? current = directory;
		while (true)
		{
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent)) return null;
			if (nodes.TryGetValue(parent, out var node)) return node;
			current = parent;
		}
	}

	private static int SetDepth(FolderNode node, int depth)
	{
		node.Depth = depth;
		// 文件比所在文件夹再深一级，使文件行与文件夹层级保持一致缩进
		foreach (var file in node.Files) file.Depth = depth + 1;
		int count = node.Files.Count;
		foreach (var folder in node.Folders) count += SetDepth(folder, depth + 1);
		node.TotalCount = count;
		return count;
	}

	private static void AttachTree(FolderNode node)
	{
		node.Attach();
		foreach (var folder in node.Folders) AttachTree(folder);
	}

	private static void DetachTree(FolderNode node)
	{
		node.Detach();
		foreach (var folder in node.Folders) DetachTree(folder);
	}

	private void RefreshFileStats()
	{
		RaisePropertyChanged(nameof(FileTotalText));
		RaisePropertyChanged(nameof(SelectedText));
		RaisePropertyChanged(nameof(AnyFileSelected));
		RaisePropertyChanged(nameof(HasFiles));
		RaisePropertyChanged(nameof(CanClearMissing));
		ClearCommand.RaiseCanExecuteChanged();
		DeleteSelectedCommand.RaiseCanExecuteChanged();
		RefreshCommand.RaiseCanExecuteChanged();
		SelectAllCommand.RaiseCanExecuteChanged();
		SelectNoneCommand.RaiseCanExecuteChanged();
	}

	private bool IsInScope(FileItem f) => f.Selected; // FilesView 已保证可见

	// ─────────────── 预览 ───────────────

	public void RecomputeAll()
	{
		_recomputeDebounce.Stop(); // 结构性变更立即结算，同时取消挂起的防抖请求
		RefreshPreviewList();
	}

	private void RefreshPreviewList()
	{
		var result = PreviewEngine.Compute(FilesView, Rules.Where(r => r.Enabled).ToList(), IsInScope);
		_affected = result.AffectedCount;
		_conflicts = result.ConflictCount;
		RaisePropertyChanged(nameof(Conflicts));
		RaisePropertyChanged(nameof(OkChangeCount));
		RaisePropertyChanged(nameof(TaskSummary));
		RaisePropertyChanged(nameof(ExecuteTooltip));
		ExecuteCommand.RaiseCanExecuteChanged();

		List<PreviewItem> shown = _previewFilter switch
		{
			PreviewFilter.Affected => result.Items.Where(i => i.HasChange).ToList(),
			PreviewFilter.Conflict => result.Items.Where(i => i.Issue != PreviewIssue.None).ToList(),
			_ => result.Items.ToList(),
		};
		Previews.Clear();
		foreach (var item in shown) Previews.Add(item);
	}

	private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems is not null)
		{
			foreach (FileItem item in e.NewItems)
				item.PropertyChanged += OnFilePropertyChanged;
		}
		if (e.OldItems is not null)
		{
			foreach (FileItem item in e.OldItems)
				item.PropertyChanged -= OnFilePropertyChanged;
		}
		if (_suspend) return; // 批量阶段结束由调用方统一刷新
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FileItem.Selected))
		{
			if (_suspend) return; // 批量勾选 / 取消期间由调用方统一刷新
			RaisePropertyChanged(nameof(SelectedText));
			RaisePropertyChanged(nameof(AnyFileSelected));
			DeleteSelectedCommand.RaiseCanExecuteChanged();
			RecomputeAll();
		}
	}

	// ─────────────── 执行 / 撤销 ───────────────

	/// <summary>判断体检跳过原因是否属于“命名冲突”（目标已存在 / 与他项重名）。</summary>
	private static bool IsConflictReason(string reason) =>
		reason.Contains("重名") || reason.Contains("已存在");

	private async void Execute()
	{
		// 若有挂起的防抖请求，先立即结算，确保按钮可用性与执行集合都基于最新结果
		FlushPending();
		if (!CanExecute) return;
		// 始终基于当前可见集构建执行计划，避免受预览过滤影响而漏执行
		var full = PreviewEngine.Compute(FilesView, Rules.Where(r => r.Enabled).ToList(), IsInScope);
		var built = PreviewEngine.BuildPlan(full.Items, Policy);

		if (built.Aborted)
		{
			// 逗号处显式换行：单行会挤到卡片边缘才折行，断点不可控
			ShowToast(
				$"已取消执行：发现 {built.ConflictCount} 处命名冲突，\n请修改规则或冲突策略。", ToastKind.Warning);
			return;
		}
		if (built.Plan.Count == 0)
		{
			if (built.Skipped.Count > 0)
				ShowToast($"{built.Skipped.Count} 个文件因冲突或名称问题被跳过，没有可改名的文件。", ToastKind.Warning);
			return;
		}

		IsExecuting = true;
		Logs.Clear();
		ProgressTotal = built.Plan.Count;
		ProgressCurrent = 0;
		ProgressText = "执行中…";
		// 中断标记与结果列表声明在 try 之外：收尾询问在 finally 之后还要用到它们
		bool interrupted = false;
		List<RenameOutcome> outcomes = [];
		try
		{
			// 体检中被跳过的条目先记入日志（冲突 / 空名 / 非法字符 / 路径过长）
			foreach (var (oldName, newName, reason) in built.Skipped)
			{
				Logs.Add(new LogItem
				{
					Original = oldName,
					NewName = newName,
					Status = "skipped",
					Reason = $"体检跳过：{reason}",
				});
			}

			// 再核对源文件是否存在：已不存在（被删除 / 改名）的跳过改名，但不影响其它文件
			var runnable = new List<(FileItem File, string OldName, string NewName)>(built.Plan.Count);
			var missing = new List<(FileItem File, string OldName, string NewName)>();
			foreach (var item in built.Plan)
			{
				if (File.Exists(item.File.FullPath)) { item.File.IsMissing = false; runnable.Add(item); }
				else missing.Add(item);
			}

			foreach (var (file, oldName, newName) in missing)
			{
				file.Selected = false;   // 取消勾选
				file.IsMissing = true;   // 醒目失效标记
				Logs.Add(new LogItem
				{
					Original = oldName,
					NewName = newName,
					Status = "failed",
					Reason = "文件不存在（已被删除或改名）",
					File = file,
				});
			}

			int done = missing.Count;
			_redoHistory.Clear(); // 新的执行使既有重做链路失效（与 RenameService 清空重做栈保持一致）

			// 改名移到后台线程执行：界面与进度窗口保持可响应，用户随时可以中断。
			// 小批量眨眼就完成，RunInBackgroundAsync 不会弹出进度窗口。
			var window = new ProgressWindow("正在改名", $"共 {built.Plan.Count} 项", indeterminate: false);
			AttachOwner(window);
			CancellationToken token = window.Token;
			try
			{
				await RunInBackgroundAsync(window, () =>
				{
					outcomes = _service.Execute(runnable,
						_ =>
						{
							int finished = ++done;
							window.Report("正在改名…", finished, built.Plan.Count);
							OnUi(() => ProgressCurrent = finished);
						},
						token,
						// 改名成功后同步列表条目：绑定对象只能在界面线程上更新
						(file, name) => OnUi(() => file.ApplyDiskRename(name)));
				});
			}
			finally
			{
				window.Finish();
			}
			interrupted = token.IsCancellationRequested;
			var byPath = runnable.ToDictionary(r => r.File.FullPath, r => r.File, StringComparer.OrdinalIgnoreCase);
			foreach (var o in outcomes)
			{
				byPath.TryGetValue(o.OldPath, out var source);
				Logs.Add(new LogItem
				{
					Original = o.OldName,
					NewName = o.NewFileName,
					Status = o.Success ? "success" : "failed",
					Reason = o.Success ? null : o.Error,
					File = source,
				});
			}
			RecordHistory(outcomes);

			int ok = outcomes.Count(o => o.Success);
			// 失败按原因分类（第8项）：冲突 / 被占用 / 不存在 / 其它
			int failConflict = outcomes.Count(o => !o.Success && o.ErrorKind == RenameErrorKind.Conflict);
			int failInUse = outcomes.Count(o => !o.Success && o.ErrorKind == RenameErrorKind.InUse);
			int failMissing = outcomes.Count(o => !o.Success && o.ErrorKind == RenameErrorKind.Missing);
			int failOther = outcomes.Count(o => !o.Success && o.ErrorKind == RenameErrorKind.Other);
			// 体检跳过：区分“命名冲突”与“名称非法 / 超长”等硬性问题
			int skipConflict = built.Skipped.Count(s => IsConflictReason(s.Reason));
			int skipOther = built.Skipped.Count - skipConflict;
			int missingTotal = missing.Count + failMissing;

			// 内容拆分：成功、文件名冲突、文件被占用、文件不存在、其它错误分别提示
			if (ok > 0)
				ShowToast($"改名完成：成功 {ok} 项", ToastKind.Success);
			if (skipConflict + failConflict > 0)
				ShowToast($"文件名冲突：跳过 {skipConflict + failConflict} 项", ToastKind.Warning);
			if (failInUse > 0)
				ShowToast($"{failInUse} 个文件被占用，请关闭占用程序后重试", ToastKind.Error);
			if (missingTotal > 0)
				ShowToast($"{missingTotal} 个文件不存在，可能已被删除或改名", ToastKind.Error);
			if (skipOther + failOther > 0)
				ShowToast($"{skipOther + failOther} 个文件改名失败（名称非法、过长或序号越界）", ToastKind.Error);
		}
		finally
		{
			IsExecuting = false;
			// E3：这里刻意不立刻把进度清零——复位若与最后一次进度更新落在同一帧，
			// 进度条永远来不及绘制到 100%。真正复位交给紧随其后的停留逻辑。
			ProgressText = interrupted ? "已中断" : "已完成";
			RaisePropertyChanged(nameof(FileTotalText));
			// 关键：改名的界面同步回调是「后台线程 BeginInvoke 排队」的（见上面 applyRename），
			// 它可能比这里的续体更晚才轮到执行。若不等它们全部落地就刷新，本次刷新的快照会取到
			// 旧路径（如 1.rar），于是：按旧路径读盘 → 已不存在 → 把刚改名的条目误标为失效；
			// 同时磁盘扫描按新路径（2.rar）当成“新文件”再插一条 → 列表里出现重名的一对
			// （一个正常、一个失效）。先用一个 Background 优先级的空操作把 Normal 优先级的
			// 队列排空，确保所有改名同步都已落到条目上，再刷新。
			await UiDispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
			// 文件夹来源重扫最新状态；文件来源已在改名时就地更新，报错条目保留。
			await RefreshFromSources();
		}

		// E3：让满格的进度条停留一小段时间再复位，用户才看得到「走到 100%」的完成瞬间。
		// 停留期间用户若又点了执行，IsExecuting 已是 true，本次复位作废，免得清掉新任务的进度。
		await Task.Delay(ProgressHoldMs);
		if (!IsExecuting)
		{
			ProgressTotal = 0;
			ProgressCurrent = 0;
		}

		// 中断后的收尾询问放在 finally 之后：此时执行状态已复位，可以直接复用撤销逻辑，
		// 日志与批次历史也会由撤销流程一并校正。
		if (interrupted)
		{
			int finishedOk = outcomes.Count(o => o.Success);
			if (finishedOk == 0)
			{
				ShowToast("已中断执行：没有文件被改名", ToastKind.Warning);
			}
			else if (AppDialog.AskRestoreAfterInterrupt(null, finishedOk, built.Plan.Count))
			{
				Undo();
			}
			else
			{
				ShowToast($"已中断执行：保留已完成的 {finishedOk} 项改名", ToastKind.Warning);
			}
		}
	}

	/// <summary>仅重试本次执行中失败的文件：只勾选这些文件后重新执行。</summary>
	private void RetryFailed()
	{
		var targets = Logs
			.Where(l => l.Status == "failed" && l.File is { IsMissing: false })
			.Select(l => l.File!)
			.Distinct()
			.ToList();
		if (targets.Count == 0) return;

		// 只勾选待重试项，避免把本次已成功的文件再改一遍
		var wanted = new HashSet<FileItem>(targets);
		_suspend = true;
		try
		{
			foreach (var f in Files) f.Selected = wanted.Contains(f);
		}
		finally
		{
			_suspend = false;
		}
		RaisePropertyChanged(nameof(SelectedText));
		RaisePropertyChanged(nameof(AnyFileSelected));
		DeleteSelectedCommand.RaiseCanExecuteChanged();
		RecomputeAll();

		if (!CanExecute)
		{
			ShowToast($"{targets.Count} 个失败项无法改名，请检查规则与勾选范围", ToastKind.Error);
			return;
		}
		Execute();
	}

	private async void Undo()
	{
		if (IsExecuting) return;
		var outcomes = _service.Undo();
		Logs.Clear();
		foreach (var o in outcomes)
		{
			Logs.Add(new LogItem
			{
				Original = o.OldName,
				NewName = o.NewFileName,
				Status = o.Success ? "success" : "failed",
				Reason = o.Success ? null : o.Error,
			});
		}
		// 整批撤销成功时，把对应的持久化历史批次同步标记为“已回滚”，保证历史面板与撤销一致
		if (outcomes.Count > 0 && outcomes.All(o => o.Success))
		{
			var entry = History.FirstOrDefault(h => !h.RolledBack);
			if (entry is not null)
			{
				entry.RolledBack = true;
				_redoHistory.Push(entry);
				HistoryStore.Save(History);
				RaisePropertyChanged(nameof(History));
				ShowToast($"已撤销 1 个批次，还原 {outcomes.Count} 个文件");
			}
		}
		await RefreshFromSources();
	}

	private async void Redo()
	{
		if (IsExecuting) return;
		var outcomes = _service.Redo();
		Logs.Clear();
		foreach (var o in outcomes)
		{
			Logs.Add(new LogItem
			{
				Original = o.OldName,
				NewName = o.NewFileName,
				Status = o.Success ? "success" : "failed",
				Reason = o.Success ? null : o.Error,
			});
		}
		// 重做成功时恢复历史批次的“已执行”标记
		if (outcomes.Count > 0 && outcomes.All(o => o.Success) && _redoHistory.Count > 0)
		{
			_redoHistory.Pop().RolledBack = false;
			HistoryStore.Save(History);
			RaisePropertyChanged(nameof(History));
			ShowToast($"已重做 1 个批次，改名 {outcomes.Count} 个文件");
		}
		await RefreshFromSources();
	}

	private void ClearMissingFiles()
	{
		// 一次性清除所有失效条目（保留仍有效的文件）
		_suspend = true;
		try
		{
			foreach (var f in Files.Where(f => f.IsMissing).ToList())
				Files.Remove(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}
}
